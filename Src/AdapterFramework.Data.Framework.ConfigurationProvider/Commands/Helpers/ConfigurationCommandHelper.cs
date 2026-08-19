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
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ConfigurationProvider.Converters;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;

public static class ConfigurationCommandHelper
{
    #region Public Constants

    public const string CommandExecutedString = "The command was already executed.";
    public const string CommandValidatedString = "The command was already validated.";
    public const string UnexpectedExecutionExceptionString = "Unable to execute configuration command for component ID: '{0}' and facet: '{1}'. {2}";
    public const string UnexpectedValidationExceptionString = "Unable to validate configuration command for component ID: '{0}' and facet: '{1}'. {2}.";
    public const string MismatchedObjectTypeString = "Type mismatch - Expected type {0}.";
    public const string MismatchedArrayTypeString = "Type mismatch - facet {0} requires array configuration.";

    #endregion

    #region Public Properties

    public static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new FlagEnumConverter<StreamProperties>(() => StreamProperties.All),
            new JsonStringEnumConverter(),
            new StringToBoolConverter(),
            new StringToTimeSpanConverter(),
        },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static readonly JsonSerializerOptions SerializerOptionsWithCustomEnumConverter = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new FlagEnumConverter<StreamProperties>(() => StreamProperties.All),
            new JsonStringEnumConverterFactory(),
            new StringToBoolConverter(),
            new StringToTimeSpanConverter(),
        },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    #endregion

    #region Public Methods

    public static void ReconcileSecretsForConfiguration(object configuration, Type configurationType, IConfigurationProtector configurationProtector, bool commit)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProtector, nameof(configurationProtector));

        if (configuration == null)
        {
            return;
        }

        if (configuration is object[] configurationArray)
        {
            configurationProtector.ReconcileSecretsChange(ref configurationArray, configurationType, commit);
        }
        else
        {
            configurationProtector.ReconcileSecretsChange(ref configuration, configurationType, commit);
        }
    }

    public static IEnumerable<PropertyInfo> GetProtectedProperties(Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        return configurationType.GetProperties().Where(propertyInfo => Attribute.IsDefined(propertyInfo, typeof(ProtectedAttribute)));
    }

    public static bool TryPersistConfiguration(string componentId, string facetName, IConfigurationProvider configurationProvider,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction, Type configurationType,
        ConfigurationChangedEventArgs configurations, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));

        if (configurationProvider.IsConfigurationValid(configurations.NewValue, out errors))
        {
            if (ExecuteCustomValidationFunction(customValidationFunction, configurations, out errors))
            {
                if (HasUniqueIdentifiersDefined(configurationType, configurations.NewValue, out errors))
                {
                    PersistConfiguration(componentId, facetName, configurationProvider, configurations, out errors);
                    return errors.Count == 0;
                }
            }

            return false;
        }

        return false;
    }

    public static void PersistConfiguration(string componentId, string facetName, IConfigurationProvider configurationProvider, ConfigurationChangedEventArgs configurations, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));

        configurationProvider.TrySaveConfiguration(componentId, facetName, configurations.NewValue, out errors);
    }

    public static bool TryValidateConfiguration(IConfigurationProvider configurationProvider,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction,
        ConfigurationChangedEventArgs configurations, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));

        if (configurationProvider.IsConfigurationValid(configurations.NewValue, out errors))
        {
            if (ExecuteCustomValidationFunction(customValidationFunction, configurations, out errors))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    public static object CreateCopy(object configurationObject, Type configurationType)
    {
        if (configurationObject == null)
        {
            return null;
        }

        var jsonSerialize = JsonSerializer.Serialize(configurationObject, configurationObject.GetType(), SerializerOptions);
        return JsonSerializer.Deserialize(jsonSerialize, configurationType, SerializerOptions);
    }

    public static bool ExecuteCustomValidationFunction(Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction,
        ConfigurationChangedEventArgs configurations, out ICollection<string> errors)
    {
        errors = new List<string>();

        if (customValidationFunction != null)
        {
            var validationResults = customValidationFunction.Invoke(configurations);

            if (validationResults.IsNullOrEmpty())
            {
                return true;
            }

            errors = validationResults;
        }

        return errors.Count == 0;
    }

    public static bool HasUniqueIdentifiersDefined(Type configurationType, object configurationObject, out ICollection<string> errors)
    {
        errors = new List<string>();

        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        if (configurationType.IsArray)
        {
            var idProperty = ConfigurationHelper.GetIdProperty(configurationType.GetElementType());

            if (idProperty != null)
            {
                if (configurationObject is object[] configurationObjectArray)
                {
                    var existingIds = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var configurationEntry in configurationObjectArray)
                    {
                        var id = idProperty.GetValue(configurationEntry);

                        if (id == null)
                        {
                            errors.Add($"Configuration object does not have ID specified. ID property name: '{idProperty.Name}'.");
                            return false;
                        }

                        if (id is string stringId)
                        {
                            if (!existingIds.Add(stringId))
                            {
                                errors.Add($"Configuration object contains duplicate IDs. ID property name: '{idProperty.Name}' Duplicate value: '{stringId}'.");
                                return false;
                            }
                        }
                    }
                }
            }
        }

        return true;
    }

    public static bool TryGetOrCreateConfiguration(IConfigurationProvider configurationProvider, string componentId, string configurationName, Type configurationType, out object existingValue, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        
        if (configurationProvider.TryGetConfiguration(componentId, configurationName, configurationType, out existingValue, out errors))
        {
            return true;
        }

        existingValue = null;

        return false;
    }

    public static void GetAndCheckIfConfigurationInvalidOrCorruptAndMove(IConfigurationProvider configurationProvider, ILogger logger, string componentId, string configurationName, Type configurationType, out object existingValue, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));

        if (!TryGetOrCreateConfiguration(configurationProvider, componentId, configurationName, configurationType, out existingValue, out errors))
        {
            if (!errors.IsNullOrEmpty())
            {
                logger?.LogWarning(EdgeSystemConstants.OriginalConfigurationInvalidMessage, configurationName, errors);

                if (!configurationProvider.TryMoveCorruptedConfiguration(componentId, configurationName, out var errorMessage))
                {
                    logger?.LogError(EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage, configurationName, errorMessage);
                }
            }
        }
    }

    #endregion
}
