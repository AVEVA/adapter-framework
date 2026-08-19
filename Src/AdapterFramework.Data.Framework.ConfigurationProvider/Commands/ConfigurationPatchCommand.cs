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
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Commands;

public class ConfigurationPatchCommand : IConfigurationSetPatchCommand
{
    #region Private Constants

    private const string ConfigurationNotFoundString = "Configuration for component ID: '{0}' and facet: '{1}' wasn't found. Cannot perform patch operation.";
    private const string ConfigurationNotFoundStringSingle = "Configuration for component ID: '{0}' and facet: '{1}' is either empty or cannot be found. Cannot perform patch operation.";
    private const string ConfigurationEntryNotFoundString = "Configuration entry with ID: '{0}' wasn't found. Configuration wasn't changed.";
    private const string UnexpectedExceptionString = "Unable to patch configuration for component ID: '{0}' and facet: '{1}'. {2}";
    private const string UnexpectedPropertiesString = "Unable to patch configuration for component ID: '{0}' and facet: '{1}'. Unknown properties were passed.";
    private const string IdCannotBeChangedString = "Unable to patch configuration for component ID: '{0}' and facet: '{1}'. Property '{2}' is ID and cannot be changed. Property should be removed from the payload.";
    private const string UnexpectedFormatString = "Unable to convert patch arguments to a JObject or JArray.";
    private const string NoIdArrayPatchString = "Patch is only allowed on array configurations where each item has an ID.";
    private const string NoIdSpecifiedArrayPatchString = "In order to patch items in an array configuration, each item must have its respective unique identifier specified in the body of the request.";
    private const string CannotUseBothIdAndArrayString = "Patch can be performed on a single item if that item has an ID and it is specified in the URL or multiple items if an array is supplied in the body of the request and each item includes a unique identifier, but not both.";

    #endregion

    #region Private Fields

    private static readonly JsonSerializerOptions _jsonSerializerOptions = ConfigurationCommandHelper.SerializerOptions;

    private readonly Func<ConfigurationChangedEventArgs, ICollection<string>> _customValidationFunction;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IConfigurationProtector _configurationProtector;
    private readonly string _componentId;
    private readonly string _facet;
    private readonly JsonElement _patches;
    private readonly Type _configurationType;
    private readonly string _id;
    private readonly bool _isArray;
    private object _newValue;
    private object _oldValue;
    private object _oldValueNoIds;
    private bool _executed;

    #endregion

    #region Public Constructor

    public ConfigurationPatchCommand(IConfigurationProvider configurationProvider, IConfigurationProtector configurationProtector, string componentId,
        string facet, string id, JsonElement patches, Type configurationType, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction)
    {
        _configurationProvider = configurationProvider;
        _configurationProtector = configurationProtector;
        _customValidationFunction = customValidationFunction;
        _componentId = componentId;
        _id = id;
        _facet = facet;
        _configurationType = configurationType;
        _patches = patches;
        _executed = false;
        _isArray = IsArray();
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
        errors = new List<string>();
        var result = TryExecute(logger, out ICollection<(int StatusCode, string ErrorMessage)> errorsTuple);
        foreach (var error in errorsTuple)
        {
            errors.Add(error.ErrorMessage);
        }

        return result;
    }

    public bool TryExecute(ILogger logger, out ICollection<(int StatusCode, string ErrorMessage)> errors)
    {
        errors = new List<(int StatusCode, string ErrorMessage)>();
        if (_executed)
        {
            errors.Add((StatusCodes.Status400BadRequest, ConfigurationCommandHelper.CommandExecutedString));
            return false;
        }

        try
        {
            _oldValue = _configurationProvider.GetConfiguration(_componentId, _facet, _configurationType);
            if (_oldValue == null)
            {
                errors.Add((StatusCodes.Status409Conflict, string.Format(CultureInfo.InvariantCulture, ConfigurationNotFoundStringSingle, _componentId, _facet)));
                return false;
            }

            _oldValueNoIds = ConfigurationCommandHelper.CreateCopy(_oldValue, _configurationType);

            if (_oldValueNoIds is object[] oldValueNoIdsArray)
            {
                _configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref oldValueNoIdsArray, _configurationType);
            }
            else
            {
                _configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref _oldValueNoIds, _configurationType);
            }

