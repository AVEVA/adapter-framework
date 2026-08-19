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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands;

public class ConfigurationGetCommand : IConfigurationGetCommand
{
    #region Private Constants

    private const string InvalidDataTypeString = "Property name: '{0}' marked with protected attribute is of invalid type: {1} specified for protected attribute. Protected attribute must be of string type.";

    #endregion

    #region Private Fields

    private readonly IConfigurationProvider _configurationProvider;
    private readonly string _componentId;
    private readonly string _facet;
    private readonly Type _configurationType;
    private readonly string _id;
    private object _existingValue;
    private bool _executed;

    #endregion

    #region Public Constructor

    public ConfigurationGetCommand(IConfigurationProvider configurationProvider, string componentId,
        string facet, Type configurationType, string id)
    {
        _configurationProvider = configurationProvider;
        _componentId = componentId;
        _id = id;
        _facet = facet;
        _configurationType = configurationType;
    }

    #endregion

    #region Public Fields

    public object Value => _executed ? _existingValue : throw new InvalidOperationException();

    #endregion

    #region Public Methods

    public bool TryExecute(out object value, out ICollection<string> errors)
    {
        errors = new List<string>();
        value = null;

        if (_executed)
        {
            errors.Add(ConfigurationCommandHelper.CommandExecutedString);
            return false;
        }

        try
        {
            _existingValue = _configurationProvider.GetConfiguration(_componentId, _facet, _configurationType);

            // This was added only to trigger custom validation and migrate configuration also on the way out.
            _configurationProvider.IsConfigurationValid(_existingValue, out _);

            if (!string.IsNullOrWhiteSpace(_id) && _existingValue is Array existingEnumerableConfiguration)
            {
                return TryGetConfigurationEntry(ref value, existingEnumerableConfiguration, out errors);
            }

            value = _existingValue;
        }
        catch (Exception ex)
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.UnexpectedExecutionExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages()));
        }
        finally
        {
            _executed = true;
        }

        return errors.Count == 0;
    }

    #endregion

    #region Private Methods

    private bool TryGetConfigurationEntry(ref object value, IEnumerable existingEnumerableConfiguration, out ICollection<string> errors)
    {
        errors = new List<string>();

        var idAttribute = ConfigurationHelper.GetIdProperty(_configurationType.GetElementType());

        if (idAttribute.PropertyType != typeof(string))
        {
            errors.Add(string.Format(CultureInfo.InvariantCulture, InvalidDataTypeString, idAttribute.Name, idAttribute.PropertyType));
            return false;
        }

        var entryFound = false;
        foreach (var configurationEntry in existingEnumerableConfiguration)
        {
            var boxedPropertyValue = idAttribute.GetValue(configurationEntry);
            if (boxedPropertyValue == null)
            {
                continue;
            }

            var stringValue = boxedPropertyValue as string;
            if (_id.Equals(stringValue, StringComparison.InvariantCultureIgnoreCase))
            {
                _existingValue = configurationEntry;
                entryFound = true;
                break;
            }
        }

        if (!entryFound)
        {
            value = null;
            return false;
        }

        value = _existingValue;
        return true;
    }

    #endregion
}
