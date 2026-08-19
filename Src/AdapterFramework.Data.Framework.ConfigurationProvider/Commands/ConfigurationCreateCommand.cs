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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands;

public class ConfigurationCreateCommand : IConfigurationSetCommand
{
    #region Private Constants

    private const string ConfigurationDoesNotExistString = "Configuration for Component ID: '{0}' and Facet '{1}' already exists and cannot be created. Please use PUT verb to replace it or PATCH verb to patch it.";

    #endregion

    #region Private Fields

    private readonly Func<ConfigurationChangedEventArgs, ICollection<string>> _customValidationFunction;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IConfigurationProtector _configurationProtector;
    private readonly string _componentId;
    private readonly string _facet;
    private readonly Type _configurationType;
    private object _oldValue;
    private object _oldValueNoIds;
    private object _newValue;
    private bool _executed;

    #endregion

    #region Public Constructor

    public ConfigurationCreateCommand(IConfigurationProvider configurationProvider,
        IConfigurationProtector configurationProtector, string componentId, string facet, object newValue,
        Type configurationType, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        _customValidationFunction = customValidationFunction;
        _configurationProvider = configurationProvider;
        _configurationProtector = configurationProtector;
        _configurationType = configurationType;
        _componentId = componentId;
        _newValue = newValue;
        _facet = facet;
    }

    #endregion

    #region Public Properties

    public object OldValue => _executed ? _oldValueNoIds : throw new InvalidOperationException();

    public object OriginalValue => _executed ? _oldValue : throw new InvalidOperationException();

    public object NewValue => _executed ? _newValue : throw new InvalidOperationException();

    #endregion

    #region Public Methods

    public bool TryExecute(ILogger logger, out ICollection<string> errors)
    {
        errors = new List<string>();

        if (_executed)
        {
            errors.Add(ConfigurationCommandHelper.CommandExecutedString);
            return false;
        }

        try
        {
            ConfigurationCommandHelper.GetAndCheckIfConfigurationInvalidOrCorruptAndMove(_configurationProvider, logger, _componentId, _facet, _configurationType, out _oldValue, out _);

            if (_oldValue != null && !_configurationType.IsArray)
            {
                errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationDoesNotExistString, _componentId, _facet));
                return false;
            }

            if (_oldValue != null)
            {
                _oldValueNoIds = ConfigurationCommandHelper.CreateCopy(_oldValue, _configurationType);
                _configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref _oldValueNoIds);
            }

            if (_configurationType.IsArray)
            {
                if (_oldValue is object[] existingValues)
                {
                    AddNewConfigurationEntries(existingValues);
                }
                else
                {
                    CreateConfigurationArray();
                }
            }

            if (!ConfigurationCommandHelper.TryPersistConfiguration(_componentId, _facet, _configurationProvider,
                _customValidationFunction, _configurationType, new ConfigurationChangedEventArgs(_oldValue, _newValue), out errors))
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.UnexpectedExecutionExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages()));
        }
        finally
        {
            _executed = true;

            ReconcileChangesToSecrets(errors.Count == 0);
        }

        return errors.Count == 0;
    }

    public void RollbackChangesToSecrets()
    {
        ReconcileChangesToSecrets(false);
    }

    public bool TryValidate(out ICollection<string> errors)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Private Methods

    private void ReconcileChangesToSecrets(bool commit)
    {
        var valueToReconcile = _newValue ?? _oldValue;
        ConfigurationCommandHelper.ReconcileSecretsForConfiguration(valueToReconcile, _configurationType, _configurationProtector, commit);
    }

    private void CreateConfigurationArray()
    {
        if (_newValue is object[] == false)
        {
            var arrayConfig = new object[1];
            arrayConfig[0] = _newValue;

            _newValue = ArrayToTypedArray(arrayConfig);
        }
    }

    private void AddNewConfigurationEntries(object[] oldEntries)
    {
        if (_newValue is object[] == false)
        {
            var arrayConfig = new object[1];
            arrayConfig[0] = _newValue;
            _newValue = arrayConfig;
        }

        var newValues = (object[])_newValue;

        Array.Resize(ref oldEntries, oldEntries.Length + newValues.Length);
        for (var i = 1; i < newValues.Length + 1; i++)
        {
            oldEntries[^i] = newValues[i - 1];
        }

        _newValue = ArrayToTypedArray(oldEntries);
    }

    private object ArrayToTypedArray(IEnumerable arrayConfig)
    {
        return ConfigurationCommandHelper.CreateCopy(arrayConfig, _configurationType);
    }

    #endregion
}
