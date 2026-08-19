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
using System.Collections.Generic;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Failover.Configuration;

namespace AdapterFramework.Data.Framework.Failover.Health;

public class FailoverHealthOmfMessageCreator : HealthOmfMessageCreatorBase
{
    protected const string IdPropertyName = "Name";
    protected const string Description = "Description";
    protected const string Version = "Version";
    protected const string Host = "Host";
    protected const string GroupIdProperty = "Failover Group ID";
    protected const string FailoverModeProperty = "Failover Mode";
    protected const string FailoverEndpointProperty = "Failover Endpoint";

    private readonly string _baseStreamId;
    private readonly string _machineName;
    private readonly string _version;
    private readonly string _failoverHealthTypeId = "FailoverHealth";
    private readonly string _descriptionValue = "Failover Health";
    private string _groupId;
    private string _failoverMode;
    private string _failoverEndpoint;

    public FailoverHealthOmfMessageCreator(IApplicationManifest manifest, ClientFailoverConfiguration configuration, LinkNode parent) : base(parent)
    {
        ThrowHelper.ThrowIfArgumentNull(manifest, nameof(manifest));
        ThrowHelper.ThrowIfArgumentNull(parent, nameof(parent));
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        _machineName = manifest.MachineName;

        var healthPrefix = manifest.HealthPrefix ?? string.Empty;
        _baseStreamId = $"{healthPrefix}{_machineName}.{manifest.ServiceName}.{FailoverConstants.FailoverKeyword}";

        _version = manifest.ProductVersion.ToString();
        _groupId = configuration.FailoverGroupId ?? string.Empty;
        _failoverMode = configuration.Mode.ToString();
        _failoverEndpoint = configuration.Endpoint ?? string.Empty;
    }

    public override LinkNode GetComponentLink()
    {
        return new DataTypeLinkNode(_failoverHealthTypeId, GetComponentAssetId());
    }

    public (string, Classification, object) GetComponentAsset(ClientFailoverConfiguration configuration)
    {
        // returns an asset instance with an updated failoverMode and groupId
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        var failoverMode = configuration.Mode.ToString();
        if (configuration.Mode == FailoverMode.NotConfigured)
        {
            failoverMode = string.Empty;
        }

        _groupId = configuration.FailoverGroupId ?? string.Empty;
        _failoverMode = failoverMode;
        _failoverEndpoint = configuration.Endpoint ?? string.Empty;

        return GetComponentAsset();
    }

    public (string, Classification, object) ResetComponentAsset()
    {
        _groupId = string.Empty;
        _failoverMode = string.Empty;
        _failoverEndpoint = string.Empty;

        return GetComponentAsset();
    }

    protected override (string, Classification, object) GetComponentAsset()
    {
        var values = new Dictionary<string, object>
        {
            [IdPropertyName] = GetComponentAssetId(),
            [Description] = _descriptionValue,
            [Host] = _machineName,
            [Version] = _version,
            [GroupIdProperty] = _groupId,
            [FailoverModeProperty] = _failoverMode,
            [FailoverEndpointProperty] = _failoverEndpoint,
        };

        return (_failoverHealthTypeId, Classification.Static, values);
    }

    protected override DataType GetComponentType()
    {
        var properties = new Dictionary<string, PropertyDefinition>();
        DataType adapterType = new StaticDataType
        {
            Id = _failoverHealthTypeId,
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
        properties[Host] = stringProperty;
        properties[Version] = stringProperty;
        properties[GroupIdProperty] = stringProperty;
        properties[FailoverModeProperty] = stringProperty;
        properties[FailoverEndpointProperty] = stringProperty;

        adapterType.Properties = properties;
        return adapterType;
    }

    protected override string GetDeviceStatusStreamId() => $"{_baseStreamId}.{DeviceStatus}";

    protected override string GetHeartbeatStreamId() => $"{_baseStreamId}.{NextHealthMessageExpected}";

    protected override string GetComponentAssetId() => $"{_baseStreamId}";
}
