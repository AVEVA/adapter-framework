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
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.EgressComponent.Health;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests.Health;

public class EgressHealthOmfMessageCreator_Tests : EgressHealthOmfMessageCreator
{
    private const string MachineName = "Machine";
    private const string ServiceName = "Service";
    private const string ComponentId = "Component";
    
    private static readonly LinkNode _parent = new DataTypeLinkNode(null, null);

    public EgressHealthOmfMessageCreator_Tests() : base(new ApplicationManifest(5000, null, null, MachineName, ServiceName, OmfVersion.Omf12), ComponentId, _parent)
    {
    }

    [Fact]
    public void GetComponentType_Test()
    {
        var componentType = GetComponentType();
        var properties = componentType.Properties;
        Assert.Equal(4, properties.Count);
        Assert.Contains(Host, properties.Keys);
        Assert.Contains(Version, properties.Keys);
        Assert.Contains(Description, properties.Keys);
        Assert.Contains(IdPropertyName, properties.Keys);
    }

    [Fact]
    public void GetComponentAsset_Test()
    {
        var asset = GetComponentAsset();
        var values = (Dictionary<string, object>)asset.Item3;
        Assert.Equal(4, values.Count);
        Assert.Equal(MachineName, values[Host]);

        Assert.Contains(IdPropertyName, values.Keys);
        Assert.Contains(Version, values.Keys);
        Assert.Contains(Description, values.Keys);
    }
}
