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

public class AdapterBaseHierarchyCreator : BaseHierarchyCreator
{
    private const string AdapterServiceHealthId = "DataCollectorService";
    private const string AdapterServiceHealthDescriptionPostFix = "Adapter Service Health";
    private readonly string _healthDescription;
    
    public AdapterBaseHierarchyCreator(IApplicationManifest manifest) : base(manifest)
    {
        _healthDescription = AdapterTypes + " " + AdapterServiceHealthDescriptionPostFix;
    }

    public static LinkNode GetRootNode()
    {
        return new DataTypeLinkNode(EdgeSystemConstants.AdaptersHealthRootId, EdgeSystemConstants.AdaptersHealthRootName);
    }

    public static LinkNode GetServiceNode(string prefix, string machineName, string serviceName)
    {
        prefix ??= string.Empty;
        return new DataTypeLinkNode(AdapterServiceHealthId, $"{prefix}{machineName}.{serviceName}");
    }

    protected override DataType[] GetTypes()
    {
        return new[] { GetRootAssetType(), GetServiceHealthType() };
    }

    protected override List<(string, Classification, object)> GetAssets()
    {
        return new List<(string, Classification, object)>
        {
            GetRootAsset(),
            CreateServiceHealthAsset(),
        };
    }

    protected override List<(string Id, Classification Classification, object Values)> GetLinks()
    {
        var links = new List<(string, Classification, object)>();

        // Every time this is called, we will link the root and adapter component health assets to ensure we do not break the hierarchy.
        // This means it will be duplicated by health/diagnostics which means extra network messages/processing but should have no other consequence.

        // link adapter instance health asset to global adapter asset
        var sourceLink = GetRootNode();
        var targetLink = GetServiceNode(HealthPrefix, MachineName, ServiceName);
        var link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        return links;
    }

    protected override string GetRootId()
    {
        return EdgeSystemConstants.AdaptersHealthRootId;
    }

    private static DataType GetServiceHealthType()
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

    private static (string Id, Classification Classification, object Values) GetRootAsset()
    {
        // Global adapter root asset
        var values = new Dictionary<string, object>
        {
            [IdPropertyName] = EdgeSystemConstants.AdaptersHealthRootName,
            [Description] = EdgeSystemConstants.AdaptersHealthRootDescription,
        };
        return (EdgeSystemConstants.AdaptersHealthRootId, Classification.Static, values);
    }

    private DataType GetRootAssetType()
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
        var descriptionProperty = new PropertyDefinition
        {
            Type = Tokens.StringToken,
        };

        properties[IdPropertyName] = idProperty;
        properties[Description] = descriptionProperty;

        adapterType.Properties = properties;
        return adapterType;
    }

    private (string Id, Classification Classification, object Values) CreateServiceHealthAsset()
    {
        var values = new Dictionary<string, object>
        {
            [IdPropertyName] = ServiceAssetId,
            [Description] = _healthDescription,
            [Host] = MachineName,
            [Endpoint] = EgressEndpoints,
            [TypeString] = AdapterTypes,
            [Version] = VersionNumber,
        };

        return (AdapterServiceHealthId, Classification.Static, values);
    }
}
