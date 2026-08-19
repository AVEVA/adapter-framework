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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Common.Security;

public class SecretsManager : ISecretsManager
{
    public const string AddBrackets = "{{{{{0}}}}}"; // {{secretId}}
    private const string SecretIdDoesNotExistMessage = "Secret with Id: '{SecretId}' was not found.";

    private readonly IRuntimeManagementRegistry _runtimeManagementRegistry;
    private readonly IInternalDataProtector _dataProtector;
    private readonly ILogger _logger;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly ConcurrentDictionary<string, string> _uncommittedSecrets = new(StringComparer.OrdinalIgnoreCase);

    private ConcurrentDictionary<string, ManagedSecretConfiguration> _secretsTable = new(StringComparer.OrdinalIgnoreCase);

    public SecretsManager(IInternalDataProtector dataProtector, ILogger logger, IConfigurationProvider configurationProvider,
        IRuntimeManagementRegistry runtimeManagementRegistry)
    {
        _dataProtector = dataProtector;
        _logger = logger;
        _configurationProvider = configurationProvider;
        _runtimeManagementRegistry = runtimeManagementRegistry;

        Initialize();
    }

    public void ConfigurationChangedAction(ConfigurationChangedEventArgs args)
    {
        ThrowHelper.ThrowIfArgumentNull(args, nameof(args));

        var newSecretsTable = new ConcurrentDictionary<string, ManagedSecretConfiguration>(StringComparer.OrdinalIgnoreCase);

        if (args.NewValue is not ManagedSecretConfiguration[] secretsConfiguration)
        {
            _secretsTable.Clear();
            return;
        }

        foreach (var secret in secretsConfiguration)
        {
            if (_secretsTable.TryGetValue(secret.Id, out var oldSecret))
            {
                if (_dataProtector.Unprotect(oldSecret.Value) != _dataProtector.Unprotect(secret.Value))
                {
                    HandleSecretUpdate(secret.Id, oldSecret.Value, secret.Value);
                }
            }
            else
            {
                HandleSecretUpdate(secret.Id, string.Empty, secret.Value);
            }

            newSecretsTable[secret.Id] = secret;
        }

        _secretsTable = newSecretsTable;
    }

    public string Protect(string secretValue)
    {
        return secretValue.IsNullOrEmpty() ? secretValue : _dataProtector.Protect(secretValue);
    }

