// Copyright 2018-2026 AVEVA Group Limited
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.DataProtector;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands;

public class ConfigurationSetCollectionCommand : IConfigurationSetCommand
{
    #region Private Constants

    private const string UnexpectedExceptionString = "Unable to update configuration for component ID: '{0}' and facet: '{1}'. {2}.";
    private const string MismatchedIdString = "ID '{0}' in the PUT URL does not match configuration ID '{1}' in the JSON payload.";

    #endregion

    #region Private Fields

    private readonly Func<ConfigurationChangedEventArgs, ICollection<string>> _customValidationFunction;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IConfigurationProtector _configurationProtector;
    private readonly object _executionLock = new();
    private readonly string _componentId;
    private readonly string _facet;
    private readonly Type _configurationType;
    private readonly string _id;
    private object _oldValue;
    private object _oldValueNoIds;
    private object _newValue;
    private bool _executed;
    private bool _validated;
    private bool _isValid;

    #endregion

    #region Public Constructor

    public ConfigurationSetCollectionCommand(IConfigurationProvider configurationProvider,
        IConfigurationProtector configurationProtector, string componentId, string facet, object newValue,
        Type configurationType, string id,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        _customValidationFunction = customValidationFunction;
        _configurationProvider = configurationProvider;
        _configurationProtector = configurationProtector;
        _componentId = componentId;
        _facet = facet;
        _newValue = newValue;
        _configurationType = configurationType;
        _id = id;
    }

    #endregion

    #region Public Fields

    public object OldValue => _executed ? _oldValueNoIds : throw new InvalidOperationException();

    public object OriginalValue => _executed ? _oldValue : throw new InvalidOperationException();

    public object NewValue => _executed ? _newValue : throw new InvalidOperationException();

    #endregion

    #region Public Methods

    public bool TryExecute(ILogger logger, out ICollection<string> errors)
    {
        lock (_executionLock)
        {
            errors = new List<string>();

            if (_executed)
            {
                errors.Add(ConfigurationCommandHelper.CommandExecutedString);
                return false;
            }

            try
            {
                ConfigurationCommandHelper.GetAndCheckIfConfigurationInvalidOrCorruptAndMove(_configurationProvider, logger, _componentId, _facet, _configurationType, out _oldValue, out errors);

                if (_oldValue != null)
                {
                    _oldValueNoIds = ConfigurationCommandHelper.CreateCopy(_oldValue, _configurationType);

                    if (_oldValueNoIds is object[] oldValueNoIds)
                    {
                        _configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref oldValueNoIds, _configurationType);
                    }
                }

                if (!UpdateExistingConfiguration(out errors))
                {
                    return false;
                }

                // if _id exists, then already validated in previous method call (single element update)
                if (string.IsNullOrEmpty(_id) && !CheckForSecrets(out errors))
                {
                    return false;
                }

                if (_validated && _isValid)
                {
                    ConfigurationCommandHelper.PersistConfiguration(_componentId, _facet, _configurationProvider, new ConfigurationChangedEventArgs(_oldValue, _newValue), out errors);
                }
                else
                {
                    if (!ConfigurationCommandHelper.TryPersistConfiguration(_componentId, _facet, _configurationProvider, _customValidationFunction,
                        _configurationType, new ConfigurationChangedEventArgs(_oldValue, _newValue), out errors))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.UnexpectedExecutionExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages()));
            }
            finally
            {
                _validated = true;
                _executed = true;

                ReconcileChangesToSecrets(errors.Count == 0);
            }

