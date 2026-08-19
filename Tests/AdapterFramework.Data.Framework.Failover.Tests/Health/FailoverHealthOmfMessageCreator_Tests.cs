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
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Failover.Configuration;
using AdapterFramework.Data.Framework.Failover.Health;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests.Health;

public class FailoverHealthOmfMessageCreator_Tests : FailoverHealthOmfMessageCreator
{
    private const string MachineName = "Machine";
    private const string ServiceName = "Service";
    private const int TotalAssetAttributeCount = 7;

    private static readonly ClientFailoverConfiguration _clientFailoverConfiguration = new() { Mode = FailoverMode.Hot, };
    private static readonly LinkNode _parent = new DataTypeLinkNode(null, null);

    public FailoverHealthOmfMessageCreator_Tests() : base(new ApplicationManifest(5000, null, null, MachineName, ServiceName, OmfVersion.Omf12), _clientFailoverConfiguration, _parent)
    {
    }

    [Fact]
    public void GetComponentAsset_Test()
    {
        var asset = GetComponentAsset();
        var values = (Dictionary<string, object>)asset.Item3;
        Assert.Equal(TotalAssetAttributeCount, values.Count);
        Assert.Equal(MachineName, values[Host]);

        Assert.Contains(IdPropertyName, values.Keys);
        Assert.Contains(Version, values.Keys);
        Assert.Contains(Description, values.Keys);
        Assert.Contains(GroupIdProperty, values.Keys);
        Assert.Contains(FailoverModeProperty, values.Keys);
        Assert.Contains(FailoverEndpointProperty, values.Keys);
    }

    [Theory]
    [InlineData(FailoverMode.Cold, nameof(FailoverMode.Cold))]
    [InlineData(FailoverMode.Hot, nameof(FailoverMode.Hot))]
    [InlineData(FailoverMode.Warm, nameof(FailoverMode.Warm))]
    [InlineData(FailoverMode.NotConfigured, "")]
    public void GetComponentAsset_ClientConfiguration_Test(FailoverMode failoverMode, string expectedFailoverMode)
    {
        ClientFailoverConfiguration clientConfiguration = new()
        {
            FailoverGroupId = "groupA",
            Mode = failoverMode,
            Endpoint = "https:\\testurl",
        };

        Assert.Throws<ArgumentNullException>(() => GetComponentAsset(null));

        var asset = GetComponentAsset(clientConfiguration);

        var values = (Dictionary<string, object>)asset.Item3;
        Assert.Equal(TotalAssetAttributeCount, values.Count);
        Assert.Equal(values[GroupIdProperty], clientConfiguration.FailoverGroupId);
        Assert.Equal(values[FailoverModeProperty], expectedFailoverMode);
        Assert.Equal(values[FailoverEndpointProperty], clientConfiguration.Endpoint);

        clientConfiguration.FailoverGroupId = null;
        clientConfiguration.Endpoint = null;
        asset = GetComponentAsset(clientConfiguration);
        values = (Dictionary<string, object>)asset.Item3;
        Assert.Equal(values[GroupIdProperty], string.Empty);
        Assert.Equal(values[FailoverEndpointProperty], string.Empty);
    }

    [Theory]
    [InlineData(FailoverMode.Cold, nameof(FailoverMode.Cold))]
    [InlineData(FailoverMode.Hot, nameof(FailoverMode.Hot))]
    [InlineData(FailoverMode.Warm, nameof(FailoverMode.Warm))]
    [InlineData(FailoverMode.NotConfigured, "")]
    public void ResetComponentAsset_Test(FailoverMode failoverMode, string expectedFailoverMode)
    {
        ClientFailoverConfiguration clientConfiguration = new()
        {
            FailoverGroupId = "groupA",
            Mode = failoverMode,
            Endpoint = "https:\\testurl",
        };

        var asset = GetComponentAsset(clientConfiguration);

        var values = (Dictionary<string, object>)asset.Item3;
        Assert.Equal(TotalAssetAttributeCount, values.Count);
        Assert.Equal(values[GroupIdProperty], clientConfiguration.FailoverGroupId);
        Assert.Equal(values[FailoverModeProperty], expectedFailoverMode);
        Assert.Equal(values[FailoverEndpointProperty], clientConfiguration.Endpoint);

        asset = ResetComponentAsset();
        values = (Dictionary<string, object>)asset.Item3;
        Assert.Equal(values[GroupIdProperty], string.Empty);
        Assert.Equal(values[FailoverModeProperty], string.Empty);
        Assert.Equal(values[FailoverEndpointProperty], string.Empty);
    }

    [Fact]
    public void GetComponentType_Test()
    {
        var type = GetComponentType();
        var properties = type.Properties;

        Assert.Equal(TotalAssetAttributeCount, properties.Count);

        Assert.Contains(Host, properties.Keys);
        Assert.Contains(Version, properties.Keys);
        Assert.Contains(Description, properties.Keys);
        Assert.Contains(IdPropertyName, properties.Keys);
        Assert.Contains(GroupIdProperty, properties.Keys);
        Assert.Contains(FailoverModeProperty, properties.Keys);
        Assert.Contains(FailoverEndpointProperty, properties.Keys);
    }
}
