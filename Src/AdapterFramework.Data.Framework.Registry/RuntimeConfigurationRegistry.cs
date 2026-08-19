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
using System.Linq;
using System.Reflection;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Registry;

public class RuntimeConfigurationRegistry : IRuntimeConfigurationRegistry
{
    private readonly ConcurrentDictionary<string /*component ID*/, IList<string> /*list of facets for component*/> _configurationFacets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IDataSourceDiscoveryManager> _dataSourceDiscoveryManagers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IHistoryRecoveryManager> _historyRecoveryManager = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<(string ComponentId, string Facet), (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> Callback, Func<string> CmdHelpFunc,
        Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction, Type ConfigurationType, Operations SupportedOperations)> _configurationCommandGenerators = new();
    private readonly ConcurrentDictionary<(string ComponentId, string Facet), IList<PropertyInfo>> _protectedProperties = new();

    public void RegisterComponentConfiguration<T>(
        string componentId,
        string facetName,
        IConfigurationCommandGenerator commandGenerator,
        Action<ConfigurationChangedEventArgs> callback,
        Func<string> cmdHelpFunc,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction = null,
        Operations supportedOperations = Operations.Get | Operations.Create | Operations.Update | Operations.Delete)
    {
        IList<PropertyInfo> AddOrUpdateProtectedPropertiesMapping(Type type, string componentIdUpper, string facetNameUpper)
        {
            var protectedProperties = ConfigurationHelper.GetProtectedPropertyInfos(type).ToList();
            if (protectedProperties.Count > 0)
            {
                _protectedProperties.AddOrUpdate((componentIdUpper, facetNameUpper), protectedProperties, (_, _) => protectedProperties);
            }

            foreach (var protectedProperty in protectedProperties)
            {
                if (protectedProperty.PropertyType != typeof(string))
                {
                    throw new ArgumentException($"Property name: '{protectedProperty.Name}' marked with protected attribute is of invalid type: {protectedProperty.PropertyType} " +
                                                "specified for protected attribute. Protected attribute must be of string type.");
                }
            }

            return protectedProperties;
        }

        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facetName, nameof(facetName));
        ThrowHelper.ThrowIfArgumentNull(commandGenerator, nameof(commandGenerator));

        var componentIdUpper = componentId.ToUpperInvariant();
        var facetNameUpper = facetName.ToUpperInvariant();
        var configurationType = typeof(T);

        if (configurationType.IsArray)
        {
            // Throws if zero or more than one properties have ID attribute
            try
            {
                var elementType = configurationType.GetElementType();
                if (elementType == null)
                {
                    throw new ArgumentException($"Facet '{facetName}' for component Id '{componentId}' uses enumerable configuration class '{configurationType}' of invalid type");
                }

                var protectedProperties = AddOrUpdateProtectedPropertiesMapping(elementType, componentIdUpper, facetNameUpper);
                if (protectedProperties.Count > 0)
                {
                    var properties = elementType.GetProperties();
                    _ = properties.Single(propertyInfo => Attribute.IsDefined(propertyInfo, typeof(IdAttribute)));
                }
            }
            catch
            {
                throw new ArgumentException($"Facet '{facetName}' for component Id '{componentId}' uses enumerable configuration class '{configurationType}', which requires the Id property when it contains protected fields");
            }
        }
        else
        {
            AddOrUpdateProtectedPropertiesMapping(configurationType, componentIdUpper, facetNameUpper);
        }

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

        _configurationCommandGenerators.TryAdd((componentIdUpper, facetNameUpper), (commandGenerator, callback, cmdHelpFunc, customValidationFunction, configurationType, supportedOperations));
    }

    public void UnregisterComponent(string componentId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        if (TryGetAvailableFacets(componentId, out var facets))
        {
            var componentIdUpper = componentId.ToUpperInvariant();

            foreach (var facet in facets)
            {
                var facetUpper = facet.ToUpperInvariant();

                _configurationCommandGenerators.TryRemove((componentIdUpper, facetUpper), out _);
                _protectedProperties.TryRemove((componentIdUpper, facetUpper), out _);
            }

            _configurationFacets.TryRemove(componentId, out _);
        }

        _dataSourceDiscoveryManagers.TryRemove(componentId, out _);
    }

    public void UnregisterComponentConfiguration(string componentId, string facetName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facetName, nameof(facetName));

        _configurationCommandGenerators.TryRemove((componentId.ToUpperInvariant(), facetName.ToUpperInvariant()), out _);

        if (TryGetAvailableFacets(componentId, out var facets))
        {
            for (var i = 0; i < facets.Count; i++)
            {
                if (facets[i].Equals(facetName, StringComparison.InvariantCultureIgnoreCase))
                {
                    facets.RemoveAt(i);
                    break;
                }
            }
        }

        if (facets.Count < 1)
        {
            _configurationFacets.TryRemove(componentId, out _);
        }
    }

    public bool TryGetAvailableFacets(string componentId, out IList<string> facets)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        return _configurationFacets.TryGetValue(componentId, out facets);
    }

    public bool TryGetCommandGeneratorTuple((string ComponentId, string Facet) id, out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction,
        Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction, Type ConfigurationType, Operations SupportedOperations) commandGenerator)
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

    public void RegisterDataSourceDiscoveryManager(string componentId, IDataSourceDiscoveryManager dataSourceDiscoveryManager)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNull(dataSourceDiscoveryManager, nameof(dataSourceDiscoveryManager));

        if (!_dataSourceDiscoveryManagers.TryAdd(componentId, dataSourceDiscoveryManager))
        {
            throw new InvalidOperationException($"Component with Id {componentId} has data source discovery manager already registered.");
        }
    }

    public bool TryGetDataSourceDiscoveryManager(string componentId, out IDataSourceDiscoveryManager dataSourceDiscoveryManager)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        return _dataSourceDiscoveryManagers.TryGetValue(componentId, out dataSourceDiscoveryManager);
    }

    public void UnregisterDataSourceDiscoveryManager(string componentId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        _dataSourceDiscoveryManagers.TryRemove(componentId, out _);
    }

    public IEnumerable<string> GetRegisteredComponentIds()
    {
        return _configurationFacets.Keys;
    }

    public void RegisterHistoryRecoveryManager(string componentId, IHistoryRecoveryManager historyRecoveryManager)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNull(historyRecoveryManager, nameof(historyRecoveryManager));

        if (!_historyRecoveryManager.TryAdd(componentId, historyRecoveryManager))
        {
            throw new InvalidOperationException($"Component with Id {componentId} has history recovery manager already registered.");
        }
    }

    public bool TryGetHistoryRecoveryManager(string componentId, out IHistoryRecoveryManager historyRecoveryManager)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        return _historyRecoveryManager.TryGetValue(componentId, out historyRecoveryManager);
    }

    public void UnregisterHistoryRecoveryManager(string componentId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        _historyRecoveryManager.TryRemove(componentId, out _);
    }

    public IReadOnlyDictionary<(string ComponentId, string Facet), IList<PropertyInfo>> GetProtectedPropertyInfos()
    {
        return _protectedProperties;
    }

    public bool TryGetFacetProtectedPropertyInfos(string componentId, string facet, out IList<PropertyInfo> protectedPropertyInfos)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));

        return _protectedProperties.TryGetValue((componentId.ToUpperInvariant(), facet.ToUpperInvariant()), out protectedPropertyInfos);
    }
}
