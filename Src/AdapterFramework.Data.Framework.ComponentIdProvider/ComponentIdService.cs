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
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ComponentIdProvider;

public class ComponentIdService : IComponentIdService
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, List<string>> _availableComponents = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Public Constructor

    public ComponentIdService(IConfigurationProvider configurationProvider, IApplicationManifest applicationManifest)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));

        GetAvailableComponents(configurationProvider, applicationManifest);
    }

    #endregion

    #region Public Methods

    public static EdgeComponentConfig[] CreateDefaultComponentConfiguration(IApplicationManifest applicationManifest)
    {
        ThrowHelper.ThrowIfArgumentNull(applicationManifest, nameof(applicationManifest));

        EdgeComponentConfig[] edgeComponentsConfiguration;
        if (applicationManifest.IsEdgeDataStore)
        {
            edgeComponentsConfiguration = new[]
            {
                new EdgeComponentConfig
                {
                    ComponentId = EdgeSystemConstants.StorageComponentId,
                    ComponentType = EdgeSystemConstants.StorageComponentType,
                },
            };
        }
        else
        {
            edgeComponentsConfiguration = new[]
            {
                new EdgeComponentConfig
                {
                    ComponentId = EdgeSystemConstants.OmfEgressComponentId,
                    ComponentType = EdgeSystemConstants.OmfEgressComponentType,
                },
            };
        }

        return edgeComponentsConfiguration;
    }

    public string GetEdgeComponentId(string edgeComponentType)
    {
        if (_availableComponents.TryGetValue(edgeComponentType, out var componentIds))
        {
            var componentId = componentIds.FirstOrDefault();

            // remove used componentId
            componentIds.Remove(componentId);

            return componentId;
        }

        return null;
    }

    public void AddEdgeComponentId(string componentType, string componentId)
    {
        if (!_availableComponents.TryGetValue(componentType, out var adapterIds))
        {
            _availableComponents.TryAdd(componentType,
                new List<string> { componentId });
        }
        else
        {
            adapterIds.Add(componentId);
        }
    }

    #endregion

    #region Private Methods
    
    private static void EnforceUniqueness(string componentId, ISet<string> existingIds)
    {
        var componentIdUpper = componentId.ToUpper(CultureInfo.InvariantCulture);

        if (componentIdUpper.Equals(EdgeSystemConstants.SystemComponentIdUpper, StringComparison.InvariantCulture)) throw new InvalidOperationException($"Component ID '{componentId}' is reserved and cannot be used for a component.");

        if (existingIds.Contains(componentIdUpper)) throw new InvalidOperationException($"Component ID cannot be added twice: '{componentId}'");

        existingIds.Add(componentIdUpper);
    }

    private void GetAvailableComponents(IConfigurationProvider configurationProvider, IApplicationManifest applicationManifest)
    {
        if (!configurationProvider.TryGetConfiguration<EdgeComponentConfig[]>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName, out var edgeComponentsConfiguration, out var errors))
        {
            edgeComponentsConfiguration = CreateDefaultComponentConfiguration(applicationManifest);
            if (errors.IsNullOrEmpty())
            {
                configurationProvider.TrySaveConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName, edgeComponentsConfiguration, out _);
            }
        }

        var existingIds = new HashSet<string>();

        foreach (var componentConfiguration in edgeComponentsConfiguration)
        {
            EnforceUniqueness(componentConfiguration.ComponentId, existingIds);

            if (!_availableComponents.TryGetValue(componentConfiguration.ComponentType, out var adapterIds))
            {
                _availableComponents.TryAdd(componentConfiguration.ComponentType,
                    new List<string> { componentConfiguration.ComponentId });
            }
            else
            {
                adapterIds.Add(componentConfiguration.ComponentId);
            }
        }
    }

    #endregion
}
