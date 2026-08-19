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
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.EgressComponent.Health;

public class EgressHealthOmfMessageCreator : HealthOmfMessageCreatorBase
{
    protected const string IdPropertyName = "Name";
    protected const string Description = "Description";
    protected const string Version = "Version";
    protected const string Host = "Host";

    private readonly string _egressHealthId;
    private readonly string _baseStreamId;
    private readonly string _machineName;
    private readonly string _version;
    private readonly string _description = "OMF Egress health";

    /// <summary>
    /// Initializes a new instance of the <see cref="EgressHealthOmfMessageCreator"/> class.
    /// </summary>
    /// <param name="manifest">The application manifest.</param>
    /// <param name="componentId">The egress component id.</param>
    /// <param name="parent">The parent to the egress health asset that will be created.</param>
    public EgressHealthOmfMessageCreator(IApplicationManifest manifest, string componentId, LinkNode parent) : base(parent)
    {
        ThrowHelper.ThrowIfArgumentNull(manifest, nameof(manifest));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNull(parent, nameof(parent));

        _version = Assembly.GetExecutingAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _machineName = manifest.MachineName;

        var healthPrefix = manifest.HealthPrefix ?? string.Empty;
        _baseStreamId = $"{healthPrefix}{manifest.MachineName}.{manifest.ServiceName}.{componentId}";
        _egressHealthId = $"{componentId}Health";
    }

    public override LinkNode GetComponentLink()
    {
        return new DataTypeLinkNode(_egressHealthId, GetComponentAssetId());
    }

    protected override DataType GetComponentType()
    {
        var properties = new Dictionary<string, PropertyDefinition>();
        DataType adapterType = new StaticDataType
        {
            Id = _egressHealthId,
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
        properties[Description] = stringProperty;
        properties[Host] = stringProperty;
        properties[Version] = stringProperty;

        adapterType.Properties = properties;
        return adapterType;
    }

    protected override ValueTuple<string, Classification, object> GetComponentAsset()
    {
        // This adapter component's health-specific asset
        var values = new Dictionary<string, object>
        {
            [IdPropertyName] = GetComponentAssetId(),
            [Description] = _description,
            [Host] = _machineName,
            [Version] = _version,
        };

        return (_egressHealthId, Classification.Static, values);
    }

    protected override string GetDeviceStatusStreamId() => $"{_baseStreamId}.{DeviceStatus}";

    protected override string GetHeartbeatStreamId() => $"{_baseStreamId}.{NextHealthMessageExpected}";

    protected override string GetComponentAssetId() => $"{_baseStreamId}";
}
