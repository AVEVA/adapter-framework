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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers.ConfigurationCommandHelper;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands;

public class ConfigurationDeleteCommand : IConfigurationSetCommand
{
    #region Private Constants

    private const string ConfigurationNotFoundString = "Configuration for component ID: '{0}' and facet: '{1}' wasn't found. Cannot perform delete operation.";
    private const string ConfigurationEntryNotFoundString = "Configuration entry with ID: '{0}' wasn't found. Configuration wasn't changed.";

    #endregion

    #region Private Fields

    private readonly Func<ConfigurationChangedEventArgs, ICollection<string>> _customValidationFunction;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly string _componentId;
    private readonly string _facet;
    private readonly string _id;
    private readonly Type _configurationType;
    private bool _executed;
    private object _oldValue;
    private object _newValue;

    #endregion

    #region Public Constructor

    public ConfigurationDeleteCommand(IConfigurationProvider configurationProvider, string componentId,
        string facet, string id, Type configurationType, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        _configurationProvider = configurationProvider;
        _componentId = componentId;
        _facet = facet;
        _id = id;
        _configurationType = configurationType;
        _customValidationFunction = customValidationFunction;
    }

    #endregion

    #region Public Methods

    public object OldValue => OriginalValue;
    public object OriginalValue => _executed ? _oldValue : throw new InvalidOperationException();
    public object NewValue => _executed ? _newValue : throw new InvalidOperationException();

    public bool TryExecute(ILogger logger, out ICollection<string> errors)
    {
        errors = new List<string>();

        if (_executed)
        {
            errors.Add(CommandExecutedString);
            return false;
        }

        try
        {
            _oldValue = _configurationProvider.GetConfiguration(_componentId, _facet, _configurationType);

            if (string.IsNullOrEmpty(_id))
            {
                if (ExecuteCustomValidationFunction(_customValidationFunction, new ConfigurationChangedEventArgs(_oldValue, null), out errors))
                {
                    _configurationProvider.DeleteConfiguration(_componentId, _facet);
                    _newValue = null;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (!TryDeleteIndexedConfigurationEntry(out errors))
                {
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, UnexpectedExecutionExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages()));
        }
        finally
        {
            _executed = true;
        }

        return errors.Count == 0;
    }

    public bool TryValidate(out ICollection<string> errors)
    {
        throw new NotImplementedException();
    }

    public void RollbackChangesToSecrets()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// This method removes entry from passed array instance.
    /// </summary>
    /// <typeparam name="T">Type of the array.</typeparam>
    /// <param name="array">Array to be changed.</param>
    /// <param name="index">Index to remove an element at.</param>
    private static void RemoveAt<T>(ref T[] array, int index)
    {
        if (index > array.Length) return;

        for (var i = index; i < array.Length - 1; i++)
        {
            array[i] = array[i + 1];
        }

        Array.Resize(ref array, array.Length - 1);
    }

    private bool TryDeleteIndexedConfigurationEntry(out ICollection<string> errors)
    {
        errors = new List<string>();

        var oldValue = CreateCopy(_oldValue, _configurationType);
        if (oldValue == null || !(oldValue is object[] oldEntries))
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationNotFoundString, _componentId, _facet));
            return false;
        }

        var configurationEntryType = _configurationType.GetElementType();

        var idAttribute = ConfigurationHelper.GetIdProperty(configurationEntryType);
        var entryFound = false;
        for (var i = 0; i < oldEntries.Length; i++)
        {
            var boxedPropertyValue = idAttribute.GetValue(oldEntries[i]);
            if (boxedPropertyValue == null)
            {
                continue;
            }

            var stringValue = boxedPropertyValue as string;
            if (_id.Equals(stringValue, StringComparison.InvariantCultureIgnoreCase))
            {
                RemoveAt(ref oldEntries, i);
                entryFound = true;
                break;
            }
        }

        if (!entryFound)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationEntryNotFoundString, _id));
            return false;
        }

        _newValue = CreateCopy(oldEntries, _configurationType);

        return TryPersistConfiguration(_componentId, _facet, _configurationProvider,
            _customValidationFunction, _configurationType, new ConfigurationChangedEventArgs(_oldValue, _newValue), out errors);
    }

    #endregion
}
