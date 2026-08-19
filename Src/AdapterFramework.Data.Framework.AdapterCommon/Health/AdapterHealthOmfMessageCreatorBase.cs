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
using AdapterFramework.Data.Framework.Common.Health;

namespace AdapterFramework.Data.Framework.AdapterCommon.Health;

public abstract class AdapterHealthOmfMessageCreatorBase : HealthOmfMessageCreatorBase
{
    protected const string DataCollectorHealth = "DataCollectorHealth";
    protected const string AdapterComponentHealthDescriptionPostFix = "Adapter Health";
    protected const string IdPropertyName = "Name";
    protected const string Description = "Description";
    protected const string DataSource = "Data Source";
    protected const string Endpoint = "End Point";
    protected const string TypeString = "Type";
    protected const string Version = "Version";
    protected const string Host = "Host";

    private readonly string _machineName;
    private readonly string _componentType;
    private readonly string _version;
    private readonly string _componentId;
    private readonly string _egressEndpoints;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdapterHealthOmfMessageCreatorBase"/> class.
    /// </summary>
    /// <param name="machineName">Name of the machine that this adapter is running.</param>
    /// <param name="componentType">The adapter component type.</param>
    /// <param name="version">The version of the running application.</param>
    /// <param name="componentId">The adapter instance.</param>
    /// <param name="parent">The parent asset that the health asset will be linked to.</param>
    /// <param name="egressEndpoints">The egress endpoints.</param>
    protected AdapterHealthOmfMessageCreatorBase(string machineName, string componentType, string version, string componentId,
        LinkNode parent, string egressEndpoints) : base(parent)
    {
        _machineName = machineName;
        _componentType = componentType;
        _version = version;
        _componentId = componentId;
        _egressEndpoints = egressEndpoints ?? string.Empty;
    }

    public override LinkNode GetComponentLink()
    {
        return new DataTypeLinkNode(DataCollectorHealth, GetComponentAssetId());
    }

    protected override DataType GetComponentType()
    {
        var properties = new Dictionary<string, PropertyDefinition>();
        DataType adapterType = new StaticDataType
        {
            Id = DataCollectorHealth,
        };

        var idProp = new PropertyDefinition
        {
            IsIndex = true,
            IsName = true,
            Type = Tokens.StringToken,
        };
        var stringProperty = new PropertyDefinition
        {
            Type = Tokens.StringToken,
        };

        properties[IdPropertyName] = idProp;
        properties[DataSource] = stringProperty;
        properties[Description] = stringProperty;
        properties[Endpoint] = stringProperty;
        properties[Host] = stringProperty;
        properties[TypeString] = stringProperty;
        properties[Version] = stringProperty;

        adapterType.Properties = properties;
        return adapterType;
    }

    protected override (string Id, Classification Classification, object Values) GetComponentAsset()
    {
        // This adapter component's health-specific asset
        var values = new Dictionary<string, object>
        {
            [IdPropertyName] = GetComponentAssetId(),
            [DataSource] = _componentId,
            [Description] = GetComponentDescription(),
            [Endpoint] = _egressEndpoints,
            [Host] = _machineName,
            [TypeString] = _componentType,
            [Version] = _version,
        };

        return (DataCollectorHealth, Classification.Static, values);
    }

    protected abstract string GetComponentDescription();
}
