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
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands;

public class ConfigurationCommandGenerator : IConfigurationCommandGenerator
{
    #region Private Fields

    private static readonly JsonSerializerOptions _jsonSerializerOptions = ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter;

    #endregion

    #region Private Properties

    private readonly IConfigurationProvider _configurationProvider;
    private readonly string _componentId;
    private readonly string _facet;

    #endregion

    #region Public Constructor

    public ConfigurationCommandGenerator(IConfigurationProvider configurationProvider, string componentId, string facet)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));

        if (facet.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("The argument cannot contain any white space.", nameof(facet));
        }

        _configurationProvider = configurationProvider;
        _componentId = componentId;
        _facet = facet;
    }

    #endregion

    #region Public Methods

    public IConfigurationGetCommand GenerateConfigurationGetCommand(Type configurationType, string id = null)
    {
        return new ConfigurationGetCommand(_configurationProvider, _componentId, _facet, configurationType, id);
    }

    public IConfigurationSetPatchCommand GenerateConfigurationPatchCommand(JsonElement patches, Type configurationType, string id,
        IConfigurationProtector configurationProtector, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        return new ConfigurationPatchCommand(_configurationProvider, configurationProtector, _componentId, _facet, id, patches, configurationType, customValidationFunction);
    }

    public IConfigurationSetCommand GenerateConfigurationDeleteCommand(Type configurationType, string id, Func<ConfigurationChangedEventArgs,
        ICollection<string>> customValidationFunction)
    {
        return new ConfigurationDeleteCommand(_configurationProvider, _componentId, _facet, id, configurationType, customValidationFunction);
    }

    public IConfigurationSetCommand GenerateConfigurationCreateCommand(JsonElement newValue, Type configurationType, IConfigurationProtector configurationProtector,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        ThrowHelper.ThrowIfArgumentNull(newValue, nameof(newValue));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));
        ThrowHelper.ThrowIfArgumentNull(configurationProtector, nameof(configurationProtector));

        if (configurationType.IsArray)
        {
            var typedNewValue = newValue.ValueKind == JsonValueKind.Array
                ? GetTypedValueFromJArray(newValue, configurationType, configurationProtector)
                : GetTypedArrayValueFromJObject(newValue, configurationType, configurationProtector, null);

            return new ConfigurationCreateCommand(_configurationProvider, configurationProtector, _componentId, _facet, typedNewValue, configurationType, customValidationFunction);
        }
        else
        {
            var typedNewValue = GetTypedValueFromJObject(newValue, configurationType, configurationProtector);

            return new ConfigurationCreateCommand(_configurationProvider, configurationProtector, _componentId, _facet, typedNewValue, configurationType, customValidationFunction);
        }
    }

    public IConfigurationSetCommand GenerateConfigurationSetCommand(JsonElement newValue, Type configurationType, string id, IConfigurationProtector configurationProtector,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        ThrowHelper.ThrowIfArgumentNull(newValue, nameof(newValue));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));
        ThrowHelper.ThrowIfArgumentNull(configurationProtector, nameof(configurationProtector));

        if (configurationType.IsArray)
        {
            var typedNewValue = newValue.ValueKind == JsonValueKind.Array
                ? GetTypedValueFromJArray(newValue, configurationType, configurationProtector)
                : GetTypedArrayValueFromJObject(newValue, configurationType, configurationProtector, id);

            return new ConfigurationSetCollectionCommand(_configurationProvider, configurationProtector, _componentId, _facet, typedNewValue, configurationType, id, customValidationFunction);
        }
        else
        {
            var typedNewValue = GetTypedValueFromJObject(newValue, configurationType, configurationProtector);

            return new ConfigurationSetSingleCommand(_configurationProvider, configurationProtector, _componentId, _facet, typedNewValue, configurationType, customValidationFunction);
        }
    }

    #endregion

    #region Private Methods

    private object GetTypedArrayValueFromJObject(JsonElement newValue, Type configurationType, IConfigurationProtector configurationProtector, string indexValue)
    {
        var elementType = configurationType.GetElementType();
        var typedNewValue = JsonSerializer.Deserialize(newValue.GetRawText(), elementType, _jsonSerializerOptions);

        if (TryPopulateIndexProperty(typedNewValue, indexValue, elementType))
        {
            configurationProtector.ProtectSecrets(ref typedNewValue, configurationType, _componentId, _facet);
        }

        return typedNewValue;
    }

    private object GetTypedValueFromJObject(JsonElement newValue, Type configurationType, IConfigurationProtector configurationProtector)
    {
        var typedNewValue = JsonSerializer.Deserialize(newValue.GetRawText(), configurationType, _jsonSerializerOptions);
        configurationProtector.ProtectSecrets(ref typedNewValue, configurationType, _componentId, _facet);

        return typedNewValue;
    }

    private object GetTypedValueFromJArray(JsonElement newValue, Type configurationType, IConfigurationProtector configurationProtector)
    {
        var typedNewValue = JsonSerializer.Deserialize(newValue.GetRawText(), configurationType, _jsonSerializerOptions);

        if (TryPopulateIndexProperty(typedNewValue, null, configurationType))
        {
            if (typedNewValue is object[] typedNewValueArray)
            {
                configurationProtector.ProtectSecrets(ref typedNewValueArray, configurationType, _componentId, _facet);
            }
        }

        return typedNewValue;
    }

    /// <summary>
    /// Calls validation function <see cref="IValidatableObject"/> on the configuration to populate Index property in case it's generated dynamically.
    /// </summary>
    /// <param name="configuration">Configuration value.</param>
    /// <param name="indexValue">Configuration index specified in URL.</param>
    /// <param name="configurationType">Configuration type.</param>
    /// <returns>true if configuration is valid when index is populated, false otherwise.</returns>
    private bool TryPopulateIndexProperty(object configuration, string indexValue, Type configurationType)
    {
        var configurationEntryType = configurationType.IsArray ? configurationType.GetElementType() : configurationType;

        if (!ConfigurationHelper.GetProtectedPropertyInfos(configurationEntryType).Any())
        {
            return true;
        }

        if (configuration is not object[])
        {
            if (!string.IsNullOrEmpty(indexValue))
            {
                var indexPropertyInfo = ConfigurationHelper.GetIdProperty(configurationEntryType);

                var existingIndex = (string)indexPropertyInfo.GetValue(configuration);

                if (existingIndex == null)
                {
                    indexPropertyInfo.SetValue(configuration, indexValue);
                }

                return true;
            }
        }

        return _configurationProvider.IsConfigurationValid(configuration, out _);
    }

    #endregion
}
