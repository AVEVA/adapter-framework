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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.DataProtector;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands;

public class ConfigurationSetSingleCommand : IConfigurationSetCommand
{
    #region Private Fields

    private readonly Func<ConfigurationChangedEventArgs, ICollection<string>> _customValidationFunction;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IConfigurationProtector _configurationProtector;
    private readonly object _executionLock = new object();
    private readonly string _componentId;
    private readonly string _facet;
    private readonly Type _configurationType;
    private readonly object _newValue;
    private object _oldValue;
    private bool _executed;
    private bool _validated;
    private bool _isValid;

    #endregion

    #region Public Constructor

    public ConfigurationSetSingleCommand(IConfigurationProvider configurationProvider,
        IConfigurationProtector configurationProtector, string componentId, string facet, object newValue,
        Type configurationType, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        _customValidationFunction = customValidationFunction;
        _configurationProvider = configurationProvider;
        _configurationProtector = configurationProtector;
        _componentId = componentId;
        _facet = facet;
        _newValue = newValue;
        _configurationType = configurationType;
        _executed = false;
    }

    #endregion

    #region Public Properties

    public object OldValue => _executed ? _oldValue : throw new InvalidOperationException();

    public object OriginalValue => OldValue;

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
                    _configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref _oldValue, _configurationType);
                }

                if (!ConfigurationProtector.TryPreserveMaskedSecret(_oldValue, _newValue, out errors))
                {
                    return false;
                }

                if (!UpdateExistingConfiguration(out errors))
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
                _executed = true;
                _validated = true;

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
        ReconcileChangesToSecrets(false);
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

        if (_newValue.GetType() != _configurationType)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.MismatchedObjectTypeString, _configurationType.Name));
        }

        return errors.Count == 0;
    }

    #endregion
}