            var oldValue = ConfigurationCommandHelper.CreateCopy(_oldValue, _configurationType);
            if (!string.IsNullOrWhiteSpace(_id) || _isArray)
            {
                if (!TryPatchExistingConfigurationEntry(oldValue, _configurationType.GetElementType(), _patches, out _newValue, out errors))
                {
                    return false;
                }
            }
            else
            {
                if (_patches.ValueKind == JsonValueKind.Object)
                {
                    if (!TryPatchConfiguration(oldValue, _configurationType, _patches, out _newValue, out errors))
                    {
                        return false;
                    }
                }
                else
                {
                    errors.Add((StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, UnexpectedExceptionString, _componentId, _facet, UnexpectedFormatString)));
                    return false;
                }
            }

            if (!ConfigurationCommandHelper.TryPersistConfiguration(_componentId, _facet, _configurationProvider,
                _customValidationFunction, _configurationType, new ConfigurationChangedEventArgs(_oldValue, _newValue), out var persistErrors))
            {
                errors = AddCollections(errors, persistErrors);
                return false;
            }
        }
        catch (Exception ex)
        {
            errors.Add((StatusCodes.Status409Conflict, string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.UnexpectedExecutionExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages())));
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

    private static HashSet<string> GetJsonPropertyNameSet(JsonElement jsonElement)
    {
        // JsonElement TryGetProperty is case-sensitive
        // We create a set of JsonElement property names to be compared with property names of the type
        var jsonPropertyNameSet = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var jsonPropertyName in jsonElement.EnumerateObject())
        {
            jsonPropertyNameSet.Add(jsonPropertyName.Name);
        }

        return jsonPropertyNameSet;
    }

    private static bool TryGetJsonElementPropertyValue(PropertyInfo property, JsonElement jsonElement, HashSet<string> jsonPropertyNameSet, out object propertyValue)
    {
        propertyValue = null;
        string jsonPropertyName = jsonPropertyNameSet.TryGetValue(property.Name, out jsonPropertyName) ? jsonPropertyName : string.Empty;
        if (jsonElement.TryGetProperty(jsonPropertyName, out var value))
        {
            var converterType = property.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType;
            if (converterType != null)
            {
                var converter = (JsonConverter)Activator.CreateInstance(converterType);
                var serializerOptions = new JsonSerializerOptions() { Converters = { converter } };

                propertyValue = JsonSerializer.Deserialize(value.GetRawText(), property.PropertyType, serializerOptions);
            }
            else
            {
                propertyValue = JsonSerializer.Deserialize(value.GetRawText(), property.PropertyType, _jsonSerializerOptions);
            }

            return true;
        }

        return false;
    }

    private static ICollection<(int StatusCode, string ErrorMessage)> AddCollections(ICollection<(int StatusCode, string ErrorMessage)> destination, ICollection<(int StatusCode, string ErrorMessage)> source)
    {
        foreach (var stringItem in source)
        {
            destination.Add(stringItem);
        }

        return destination;
    }

    private static ICollection<(int StatusCode, string ErrorMessage)> AddCollections(ICollection<(int StatusCode, string ErrorMessage)> destination, ICollection<string> source)
    {
        foreach (var stringItem in source)
        {
            destination.Add((StatusCodes.Status409Conflict, stringItem));
        }

        return destination;
    }

    private void ReconcileChangesToSecrets(bool commit)
    {
        var valueToReconcile = _newValue ?? _oldValue;
        ConfigurationCommandHelper.ReconcileSecretsForConfiguration(valueToReconcile, _configurationType, _configurationProtector, commit);
    }

    private bool IsArray() => string.IsNullOrEmpty(_id) && _configurationType.IsArray;

    private bool TryPatchExistingConfigurationEntry(object oldConfigurationEntries, Type configurationEntryType, JsonElement patches, out object patchedConfiguration, out ICollection<(int StatusCode, string ErrorMessage)> errors)
    {
        errors = new List<(int StatusCode, string ErrorMessage)>();
        patchedConfiguration = null;

        if (oldConfigurationEntries == null || oldConfigurationEntries is not object[] oldEntries)
        {
            errors.Add((StatusCodes.Status409Conflict, string.Format(CultureInfo.InvariantCulture, ConfigurationNotFoundString, _id, _facet)));
            return false;
        }

        var idAttribute = ConfigurationHelper.GetIdProperty(configurationEntryType);

        object[] newEntries = null;
        var result = true;

        if (patches.ValueKind == JsonValueKind.Array)
        {
            if (idAttribute == null)
            {
                errors.Add((StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, UnexpectedExceptionString, _componentId, _facet, NoIdArrayPatchString)));
                return false;
            }

            if (!_isArray)
            {
                errors.Add((StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, UnexpectedExceptionString, _componentId, _facet, CannotUseBothIdAndArrayString)));
                return false;
            }

            foreach (var patch in patches.EnumerateArray())
            {
                result &= TryPatchExistingArrayItem(idAttribute, oldEntries, configurationEntryType, patch, out newEntries, out var innerErrors);
                errors = AddCollections(errors, innerErrors);

                if (newEntries != null)
                {
                    oldEntries = newEntries;
                }
            }
        }
        else
        {
            result = TryPatchExistingArrayItem(idAttribute, oldEntries, configurationEntryType, patches, out newEntries, out errors);
        }

        patchedConfiguration = newEntries;

        return result;
    }

    private bool TryPatchConfiguration(object configurationToPatch, Type configurationType, JsonElement patches, out object patchedConfiguration, out ICollection<(int StatusCode, string ErrorMessage)> errors, string idAttributeName = null)
    {
        errors = new List<(int StatusCode, string ErrorMessage)>();
        patchedConfiguration = configurationToPatch;

        try
        {
            var properties = configurationType.GetProperties();

            var protectedProperties = ConfigurationCommandHelper.GetProtectedProperties(configurationType).ToList();
            var objectPatched = false;

            var jsonPropertyNameSet = GetJsonPropertyNameSet(patches);

            foreach (var property in properties)
            {
                if (TryGetJsonElementPropertyValue(property, patches, jsonPropertyNameSet, out var propertyValue))
                {
                    if (idAttributeName != null && property.Name.Equals(idAttributeName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (_isArray)
                        {
                            continue;
                        }

                        errors.Add((StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, IdCannotBeChangedString, _componentId, _facet, idAttributeName)));
                        return false;
                    }

                    foreach (var protectedProperty in protectedProperties)
                    {
                        if (property.Name.Equals(protectedProperty.Name, StringComparison.InvariantCultureIgnoreCase))
                        {
                            if (propertyValue != null)
                            {
                                var generatedId = ConfigurationHelper.GenerateProtectedPropertyId(configurationToPatch, _configurationType.IsArray, _componentId, _facet, property.Name);
                                propertyValue = _configurationProtector.SecretsManager.Protect(propertyValue.ToString(), _componentId, generatedId);
                            }

                            break;
                        }
                    }

                    objectPatched = true;
                    property.SetValue(patchedConfiguration, propertyValue);
                }
            }

            if (!objectPatched)
            {
                errors.Add((StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, UnexpectedPropertiesString, _componentId, _facet)));
                return false;
            }
        }
        catch (Exception ex)
        {
            errors.Add((StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, UnexpectedExceptionString, _componentId, _facet, ex.GetExceptionTypeAndMessages())));
            return false;
        }

        return true;
    }

    private bool TryPatchExistingArrayItem(PropertyInfo idAttribute, object[] oldEntries, Type configurationEntryType, JsonElement patches, out object[] newEntries, out ICollection<(int StatusCode, string ErrorMessage)> errors)
    {
        errors = new List<(int StatusCode, string ErrorMessage)>();
        newEntries = null;
        var id = _id;

        var jsonPropertyNames = GetJsonPropertyNameSet(patches);

        var entryFound = false;
        for (var i = 0; i < oldEntries.Length; i++)
        {
            var boxedPropertyValue = idAttribute.GetValue(oldEntries[i]);

            if (boxedPropertyValue is string stringValue)
            {
                if (_isArray)
                {
                    if (TryGetJsonElementPropertyValue(idAttribute, patches, jsonPropertyNames, out var idValue) && !string.IsNullOrWhiteSpace((string)idValue))
                    {
                        id = (string)idValue;
                    }
                    else
                    {
                        errors.Add((StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, ConfigurationCommandHelper.UnexpectedExecutionExceptionString, _componentId, _facet, NoIdSpecifiedArrayPatchString)));
                        return false;
                    }
                }

                if (id.Equals(stringValue, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (TryPatchConfiguration(oldEntries[i], configurationEntryType, patches, out var patchedEntry, out errors, idAttribute.Name))
                    {
                        oldEntries[i] = patchedEntry;
                        newEntries = oldEntries;
                        entryFound = true;
                        break;
                    }

                    return false;
                }
            }
        }

        if (!entryFound)
        {
            errors.Add((StatusCodes.Status409Conflict, string.Format(CultureInfo.InvariantCulture, ConfigurationEntryNotFoundString, id)));
            return false;
        }

        return true;
    }

    #endregion
}