    public string Protect(string secretValue, string componentId, string secretId)
    {
        ThrowHelper.ThrowIfArgumentNullOrEmpty(secretValue, nameof(secretValue));
        ThrowHelper.ThrowIfArgumentNullOrEmpty(secretId, nameof(secretId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        var protectedSecret = _dataProtector.Protect(secretValue);
        if (componentId.Equals(EdgeSystemConstants.ManagementComponentId, StringComparison.OrdinalIgnoreCase))
        {
            return SecretMatchesIdRegex(secretValue) ? secretValue : protectedSecret;
        }

        if (SecretMatchesIdRegex(secretValue))
        {
            var scrubbedId = ConfigurationHelper.RemovePatternFromId(secretValue);

            AddOrUpdateSecretMapping(scrubbedId, secretId);

            if (!_secretsTable.TryGetValue(scrubbedId, out _))
            {
                _logger.LogWarning(SecretIdDoesNotExistMessage, secretValue);
            }

            return secretValue;
        }

        _uncommittedSecrets[secretId] = protectedSecret;

        var secretIdWithBrackets = string.Format(CultureInfo.InvariantCulture, AddBrackets, secretId);

        AddOrUpdateSecretMapping(secretId, secretId);

        return secretIdWithBrackets;
    }

    public string Unprotect(string id)
    {
        if (id.IsNullOrEmpty())
        {
            return id;
        }

        if (SecretMatchesIdRegex(id))
        {
            var scrubbedId = ConfigurationHelper.RemovePatternFromId(id);

            if (_uncommittedSecrets.TryGetValue(scrubbedId, out var value))
            {
                return _dataProtector.Unprotect(value);
            }

            if (!_secretsTable.TryGetValue(scrubbedId, out var secret))
            {
                _logger.LogWarning(SecretIdDoesNotExistMessage, id);
                return null;
            }

            return _dataProtector.Unprotect(secret.Value);
        }

        return _dataProtector.Unprotect(id);
    }

    public string GetProtectedString(string secretId)
    {
        if (SecretMatchesIdRegex(secretId))
        {
            var scrubbedId = ConfigurationHelper.RemovePatternFromId(secretId);
            if (_secretsTable.TryGetValue(scrubbedId, out var secret))
            {
                return secret.Value;
            }

            _logger.LogWarning(SecretIdDoesNotExistMessage, secretId);
        }

        return secretId;
    }

    public void ReconcileSecretValueChange(string secretId, bool commit = true)
    {
        if (!SecretMatchesIdRegex(secretId))
        {
            return;
        }

        secretId = ConfigurationHelper.RemovePatternFromId(secretId);
        if (_uncommittedSecrets.TryRemove(secretId, out string protectedSecret))
        {
            if (!commit)
            {
                _logger.LogDebug("Change to secret with Id {Id} has been rolled back.", secretId);

                var (componentId, facetName, _) = ParseGeneratedSecretId(secretId);

                if (string.IsNullOrEmpty(componentId) || string.IsNullOrEmpty(facetName))
                {
                    return;
                }

                return;
            }

            _secretsTable.GetOrAdd(secretId, new ManagedSecretConfiguration() { Id = secretId, Value = protectedSecret }).Value = protectedSecret;

            SaveToDisk();
            _logger.LogDebug("Secret with Id {Id} has been persisted.", secretId);
        }
    }

    private static (string ComponentId, string FacetName, string ConfigurationEntryId) ParseGeneratedSecretId(string generatedSecretId)
    {
        var split = generatedSecretId.Split('.');

        if (split.Length == 3)
        {
            return (split[0], split[1], null);
        }

        return split.Length == 4 ? (split[0], split[1], split[2]) : default;
    }

    private static bool SecretMatchesIdRegex(string secret)
    {
        return Regex.IsMatch(secret, EdgeSystemConstants.SecretIdPlaceholderPattern);
    }

    private static bool TryLocateConfigurationIndex(string entryId, Type configurationElementType, object existingConfiguration, out int configurationEntryIndex)
    {
        configurationEntryIndex = 0;
        if (string.IsNullOrEmpty(entryId))
        {
            return true;
        }

        var idPropertyInfo = ConfigurationHelper.GetIdProperty(configurationElementType);
        var indexFound = false;
        if (existingConfiguration is not object[] arrayConfiguration)
        {
            return true;
        }

        for (int i = 0; i < arrayConfiguration.Length; i++)
        {
            if ((string)idPropertyInfo.GetValue(arrayConfiguration[i]) == entryId)
            {
                configurationEntryIndex = i;
                indexFound = true;
                break;
            }
        }

        return indexFound;
    }

    private static bool TrySetProtectedPropertyValue(PropertyInfo protectedPropertyInfo, object oldConfiguration, object newConfiguration, string secretId, string oldValue, string newValue)
    {
        if ((string)protectedPropertyInfo.GetValue(oldConfiguration) != secretId)
        {
            return false;
        }

        protectedPropertyInfo.SetValue(oldConfiguration, oldValue);
        protectedPropertyInfo.SetValue(newConfiguration, newValue);
        return true;
    }

    private static bool TryCreateConfigurationObjects(string previousSecretValue, string currentSecretValue, Type configurationElementType, object existingConfiguration,
        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) commandGenerator, int configurationEntryIndex, string secretId, out object newValue)
    {
        var secretIdWithBrackets = string.Format(CultureInfo.InvariantCulture, AddBrackets, secretId);
        var protectedProperties = ConfigurationHelper.GetProtectedPropertyInfos(configurationElementType);
        newValue = ConfigurationHelper.CreateObjectCopy(existingConfiguration, commandGenerator.ConfigurationType);
        var secretPropertyPresent = false;
        foreach (var protectedProperty in protectedProperties)
        {
            if (newValue is object[] newConfigurationArray)
            {
                if (TrySetProtectedPropertyValue(protectedProperty, ((object[])existingConfiguration)[configurationEntryIndex], newConfigurationArray[configurationEntryIndex],
                        secretIdWithBrackets, previousSecretValue, currentSecretValue))
                {
                    secretPropertyPresent = true;
                    break;
                }
            }
            else if (TrySetProtectedPropertyValue(protectedProperty, existingConfiguration, newValue, secretIdWithBrackets, previousSecretValue, currentSecretValue))
            {
                secretPropertyPresent = true;
                break;
            }
        }

        return secretPropertyPresent;
    }

    private void Initialize()
    {
        if (!_configurationProvider.TryGetConfiguration<ManagedSecretConfiguration[]>(EdgeSystemConstants.ManagementComponentId, EdgeSystemConstants.SecretsFacetName,
            out var secretsConfiguration, out var errors))
        {
            if (errors?.Count > 0)
            {
                _logger.LogWarning("Management secrets configuration is invalid. {Errors}.", errors);
            }

            return;
        }

        foreach (var item in secretsConfiguration)
        {
            _secretsTable[item.Id] = item;
        }
    }

    private void HandleSecretUpdate(string secretId, string previousSecretValue, string currentSecretValue)
    {
        if (!_runtimeManagementRegistry.TryGetFacetsWithSecret(secretId, out var facetTuples))
        {
            return;
        }

        foreach (var (componentId, facet, entryId) in facetTuples)
        {
            if (!_runtimeManagementRegistry.TryGetConfigurationRegistryCommandGeneratorTuple((componentId, facet), out var commandGenerator))
            {
                continue;
            }

            var getCommand = commandGenerator.CommandGenerator.GenerateConfigurationGetCommand(commandGenerator.ConfigurationType, null);

            if (!getCommand.TryExecute(out var existingConfiguration, out var errors))
            {
                _logger.LogDebug("Secrets manager was unable to load existing configuration for component Id: {ComponentId}, facet: {Facet} and entry Id: {EntryId}. Errors: {Errors}",
                    componentId, facet, entryId, errors);

                continue;
            }

            if (existingConfiguration == null)
            {
                _logger.LogDebug("Secrets manager loaded empty configuration for: component {ComponentId}, facet: {Facet} and entry Id: {EntryId}. Mapping to {SecretId} has been removed.",
                    componentId, facet, entryId, secretId);

                _runtimeManagementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, facet, entryId);
                continue;
            }

            var configurationElementType = commandGenerator.ConfigurationType.IsArray
                ? commandGenerator.ConfigurationType.GetElementType()
                : commandGenerator.ConfigurationType;

            if (!TryLocateConfigurationIndex(entryId, configurationElementType, existingConfiguration, out var configurationEntryIndex))
            {
                _logger.LogDebug("Component '{ComponentId}' and facet '{Facet}' no longer contains configuration entry with id: {Id}. Mapping has been removed.",
                    componentId, facet, entryId);

                _runtimeManagementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, facet, entryId);
                return;
            }

            if (!TryCreateConfigurationObjects(previousSecretValue, currentSecretValue, configurationElementType, existingConfiguration, commandGenerator,
                    configurationEntryIndex, secretId, out var newConfiguration))
            {
                _logger.LogDebug("Component '{ComponentId}' and facet '{Facet}' configuration no longer reference secret with Id {Id}. Mapping has been removed.",
                    componentId, facet, secretId);

                _runtimeManagementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, facet, entryId);
                return;
            }

