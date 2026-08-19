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
using System.Reflection;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Registry;

public class RuntimeManagementRegistry : IRuntimeManagementRegistry
{
    private readonly Dictionary<string, IList<string>> _configurationFacets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string ComponentId, string Facet), (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> Callback,
        Func<string> CmdHelpFunc, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction, Type ConfigurationType, Operations SupportedOperations)> _configurationCommandGenerators = new();
    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;
    private readonly ConcurrentDictionary<string, HashSet<(string ComponentId, string Facet, string EntryId)>> _registeredSecretIds = new(StringComparer.OrdinalIgnoreCase);

    public RuntimeManagementRegistry(IRuntimeConfigurationRegistry runtimeConfigurationRegistry)
    {
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
    }

    public void AddOrUpdateSecretIdFacetsMapping(string secretId, string componentId, string facetName, string entryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(secretId, nameof(secretId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facetName, nameof(facetName));

        var upperComponent = componentId.ToUpperInvariant();
        var upperFacet = facetName.ToUpperInvariant();

        _registeredSecretIds.AddOrUpdate(secretId, new HashSet<(string, string, string)> { (upperComponent, upperFacet, entryId) },
            (_, value) =>
        {
            value.Add((upperComponent, upperFacet, entryId));
            return value;
        });
    }

    public void RemoveSecretIdFacetMapping(string secretId, string componentId, string facetName, string entryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(secretId, nameof(secretId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facetName, nameof(facetName));

        if (!_registeredSecretIds.TryGetValue(secretId, out var values))
        {
            return;
        }

        values.Remove((componentId.ToUpperInvariant(), facetName.ToUpperInvariant(), entryId));

        if (values.Count == 0)
        {
            _registeredSecretIds.TryRemove(secretId, out _);
        }
    }

    public bool TryGetFacetsWithSecret(string secretId, out IReadOnlyList<(string ComponentId, string Facet, string EntryId)> registrations)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(secretId, nameof(secretId));

        registrations = null;

        if (!_registeredSecretIds.TryGetValue(secretId, out var registeredFacets))
        {
            return false;
        }

        registrations = registeredFacets.ToReadOnlyList();
        return true;
    }

    public bool TryGetConfigurationRegistryCommandGeneratorTuple((string ComponentId, string Facet) id, out (IConfigurationCommandGenerator CommandGenerator,
        Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
        Type ConfigurationType, Operations SupportedOperations) commandGeneratorTuple)
    {
        return _runtimeConfigurationRegistry.TryGetCommandGeneratorTuple(id, out commandGeneratorTuple);
    }

    public void RegisterComponentConfiguration<T>(
        string componentId,
        string facetName,
        IConfigurationCommandGenerator commandGenerator,
        Action<ConfigurationChangedEventArgs> callback,
        Func<string> cmdHelpFunc,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction = null,
        Operations supportedOperations = Operations.Get | Operations.Create | Operations.Update | Operations.Delete)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facetName, nameof(facetName));
        ThrowHelper.ThrowIfArgumentNull(commandGenerator, nameof(commandGenerator));

        var componentIdUpper = componentId.ToUpperInvariant();
        var facetNameUpper = facetName.ToUpperInvariant();

        if (_configurationFacets.TryGetValue(componentId, out var facets))
        {
            if (facets.Contains(facetName))
            {
                throw new InvalidOperationException($"Facet '{facetName}' for component Id '{componentId}' has been already added.");
            }

            facets.Add(facetName);
        }
        else
        {
            _configurationFacets.TryAdd(componentId, new List<string> { facetName });
        }

        _configurationCommandGenerators.TryAdd((componentIdUpper, facetNameUpper), (commandGenerator, callback, cmdHelpFunc, customValidationFunction,
            typeof(T), supportedOperations));
    }

    public bool TryGetCommandGeneratorTuple((string ComponentId, string Facet) id, out (IConfigurationCommandGenerator CommandGenerator,
        Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
        Type ConfigurationType, Operations SupportedOperations) commandGenerator)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id.ComponentId, nameof(id.ComponentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id.Facet, nameof(id.Facet));

        commandGenerator = default;

        id.ComponentId = id.ComponentId.ToUpperInvariant();
        id.Facet = id.Facet.ToUpperInvariant();

        if (_configurationCommandGenerators.TryGetValue(id, out var commandGeneratorTuple))
        {
            commandGenerator = (commandGeneratorTuple.CommandGenerator, commandGeneratorTuple.Callback,
                commandGeneratorTuple.CustomValidationFunction, commandGeneratorTuple.ConfigurationType,
                commandGeneratorTuple.SupportedOperations);

            return true;
        }

        return false;
    }

    public IEnumerable<string> GetRegisteredComponentIds()
    {
        return _configurationFacets.Keys;
    }

    public bool TryGetCommandLineHelpFunction(string componentId, string facet, out Func<string> cmdHelpFunc)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));

        cmdHelpFunc = null;

        componentId = componentId.ToUpperInvariant();
        facet = facet.ToUpperInvariant();

        if (_configurationCommandGenerators.TryGetValue((componentId, facet), out var commandGeneratorTuple))
        {
            cmdHelpFunc = commandGeneratorTuple.CmdHelpFunc;
            return true;
        }

        return false;
    }

    public bool TryGetAvailableFacets(string componentId, out IList<string> facets)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        return _configurationFacets.TryGetValue(componentId, out facets);
    }

    public bool TryGetDataSourceDiscoveryManager(string componentId, out IDataSourceDiscoveryManager dataSourceDiscoveryManager)
    {
        throw new NotImplementedException();
    }

    public bool TryGetHistoryRecoveryManager(string componentId, out IHistoryRecoveryManager historyRecoveryManager)
    {
        throw new NotImplementedException();
    }

    public void UnregisterComponent(string componentId)
    {
        throw new NotImplementedException();
    }

    public void UnregisterComponentConfiguration(string componentId, string facetName)
    {
        throw new NotImplementedException();
    }

    public void UnregisterDataSourceDiscoveryManager(string componentId)
    {
        throw new NotImplementedException();
    }

    public void UnregisterHistoryRecoveryManager(string componentId)
    {
        throw new NotImplementedException();
    }

    public void RegisterDataSourceDiscoveryManager(string componentId, IDataSourceDiscoveryManager dataSourceDiscoveryManager)
    {
        throw new NotImplementedException();
    }

    public void RegisterHistoryRecoveryManager(string componentId, IHistoryRecoveryManager historyRecoveryManager)
    {
        throw new NotImplementedException();
    }

    public IReadOnlyDictionary<(string ComponentId, string Facet), IList<PropertyInfo>> GetProtectedPropertyInfos()
    {
        throw new NotImplementedException();
    }

    public bool TryGetFacetProtectedPropertyInfos(string componentId, string facet, out IList<PropertyInfo> protectedPropertyInfos)
    {
        throw new NotImplementedException();
    }
}