            return errors.Count == 0;
        }
    }

    public bool TryValidate(out ICollection<string> errors)
    {
        lock (_executionLock)
        {
            errors = new List<string>();

            if (_validated)
            {
                errors.Add(ConfigurationCommandHelper.CommandValidatedString);
                return false;
            }

            try
            {
                ConfigurationCommandHelper.TryGetOrCreateConfiguration(_configurationProvider, _componentId, _facet, _configurationType, out _oldValue, out errors);

                if (!UpdateExistingConfiguration(out errors))
                {
                    return false;
                }

                if (!ConfigurationCommandHelper.TryValidateConfiguration(_configurationProvider, _customValidationFunction,
                    new ConfigurationChangedEventArgs(_oldValue, _newValue), out errors))
                {
                    return false;
                }

                if (!ConfigurationCommandHelper.HasUniqueIdentifiersDefined(_configurationType, _newValue, out errors))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.UnexpectedValidationExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages()));
            }
            finally
            {
                _validated = true;
                if (errors.Count != 0)
                {
                    ReconcileChangesToSecrets(false);
                }
            }

            return _isValid = errors.Count == 0;
        }
    }

    public void RollbackChangesToSecrets()
    {
        lock (_executionLock)
        {
            ReconcileChangesToSecrets(false);
        }
    }

    #endregion

    #region Private Methods

    private void ReconcileChangesToSecrets(bool commit)
    {
        var valueToReconcile = _newValue ?? _oldValue;
        ConfigurationCommandHelper.ReconcileSecretsForConfiguration(valueToReconcile, _configurationType, _configurationProtector, commit);
    }

    private bool UpdateExistingConfiguration(out ICollection<string> errors)
    {
        errors = new List<string>();

        if (_validated)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(_id))
        {
            var oldValue = ConfigurationCommandHelper.CreateCopy(_oldValue, _configurationType);
            if (!TryUpdateExistingConfigurationEntry(oldValue, _configurationType, out errors))
            {
                return false;
            }
        }

        if (_newValue.GetType() != _configurationType)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.MismatchedArrayTypeString, _facet));
            return false;
        }

        return errors.Count == 0;
    }

    private bool TryUpdateExistingConfigurationEntry(object oldValue, Type configurationType, out ICollection<string> errors)
    {
        errors = new List<string>();

        if (oldValue is not object[] oldEntries)
        {
            oldEntries = Array.Empty<object>();
        }

        var configurationEntryType = configurationType.GetElementType();

        if (_newValue.GetType() != configurationEntryType)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.MismatchedObjectTypeString, configurationEntryType?.Name));
            return false;
        }

        try
        {
            var idAttribute = ConfigurationHelper.GetIdProperty(configurationEntryType);
            var boxedNewIdValue = idAttribute.GetValue(_newValue);
            if (boxedNewIdValue is string newIdValue && !newIdValue.Equals(_id, StringComparison.InvariantCultureIgnoreCase))
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, MismatchedIdString, _id, newIdValue));
                return false;
            }

            var entryFound = false;
            for (var i = 0; i < oldEntries.Length; i++)
            {
                var boxedPropertyValue = idAttribute.GetValue(oldEntries[i]);
                if (boxedPropertyValue == null)
                {
                    continue;
                }

                var stringValue = (string)boxedPropertyValue;
                if (_id.Equals(stringValue, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!UpdateExistingEntry(oldEntries, idAttribute, i, out errors))
                    {
                        return false;
                    }

                    entryFound = true;
                    break;
                }
            }

            if (!entryFound)
            {
                AddNewEntry(ref oldEntries, idAttribute);
                ConfigurationProtector.TryPreserveMaskedSecret(null, oldEntries[^1], out errors);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, UnexpectedExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages()));
            return false;
        }
    }

    private bool UpdateExistingEntry(IList<object> oldEntries, PropertyInfo idAttribute, int position, out ICollection<string> errors)
    {
        var result = ConfigurationProtector.TryPreserveMaskedSecret(oldEntries[position], _newValue, out errors);
        idAttribute.SetValue(_newValue, _id);
        oldEntries[position] = _newValue;
        _newValue = oldEntries;
        return result;
    }

    private void AddNewEntry(ref object[] oldEntries, PropertyInfo idAttribute)
    {
        Array.Resize(ref oldEntries, oldEntries.Length + 1);

        idAttribute.SetValue(_newValue, _id);
        oldEntries[^1] = _newValue;
        _newValue = ConfigurationCommandHelper.CreateCopy(oldEntries, _configurationType);
    }

    private bool CheckForSecrets(out ICollection<string> errors)
    {
        errors = new List<string>();

        if (_newValue is not object[] newEntries)
        {
            return true;
        }

        if (_oldValue is not object[] oldEntries)
        {
            oldEntries = Array.Empty<object>();
        }

        var configurationEntryType = _configurationType.GetElementType();

        if (_newValue.GetType() != _configurationType)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.MismatchedObjectTypeString, configurationEntryType?.Name));
            return false;
        }

        if (!ConfigurationCommandHelper.GetProtectedProperties(configurationEntryType).Any())
        {
            return true;
        }

        try
        {
            var newConfigDict = new Dictionary<string, object>();
            var idAttribute = ConfigurationHelper.GetIdProperty(configurationEntryType);
            
            for (var i = 0; i < newEntries.Length; i++)
            {
                if (ConfigurationProtector.HasMaskedSecrets(newEntries[i]))
                {
                    var boxedPropertyValue = idAttribute.GetValue(newEntries[i]);
                    if (boxedPropertyValue == null)
                    {
                        continue;
                    }

                    newConfigDict[(string)boxedPropertyValue] = newEntries[i];
                }
            }

            if (newConfigDict.IsEmpty())
            {
                return true;
            }

            for (var i = 0; i < oldEntries.Length; i++)
            {
                var boxedPropertyValue = idAttribute.GetValue(oldEntries[i]);
                if (boxedPropertyValue == null)
                {
                    continue;
                }

                var stringValue = (string)boxedPropertyValue;

                if (newConfigDict.Remove(stringValue, out var newEntry))
                {
                    if (!ConfigurationProtector.TryPreserveMaskedSecret(oldEntries[i], newEntry, out errors))
                    {
                        return false;
                    }
                }
            }

            // List of configurations with secrets that didn't have an older version.
            foreach (var item in newConfigDict.Values)
            {
                if (!ConfigurationProtector.TryPreserveMaskedSecret(null, item, out errors))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, UnexpectedExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages()));
            return false;
        }
    }

    #endregion
}