            ExecuteComponentCallbackFunction(componentId, facet, entryId, commandGenerator.CallbackAction, existingConfiguration, newConfiguration);
        }
    }

    private void ExecuteComponentCallbackFunction(string componentId, string facet, string entryId, Action<ConfigurationChangedEventArgs> callbackAction,
        object existingConfiguration, object newConfiguration)
    {
        _logger.LogDebug("Executing callback for component '{Component}' and facet '{Facet}' with entry Id '{EntryId}'.", componentId, facet, entryId);

        try
        {
            callbackAction?.Invoke(new ConfigurationChangedEventArgs(existingConfiguration, newConfiguration));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Callback action execution failed for component '{ComponentId}' and facet '{Facet}'.", componentId, facet);
        }
    }

    private void AddOrUpdateSecretMapping(string secretId, string generatedSecretId)
    {
        var (componentId, facetName, configurationEntryId) = ParseGeneratedSecretId(generatedSecretId);

        if (string.IsNullOrEmpty(componentId) || string.IsNullOrEmpty(facetName))
        {
            return;
        }

        _runtimeManagementRegistry.AddOrUpdateSecretIdFacetsMapping(secretId, componentId, facetName, configurationEntryId);
    }

    private void SaveToDisk()
    {
        if (!_configurationProvider.TrySaveConfiguration(EdgeSystemConstants.ManagementComponentId, EdgeSystemConstants.SecretsFacetName, _secretsTable.Values, out var errors))
        {
            _logger.LogError("Unable to save secrets configuration. {Errors}", errors);
        }
    }
}
