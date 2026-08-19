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
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.EgressComponent.Health;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.HistoryRecovery;

public class AdapterHistoryRecoveryService_Tests
{
    private const string ComponentId = "ComponentId";
    private const string ComponentType = "ComponentType";

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void AdapterHistoryRecoveryService_Constructor_Throws_Test(string badComponentId, string badComponentType)
    {
        var historyRecoveryState = new HistoryRecoveryState();
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockEdgeDataProtector = new Mock<IEdgeDataProtector>();
        var mockMessageProcessor = new Mock<IAdapterMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        using var egressHealthService = new EgressHealthService(mockHealthMessageProcessor.Object, mockLogger.Object, new ApplicationManifest(), ComponentId);

        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(null, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, ComponentId, egressHealthService));
        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(historyRecoveryState, null, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, ComponentId, egressHealthService));
        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, null, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, ComponentId, egressHealthService));
        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, null, mockEdgeDataProtector.Object, ComponentType, ComponentId, egressHealthService));
        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, null, ComponentType, ComponentId, egressHealthService));
        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, badComponentType, ComponentId, egressHealthService));
        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, badComponentId, egressHealthService));
        Assert.ThrowsAny<ArgumentException>(() => new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, ComponentId, null));
    }

    [Fact]
    public void AdapterHistoryRecoveryService_UpdateCheckpoint_Test()
    {
        var historyRecoveryState = new HistoryRecoveryState();
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockEdgeDataProtector = new Mock<IEdgeDataProtector>();
        var mockMessageProcessor = new Mock<IAdapterMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        using var egressHealthService = new EgressHealthService(mockHealthMessageProcessor.Object, mockLogger.Object, new ApplicationManifest(), ComponentId);

        var service = new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, ComponentId, egressHealthService);

        var expectedCheckpoint = DateTime.UtcNow;
        service.UpdateCheckpoint(expectedCheckpoint);

        Assert.Equal(expectedCheckpoint, historyRecoveryState.Checkpoint);
    }

    [Fact]
    public void AdapterHistoryRecoveryService_UpdateProgress_Test()
    {
        var historyRecoveryState = new HistoryRecoveryState();
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockEdgeDataProtector = new Mock<IEdgeDataProtector>();
        var mockMessageProcessor = new Mock<IAdapterMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        using var egressHealthService = new EgressHealthService(mockHealthMessageProcessor.Object, mockLogger.Object, new ApplicationManifest(), ComponentId);

        var service = new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, ComponentId, egressHealthService);

        var expectedProgress = 100;
        service.UpdateProgress(expectedProgress);

        Assert.Equal(expectedProgress, historyRecoveryState.Progress);
    }

    [Fact]
    public void AdapterHistoryRecoveryService_RecoveredEvents_Test()
    {
        var historyRecoveryState = new HistoryRecoveryState();
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockEdgeDataProtector = new Mock<IEdgeDataProtector>();
        var mockMessageProcessor = new Mock<IAdapterMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        using var egressHealthService = new EgressHealthService(mockHealthMessageProcessor.Object, mockLogger.Object, new ApplicationManifest(), ComponentId);

        var service = new AdapterHistoryRecoveryService(historyRecoveryState, mockLogger.Object, mockConfigurationProvider.Object, mockMessageProcessor.Object, mockEdgeDataProtector.Object, ComponentType, ComponentId, egressHealthService);

        var expectedRecoveredEvents = 100L;
        service.UpdateRecoveredEvents(expectedRecoveredEvents);

        Assert.Equal(expectedRecoveredEvents, historyRecoveryState.RecoveredEvents);
    }
}
