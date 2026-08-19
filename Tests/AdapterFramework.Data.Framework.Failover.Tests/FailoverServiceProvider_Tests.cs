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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests;

public class FailoverServiceProvider_Tests
{
    [Fact]
    public void FailoverServiceProvider_AddComponent_Test()
    {
        var services = new ServiceCollection();

        var mockLogger = new Mock<ILogger>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockDataProtector = new Mock<IEdgeDataProtector>();
        var mockSerializer = new Mock<ISerializer>();

        services.AddSingleton(mockLogger.Object);
        services.AddSingleton(mockApplicationManifest.Object);
        services.AddSingleton(mockConfigurationProvider.Object);
        services.AddSingleton(mockDiagnosticsMessageProcessor.Object);
        services.AddSingleton(mockHealthMessageProcessor.Object);
        services.AddSingleton(mockEndpointManager.Object);
        services.AddSingleton(mockDataProtector.Object);
        services.AddSingleton(mockSerializer.Object);

        FailoverServiceProvider.AddComponent(services, null);

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IFailoverManager>());
        Assert.NotNull(provider.GetService<IFailoverDataMessageProcessor>());
        Assert.NotNull(provider.GetService<IFailoverEndpointManager>());
    }

    [Fact]
    public async Task FailoverServiceProvider_StartAsync_Test()
    {
        var services = new ServiceCollection();
        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.SupportedFailoverModes).Returns(FailoverMode.Hot);

        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockFailoverManager = new Mock<IFailoverManager>();

        services.AddSingleton(mockAdapter.Object);
        var provider = services.BuildServiceProvider();

        using var failoverServiceProvider = new FailoverServiceProvider(mockLogger.Object, provider,
            mockConfigurationProvider.Object, mockRuntimeConfigurationRegistry.Object, mockFailoverManager.Object);

        await failoverServiceProvider.StartAsync(CancellationToken.None);

        mockFailoverManager.Verify(fm => fm.Initialize(It.IsAny<FailoverMode>()), Times.Once);
        mockFailoverManager.Verify(fm => fm.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailoverServiceProvider_StartAsync_NoModesSupported_Test()
    {
        var services = new ServiceCollection();
        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.SupportedFailoverModes).Returns(FailoverMode.NotConfigured);

        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockFailoverManager = new Mock<IFailoverManager>();

        services.AddSingleton(mockAdapter.Object);
        var provider = services.BuildServiceProvider();

        using var failoverServiceProvider = new FailoverServiceProvider(mockLogger.Object, provider,
            mockConfigurationProvider.Object, mockRuntimeConfigurationRegistry.Object, mockFailoverManager.Object);

        await failoverServiceProvider.StartAsync(CancellationToken.None);

        mockFailoverManager.Verify(fm => fm.Initialize(It.IsAny<FailoverMode>()), Times.Never);
        mockFailoverManager.Verify(fm => fm.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FailoverServiceProvider_StopAsync_Test()
    {
        var services = new ServiceCollection();
        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.SupportedFailoverModes).Returns(FailoverMode.Hot);

        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockFailoverManager = new Mock<IFailoverManager>();

        services.AddSingleton(mockAdapter.Object);
        var provider = services.BuildServiceProvider();

        using var failoverServiceProvider = new FailoverServiceProvider(mockLogger.Object, provider,
            mockConfigurationProvider.Object, mockRuntimeConfigurationRegistry.Object, mockFailoverManager.Object);

        await failoverServiceProvider.StartAsync(CancellationToken.None);
        await failoverServiceProvider.StopAsync(CancellationToken.None);

        mockFailoverManager.Verify(fm => fm.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void FailoverServiceProvider_ResendHealthMetadata_Test()
    {
        var services = new ServiceCollection();
        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.SupportedFailoverModes).Returns(FailoverMode.Hot);

        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockFailoverManager = new Mock<IFailoverManager>();

        services.AddSingleton(mockAdapter.Object);
        var provider = services.BuildServiceProvider();

        using var failoverServiceProvider = new FailoverServiceProvider(mockLogger.Object, provider,
            mockConfigurationProvider.Object, mockRuntimeConfigurationRegistry.Object, mockFailoverManager.Object);

        failoverServiceProvider.ResendHealthMetadata();

        mockFailoverManager.Verify(fm => fm.ResendHealthAndDiagnostics(), Times.Once);
    }
}
