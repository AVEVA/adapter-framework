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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Failover.Health;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests.Diagnostics;

public class FailoverDiagnosticsOmfMessageCreator_Tests
{
    private const string MachineName = "Machine";
    private const string ServiceName = "Service";
    private const string LinkAssetId = "Link.Asset.ID";
    private const string FailoverHealthId = "Failover.Health";

    private readonly FailoverDiagnosticsOmfMessageCreator _messageCreator;

    public FailoverDiagnosticsOmfMessageCreator_Tests()
    {
        var linkNode = new DataTypeLinkNode(FailoverHealthId, LinkAssetId);
        _messageCreator = new FailoverDiagnosticsOmfMessageCreator(new ApplicationManifest(5000, null, null, MachineName, ServiceName, OmfVersion.Omf12), linkNode);
    }

    [Fact]
    public void InvalidInput_Test()
    {
        Assert.Throws<ArgumentNullException>(() => new FailoverDiagnosticsOmfMessageCreator(null, null));
        Assert.Throws<ArgumentNullException>(() => new FailoverDiagnosticsOmfMessageCreator(new ApplicationManifest(5000, null, null, null, ServiceName, OmfVersion.Omf12), null));
        Assert.Throws<ArgumentNullException>(() => new FailoverDiagnosticsOmfMessageCreator(new ApplicationManifest(5000, null, null, MachineName, null, OmfVersion.Omf12), null));
        Assert.Throws<ArgumentNullException>(() => new FailoverDiagnosticsOmfMessageCreator(new ApplicationManifest(5000, null, null, null, null, OmfVersion.Omf12), null));
    }

    [Fact]
    public void GetFailoverStatusStreamId_Test()
    {
        var expectedStreamId = $"{MachineName}.{ServiceName}.{FailoverConstants.FailoverKeyword}.{FailoverConstants.FailoverStatusStreamName}";
        var streamId = _messageCreator.GetFailoverStatusStreamId();
        Assert.Equal(expectedStreamId, streamId);
    }
}
