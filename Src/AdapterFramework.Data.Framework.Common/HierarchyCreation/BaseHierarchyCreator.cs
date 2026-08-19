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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Common.HierarchyCreation;

public abstract class BaseHierarchyCreator
{
    protected const string IdPropertyName = "Name";
    protected const string Description = "Description";
    protected const string Endpoint = "End Point";
    protected const string TypeString = "Type";
    protected const string Version = "Version";
    protected const string Host = "Host";

    protected BaseHierarchyCreator(IApplicationManifest manifest)
    {
        ThrowHelper.ThrowIfArgumentNull(manifest, nameof(manifest));
       
        VersionNumber = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0.0";
        HealthPrefix = manifest.HealthPrefix;
        MachineName = manifest.MachineName;
        ServiceName = manifest.ServiceName;
        
        if (manifest.IsEdgeDataStore)
        {
            AdapterTypes = EdgeSystemConstants.EdgeDataStoreHealthType;
        }
        else
        {
            AdapterTypes = string.Join(", ", manifest.GetAdapterTypes());
        }

        var healthPrefix = manifest.HealthPrefix ?? string.Empty;
        ServiceAssetId = $"{healthPrefix}{MachineName}.{ServiceName}";
    }

    protected static string EgressEndpoints => string.Empty;

    protected string VersionNumber { get; }
    protected string HealthPrefix { get; }
    protected string MachineName { get; }
    protected string ServiceName { get; }
    protected string AdapterTypes { get; }
    protected string ServiceAssetId { get; }

    public void CreateAndSendBaseHierarchy(IHealthMessageProcessor messageProcessor)
    {
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));

        messageProcessor.WriteHealthTypes(GetTypes());

        foreach (var (id, classification, instance) in GetAssets())
        {
            messageProcessor.WriteHealthValue(id, classification, instance);
        }

        foreach (var (id, classification, instance) in GetLinks())
        {
            messageProcessor.WriteHealthValue(id, classification, instance);
        }
    }

    protected abstract DataType[] GetTypes();
    protected abstract List<(string Id, Classification Classification, object Values)> GetAssets();
    protected abstract List<(string Id, Classification Classification, object Values)> GetLinks();
    protected abstract string GetRootId();
}
