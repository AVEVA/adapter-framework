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
using AdapterFramework.Data.Framework.Common.Constants;
using AdapterFramework.Data.Framework.EgressComponent.Diagnostics;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests.Diagnostics;

public class EgressDiagnosticsOmfMessageCreator_Tests
{
    public const string ComponentId = "OmfEgress";
    public const string LinkAssetId = "Link.Asset.ID";
    public const string EgressHealthId = ComponentId + "Health";

    private readonly EgressDiagnosticsOmfMessageCreator _messageCreator;

    public EgressDiagnosticsOmfMessageCreator_Tests()
    {
        var linkNode = new DataTypeLinkNode(EgressHealthId, LinkAssetId);
        _messageCreator = new EgressDiagnosticsOmfMessageCreator(ComponentId, linkNode);
    }

    [Fact]
    public void EgressDiagnosticsOmfMessageCreator_Constructor_InvalidInput_Test()
    {
        Assert.Throws<ArgumentNullException>(() => new EgressDiagnosticsOmfMessageCreator(ComponentId, null));
        Assert.Throws<ArgumentNullException>(() => new EgressDiagnosticsOmfMessageCreator(null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EgressDiagnosticsOmfMessageCreator(string.Empty, null));
        Assert.Throws<ArgumentException>(() => new EgressDiagnosticsOmfMessageCreator(" ", null));
    }

    [Fact]
    public void EgressDiagnosticsOmfMessageCreator_CreateStream_Test()
    {
        const string EndpointId = "UnitTestEndpoint";
        var expectedStreamId = $"{ComponentId}.{EndpointId}.{DiagnosticsConstants.IoRateStreamName}";

        var stream = _messageCreator.CreateIoRateStream(EndpointId);

        Assert.Equal(expectedStreamId, stream.Id);
        Assert.Equal($"{EndpointId}.{DiagnosticsConstants.IoRateStreamName}", stream.Name);
        Assert.NotEmpty(stream.TypeId);
    }

    [Fact]
    public void EgressDiagnosticsOmfMessageCreator_CreateLink_Test()
    {
        const string StreamIdToLink = "Gnarly.Stream.Id";

        var (index, classification, link) = _messageCreator.CreateLink(StreamIdToLink);

        var rawLink = (Link)link;
        Assert.Equal(StreamIdToLink, rawLink.Target.Id);
        Assert.Equal(EgressHealthId, rawLink.Source.Id);
        Assert.Equal(LinkAssetId, rawLink.Source.Index);
        Assert.Equal(Tokens.Link, index);
        Assert.Equal(Classification.Static, classification);
    }

    [Fact]
    public void EgressDiagnosticsOmfMessageCreator_CreateLink_InvalidInput_Test()
    {
        Assert.Throws<ArgumentNullException>(() => _messageCreator.CreateLink(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => _messageCreator.CreateLink(string.Empty));
        Assert.Throws<ArgumentException>(() => _messageCreator.CreateLink(" "));
    }

    [Fact]
    public void EgressDiagnosticsOmfMessageCreator_GetTypes_Test()
    {
        var diagnosticsTypes = EgressDiagnosticsOmfMessageCreator.GetTypes();

        Assert.Single(diagnosticsTypes);
        Assert.Equal(DiagnosticsConstants.IoRateTypeId, diagnosticsTypes[0].Id);
    }
}
