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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;

namespace AdapterFramework.Data.Framework.Common.HierarchyCreation;

public class EdsBaseHierarchyCreator : BaseHierarchyCreator
{
    private const string AdapterServiceHealthId = "DataCollectorService";
    private const string HealthDescription = "EDS Service Health";

    public EdsBaseHierarchyCreator(IApplicationManifest manifest) : base(manifest)
    {
    }

    public static LinkNode GetRootNode()
    {
        return new DataTypeLinkNode(EdgeSystemConstants.EdgeDataStoreHealthRootId, EdgeSystemConstants.EdgeDataStoreHealthRootName);
    }

    public static LinkNode GetServiceNode(string prefix, string machineName, string serviceName)
    {
        prefix ??= string.Empty;
        return new DataTypeLinkNode(AdapterServiceHealthId, $"{prefix}{machineName}.{serviceName}");
    }

    protected override DataType[] GetTypes()
    {
        return new[]
        {
            CreateRootAssetType(),
            CreateServiceHealthType(),
        };
    }

    protected override List<(string Id, Classification Classification, object Values)> GetLinks()
    {
        var links = new List<(string, Classification, object)>();

        // link service asset to global Edge Data Store asset
        var sourceLink = GetRootNode();
        var targetLink = GetServiceNode(HealthPrefix, MachineName, ServiceName);
        var link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        return links;
    }

    protected override string GetRootId()
    {
        return EdgeSystemConstants.EdgeDataStoreHealthRootId;
    }

    protected override List<(string Id, Classification Classification, object Values)> GetAssets()
    {
        var assets = new List<(string, Classification, object)>();

        // Global Edge Data Store root asset
        var values = new Dictionary<string, object>
        {
            [IdPropertyName] = EdgeSystemConstants.EdgeDataStoreHealthRootName,
            [Description] = EdgeSystemConstants.EdgeDataStoreHealthRootDescription,
        };
        assets.Add((EdgeSystemConstants.EdgeDataStoreHealthRootId, Classification.Static, values));

        assets.Add(CreateServiceHealthAsset());
        return assets;
    }

    private static DataType CreateServiceHealthType()
    {
        var properties = new Dictionary<string, PropertyDefinition>();
        DataType adapterType = new StaticDataType
        {
            Id = AdapterServiceHealthId,
        };

        var idProperty = new PropertyDefinition
        {
            IsIndex = true,
            IsName = true,
            Type = Tokens.StringToken,
        };

        var stringProperty = new PropertyDefinition
        {
            Type = Tokens.StringToken,
        };

        properties[IdPropertyName] = idProperty;
        properties[Description] = stringProperty;
        properties[Endpoint] = stringProperty;
        properties[Host] = stringProperty;
        properties[TypeString] = stringProperty;
        properties[Version] = stringProperty;

        adapterType.Properties = properties;
        return adapterType;
    }

    private (string Id, Classification Classification, object Values) CreateServiceHealthAsset()
    {
        var values = new Dictionary<string, object>
        {
            [IdPropertyName] = ServiceAssetId,
            [Description] = HealthDescription,
            [Host] = MachineName,
            [Endpoint] = EgressEndpoints,
            [TypeString] = AdapterTypes,
            [Version] = VersionNumber,
        };

        return (AdapterServiceHealthId, Classification.Static, values);
    }

    private DataType CreateRootAssetType()
    {
        var properties = new Dictionary<string, PropertyDefinition>();
        DataType adapterType = new StaticDataType
        {
            Id = GetRootId(),
        };

        var idProperty = new PropertyDefinition
        {
            IsIndex = true,
            IsName = true,
            Type = Tokens.StringToken,
        };
        var descriptionProp = new PropertyDefinition
        {
            Type = Tokens.StringToken,
        };

        properties[IdPropertyName] = idProperty;
        properties[Description] = descriptionProp;

        adapterType.Properties = properties;
        return adapterType;
    }
}
