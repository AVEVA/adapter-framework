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
using System.Reflection;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// Defines set of services provided to <see cref="IEdgeComponent"/> to register runtime configurations.
/// </summary>
public interface IRuntimeConfigurationRegistry
{
    /// <summary>
    /// Registers a component configuration facet.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="facetName">Name of the configuration facet.</param>
    /// <param name="commandGenerator">Command generator instance.</param>
    /// <param name="callback">Callback action.</param>
    /// <param name="cmdHelpFunc">Command-line help output function for the given <paramref name="facetName"/>.</param>
    /// <param name="customValidationFunction">Optional custom validation function that gets called before a new configuration object is persisted.</param>
    /// <param name="supportedOperations">Operation supported on the configuration object.</param>
    void RegisterComponentConfiguration<T>(string componentId, string facetName, IConfigurationCommandGenerator commandGenerator, Action<ConfigurationChangedEventArgs> callback, Func<string> cmdHelpFunc, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction = null, Operations supportedOperations = Operations.Get | Operations.Create | Operations.Delete | Operations.Update);

    /// <summary>
    /// Unregisters a component specific configuration facet.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="facetName">Name of the configuration facet.</param>
    void UnregisterComponentConfiguration(string componentId, string facetName);

    /// <summary>
    /// Removes all registered configuration facets.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    void UnregisterComponent(string componentId);

    /// <summary>
    /// Gets all registered component IDs from the registry.
    /// </summary>
    /// <returns>Collection of registered component IDs.</returns>
    IEnumerable<string> GetRegisteredComponentIds();

    /// <summary>
    /// Gets collection of facets registered for the component ID.
    /// </summary>
    /// <param name="componentId">Component ID to get collection of facets for.</param>
    /// <param name="facets">Collection of registered facets.</param>
    /// <returns>True when component has at least one facet registered. False otherwise.</returns>
    bool TryGetAvailableFacets(string componentId, out IList<string> facets);

    /// <summary>
    /// Tries to get a <see cref="IConfigurationCommandGenerator"/> tuple registered for component ID and facet.
    /// </summary>
    /// <param name="id">Tuple of Component ID and facet to get <see cref="IConfigurationCommandGenerator"/> tuple for.</param>
    /// <param name="commandGeneratorTuple">Command generator tuple that contains instances of <see cref="IConfigurationCommandGenerator"/>,
    /// callback action and configuration type and <see cref="Operations"/> supported on the configuration object."/></param>
    /// <returns>True when command generator tuple is found. False otherwise.</returns>
    bool TryGetCommandGeneratorTuple((string ComponentId, string Facet) id, out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction, Type ConfigurationType, Operations SupportedOperations) commandGeneratorTuple);

    /// <summary>
    /// Tries to get registered Command line help function registered for component ID and facet.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="facet">Facet name.</param>
    /// <param name="cmdHelpFunc">Registered command line help function.</param>
    /// <returns>True when command line function is found. False otherwise.</returns>
    bool TryGetCommandLineHelpFunction(string componentId, string facet, out Func<string> cmdHelpFunc);

    /// <summary>
    /// Registers <see cref="IDataSourceDiscoveryManager"/> for a component.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="dataSourceDiscoveryManager"><see cref="IDataSourceDiscoveryManager"/> instance to register.</param>
    void RegisterDataSourceDiscoveryManager(string componentId, IDataSourceDiscoveryManager dataSourceDiscoveryManager);

    /// <summary>
    /// Tries to get a <see cref="IConfigurationCommandGenerator"/> tuple registered for component ID and facet.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="dataSourceDiscoveryManager">Registered <see cref="IDataSourceDiscoveryManager"/>.</param>
    /// <returns>True when DataSourceDiscoveryManager is found. False otherwise.</returns>
    bool TryGetDataSourceDiscoveryManager(string componentId, out IDataSourceDiscoveryManager dataSourceDiscoveryManager);

    /// <summary>
    /// Removes registered <see cref="IDataSourceDiscoveryManager"/> for given <paramref name="componentId"/>.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    void UnregisterDataSourceDiscoveryManager(string componentId);

    /// <summary>
    /// Registers <see cref="IHistoryRecoveryManager"/> for a component.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="historyRecoveryManager"><see cref="IHistoryRecoveryManager"/> instance to register.</param>
    void RegisterHistoryRecoveryManager(string componentId, IHistoryRecoveryManager historyRecoveryManager);

    /// <summary>
    /// Tries to get a <see cref="IHistoryRecoveryManager"/> tuple registered for component ID and facet.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="historyRecoveryManager">Registered <see cref="IHistoryRecoveryManager"/>.</param>
    /// <returns>True when <see cref="IHistoryRecoveryManager"/> is found. False otherwise.</returns>
    bool TryGetHistoryRecoveryManager(string componentId, out IHistoryRecoveryManager historyRecoveryManager);

    /// <summary>
    /// Removes registered <see cref="IHistoryRecoveryManager"/> instance for given <paramref name="componentId"/>.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    void UnregisterHistoryRecoveryManager(string componentId);

    /// <summary>
    /// Returns collection of all registered facets per component where at least one property is decorated with
    /// protected attribute.
    /// </summary>
    /// <returns>
    /// Returns collection of all registered facets per component where at least one property is decorated with
    /// protected attribute.
    /// </returns>
    IReadOnlyDictionary<(string ComponentId, string Facet), IList<PropertyInfo>> GetProtectedPropertyInfos();

    /// <summary>
    /// Returns <see cref="PropertyInfo"/> for all properties registered in a given facet containing Protected attribute.
    /// </summary>
    /// <param name="componentId">Component Id to get the protected properties for.</param>
    /// <param name="facet">Facet name to get the protected properties for.</param>
    /// <param name="protectedPropertyInfos"> Collection of <see cref="PropertyInfo"/> for the component Id and facet name combination.</param>
    /// <returns>True if found. False otherwise.</returns>
    bool TryGetFacetProtectedPropertyInfos(string componentId, string facet, out IList<PropertyInfo> protectedPropertyInfos);
}
