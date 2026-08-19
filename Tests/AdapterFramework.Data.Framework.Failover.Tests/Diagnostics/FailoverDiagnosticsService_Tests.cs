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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Failover.Health;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests.Diagnostics;

public class FailoverDiagnosticsService_Tests
{
    private readonly FailoverState _failoverState;
    private readonly List<DataType> _types;
    private readonly List<DataStream> _streams;
    private readonly List<string> _data;
    private readonly Mock<ILogger> _logger;
    private readonly IDiagnosticsMessageProcessor _fakeDiagnosticsMessageProcessor;
    private readonly LinkNode _node = new DataTypeLinkNode("fakeType", "fake.index");

    public FailoverDiagnosticsService_Tests()
    {
        _failoverState = new FailoverState();
        _logger = new();
        _types = new List<DataType>();
        _streams = new List<DataStream>();
        _data = new List<string>();
        _fakeDiagnosticsMessageProcessor = new FakeDiagnosticsMessageProcessor(_types, _streams, _data);
    }

    [Fact]
    public void InvalidInput_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        FailoverState failoverState = new();

        Assert.Throws<ArgumentNullException>(() => new FailoverDiagnosticsService(mockDiagnosticsMessageProcessor.Object, failoverState, mockLogger.Object, _node, null));
        Assert.Throws<ArgumentNullException>(() => new FailoverDiagnosticsService(mockDiagnosticsMessageProcessor.Object, failoverState, mockLogger.Object, null, mockApplicationManifest.Object));
    }

    [Fact]
    public async Task Initialize_Test()
    {
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        using var diagnosticsService = new FailoverDiagnosticsService(_fakeDiagnosticsMessageProcessor, _failoverState, _logger.Object, _node, mockApplicationManifest.Object);
        await diagnosticsService.InitializeAsync();

        VerifyTypesStreamData();
    }

    [Fact]
    public void ResendTypesAndStreams_Test()
    {
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        using var diagnosticsService = new FailoverDiagnosticsService(_fakeDiagnosticsMessageProcessor, _failoverState, _logger.Object, _node, mockApplicationManifest.Object);

        diagnosticsService.ResendTypesAndStreams();

        VerifyTypesStreamData();
    }

    [Fact]
    public async Task SendFailoverState_Test()
    {
        var failoverState = new FailoverState()
        {
            FailoverScore = 68,
            Role = FailoverRole.Primary,
        };

        var mockApplicationManifest = new Mock<IApplicationManifest>();
        using var diagnosticsService = new FailoverDiagnosticsService(_fakeDiagnosticsMessageProcessor, failoverState, _logger.Object, _node, mockApplicationManifest.Object);
        await diagnosticsService.InitializeAsync();
        await diagnosticsService.StartAsync();

        Assert.Throws<ArgumentNullException>(() => diagnosticsService.SendFailoverState(null));
        diagnosticsService.SendFailoverState(failoverState);
        diagnosticsService.SendFailoverState(failoverState);
        Assert.Single(_data.FindAll(x => x.Contains(FailoverConstants.FailoverStatusStreamName, StringComparison.InvariantCultureIgnoreCase)));

        var failoverState2 = new FailoverState()
        {
            FailoverScore = 68,
            Role = FailoverRole.Secondary,
        };
        diagnosticsService.SendFailoverState(failoverState2);
        Assert.Equal(2, _data.FindAll(x => x.Contains(FailoverConstants.FailoverStatusStreamName, StringComparison.InvariantCultureIgnoreCase)).Count);

        await diagnosticsService.StopAsync();
    }

    [Fact]
    public async Task ResetFailoverState_Test()
    {
        var failoverState = new FailoverState()
        {
            FailoverScore = 68,
            Role = FailoverRole.Primary,
        };

        var mockApplicationManifest = new Mock<IApplicationManifest>();
        using var diagnosticsService = new FailoverDiagnosticsService(_fakeDiagnosticsMessageProcessor, failoverState, _logger.Object, _node, mockApplicationManifest.Object);
        await diagnosticsService.InitializeAsync();
        await diagnosticsService.StartAsync();

        diagnosticsService.SendFailoverState(failoverState);
        Assert.Single(_data.FindAll(x => x.Contains(FailoverConstants.FailoverStatusStreamName, StringComparison.InvariantCultureIgnoreCase)));

        diagnosticsService.ResetFailoverState();
        Assert.Equal(2, _data.FindAll(x => x.Contains(FailoverConstants.FailoverStatusStreamName, StringComparison.InvariantCultureIgnoreCase)).Count);

        diagnosticsService.ResetFailoverState();
        Assert.Equal(2, _data.FindAll(x => x.Contains(FailoverConstants.FailoverStatusStreamName, StringComparison.InvariantCultureIgnoreCase)).Count);

        await diagnosticsService.StopAsync();
    }

    private void VerifyTypesStreamData()
    {
        Assert.Single(_types);
        var properties = _types[0].Properties;
        Assert.Contains(FailoverConstants.TimestampPropertyName, properties.Keys);
        Assert.Contains(FailoverConstants.FailoverRolePropertyName, properties.Keys);
        Assert.Contains(FailoverConstants.FailoverScorePropertyName, properties.Keys);

        Assert.NotEmpty(_streams);
        Assert.Null(_streams[0].Metadata);

        Assert.NotEmpty(_data);
        Assert.Single(_data.FindAll(x => x.Contains(FailoverConstants.FailoverStatusStreamName, StringComparison.InvariantCultureIgnoreCase)));
    }
}
