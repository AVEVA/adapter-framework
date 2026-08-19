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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;

namespace AdapterFramework.Data.Framework.Abstractions.Management;

public interface IRuntimeManagementRegistry : IRuntimeConfigurationRegistry
{
    /// <summary>
    /// Removes secret from <see cref="IRuntimeManagementRegistry"/>.
    /// </summary>
    /// <param name="secretId">The secret Id to remove.</param>
    /// <param name="componentId">The component Id that uses the secret.</param>
    /// <param name="facetName">The facet name that uses the secret.</param>
    /// <param name="configurationEntryId">The configuration entry Id with the secret.</param>
    public void RemoveSecretIdFacetMapping(string secretId, string componentId, string facetName, string configurationEntryId = null);

    /// <summary>
    /// Adds or updates secret Id registration.
    /// </summary>
    /// <param name="secretId">The secret Id registration to update.</param>
    /// <param name="componentId">The component Id that uses the secret.</param>
    /// <param name="facetName">The facet name that uses the secret.</param>
    /// <param name="configurationEntryId">The configuration entry Id with the secret.</param>
    public void AddOrUpdateSecretIdFacetsMapping(string secretId, string componentId, string facetName, string configurationEntryId = null);

    /// <summary>
    /// Try to retrieve a set of facets that have the secret Id registered.
    /// </summary>
    /// <param name="secretId">The secret Id to look up.</param>
    /// <param name="registrations">Tuple containing information about secret registrations.</param>
    /// <returns>True if registrations were found. False otherwise.</returns>
    public bool TryGetFacetsWithSecret(string secretId, out IReadOnlyList<(string ComponentId, string Facet, string EntryId)> registrations);

    /// <summary>
    /// Tries to get a <see cref="IConfigurationCommandGenerator"/> tuple registered for component ID and facet in the <see cref="IRuntimeConfigurationRegistry"/>.
    /// </summary>
    /// <param name="id">Tuple of Component ID and facet to get <see cref="IConfigurationCommandGenerator"/> tuple for.</param>
    /// <param name="commandGeneratorTuple">Command generator tuple that contains instances of <see cref="IConfigurationCommandGenerator"/>,
    /// callback action and configuration type and <see cref="Operations"/> supported on the configuration object."/></param>
    /// <returns>True when command generator tuple is found. False otherwise.</returns>
    bool TryGetConfigurationRegistryCommandGeneratorTuple((string ComponentId, string Facet) id, out (IConfigurationCommandGenerator CommandGenerator,
        Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
        Type ConfigurationType, Operations SupportedOperations) commandGeneratorTuple);
}
