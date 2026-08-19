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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using AdapterFramework.Data.Framework.Host.Administration;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests;

public class EgressComponent_Tests
{
    private const long DefaultLogFileSizeLimitBytes = 1073741824 / 31;
    private const int DefaultLogFileCountLimit = 31;
    private const LogLevel DefaultLogLevel = LogLevel.Information;
    private const string UnitTestComponentType = "UnitTestComponentType";

    [Fact]
    public void EgressComponent_AddComponent_Test()
    {
        var mockComponentIdService = new Mock<IComponentIdService>();

        mockComponentIdService
            .Setup(componentIdService => componentIdService.GetEdgeComponentId(It.IsAny<string>()))
            .Returns("Test");

        var servicesDictionary = new Dictionary<Type, object> { { typeof(IComponentIdService), mockComponentIdService.Object } };
        var services = new ServiceCollection();

        OmfEgressComponent.AddComponent(services, servicesDictionary);

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IEgressComponentIdService>());
    }

    [Fact]
    public async Task EgressComponent_InitializeAsync_Test()
    {
        var componentId = "EgressComponentTestId";
        var testLogger = new TestLogger();
        var loggingConfigurationRegistered = false;
        var mockLogManager = new Mock<ILogManager>();
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockRuntimeAdministrationRegistry = new Mock<IRuntimeAdministrationRegistry>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockEdgeComponentsOperationService = new Mock<IEdgeComponentsOperationService>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockLogManager.Setup(logManager => logManager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>()))
            .Returns(testLogger);

        mockEgressComponentIdService.Setup(egressComponentIdService => egressComponentIdService.ComponentId).Returns(componentId);

        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns("a/b/c/d");
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryName()).Returns(UnitTestComponentType);
        mockOmfDataEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long>());

        mockRuntimeConfigurationRegistry.Setup(configurationRegistry =>
                configurationRegistry.RegisterComponentConfiguration<LoggerConfiguration>(
                    componentId, EdgeSystemConstants.LoggingFacetName, It.IsAny<IConfigurationCommandGenerator>(),
                    It.IsAny<Action<ConfigurationChangedEventArgs>>(), It.IsAny<Func<string>>(),
                    It.IsAny<Func<ConfigurationChangedEventArgs, ICollection<string>>>(), It.IsAny<Operations>()))
            .Callback(() => loggingConfigurationRegistered = true);

        using var egressComponent = new OmfEgressComponent(mockLogManager.Object, mockEgressComponentIdService.Object,
            mockRuntimeConfigurationRegistry.Object, mockRuntimeAdministrationRegistry.Object, mockConfigurationProvider.Object,
            mockEdgeComponentsOperationService.Object, mockOmfDataEndpointManager.Object,
            mockDiagnosticsMessageProcessor.Object, mockHealthMessageProcessor.Object, mockApplicationManifest.Object);

        await egressComponent.InitializeAsync();

        Assert.Equal(componentId, egressComponent.ComponentId);
        Assert.Empty(testLogger.GetLogMessages());
        Assert.False(loggingConfigurationRegistered);
    }

    [Fact]
    public async Task EgressComponent_StartAsync_Test()
    {
        var testLogger = new TestLogger();

        var mockLogManager = new Mock<ILogManager>();
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockRuntimeAdministrationRegistry = new Mock<IRuntimeAdministrationRegistry>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockEdgeComponentsOperationService = new Mock<IEdgeComponentsOperationService>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockOmfDataEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long>());

        mockLogManager.Setup(logManager => logManager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>())).Returns(testLogger);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns("a/b/c/d");
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryName()).Returns(UnitTestComponentType);

        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var diagnosticsMessageProcessor = new FakeDiagnosticsMessageProcessor
        {
            StreamMetadataLevel = MetadataInfo.High,
        };

        mockEgressComponentIdService.Setup(egressComponentIdService => egressComponentIdService.ComponentId).Returns("TestId");

        using var egressComponent = new OmfEgressComponent(mockLogManager.Object, mockEgressComponentIdService.Object,
            mockRuntimeConfigurationRegistry.Object, mockRuntimeAdministrationRegistry.Object, mockConfigurationProvider.Object,
            mockEdgeComponentsOperationService.Object, mockOmfDataEndpointManager.Object,
            diagnosticsMessageProcessor, mockHealthMessageProcessor.Object, mockApplicationManifest.Object);

        await egressComponent.InitializeAsync();

        testLogger.ClearLog();

        await egressComponent.StartAsync();

        var logMessages = testLogger.GetLogMessages();

        Assert.NotEmpty(logMessages);
        Assert.Equal(LogLevel.Debug, logMessages[0].LogLevel);
        Assert.Contains(logMessages, x => x.LogMessage.Contains("started", StringComparison.InvariantCulture));
    }

    [Fact]
    public async Task EgressComponent_StopAsync_Test()
    {
        var testLogger = new TestLogger();
        var mockLogManager = new Mock<ILogManager>();
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockRuntimeAdministrationRegistry = new Mock<IRuntimeAdministrationRegistry>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockEdgeComponentsOperationService = new Mock<IEdgeComponentsOperationService>();
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockOmfDataEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long>());

        mockLogManager.Setup(logManager => logManager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>())).Returns(testLogger);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns("a/b/c/d");
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryName()).Returns(UnitTestComponentType);
        mockEgressComponentIdService.Setup(egressComponentIdService => egressComponentIdService.ComponentId).Returns("TestId");

        using var egressComponent = new OmfEgressComponent(mockLogManager.Object, mockEgressComponentIdService.Object,
            mockRuntimeConfigurationRegistry.Object, mockRuntimeAdministrationRegistry.Object, mockConfigurationProvider.Object,
            mockEdgeComponentsOperationService.Object, mockOmfDataEndpointManager.Object,
            mockDiagnosticsMessageProcessor.Object, mockHealthMessageProcessor.Object, mockApplicationManifest.Object);

        await egressComponent.InitializeAsync();
        await egressComponent.StartAsync();

        testLogger.ClearLog();

        await egressComponent.StopAsync();

        var logMessages = testLogger.GetLogMessages();

        Assert.NotEmpty(logMessages);
        Assert.Equal(LogLevel.Debug, logMessages[0].LogLevel);
        Assert.Contains("stopped", logMessages[0].LogMessage, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void EgressComponent_ResendHealthMetadata_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockLogManager = new Mock<ILogManager>();
        mockLogManager.Setup(logManager => logManager.GetOrCreateLogger(It.IsAny<string>(), null))
            .Returns(mockLogger.Object);
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        mockEgressComponentIdService.Setup(componentIdService => componentIdService.ComponentId).Returns("Egress");
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockRuntimeAdministrationRegistry = new Mock<IRuntimeAdministrationRegistry>();
        var mockConfigurationProvider = TestUtilities.GetMockConfigurationProvider();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockEdgeComponentsOperationService = new Mock<IEdgeComponentsOperationService>();
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryName()).Returns(UnitTestComponentType);

        using var egressComponent = new OmfEgressComponent(mockLogManager.Object, mockEgressComponentIdService.Object,
            mockRuntimeConfigurationRegistry.Object, mockRuntimeAdministrationRegistry.Object, mockConfigurationProvider.Object,
            mockEdgeComponentsOperationService.Object, mockOmfDataEndpointManager.Object,
            mockDiagnosticsMessageProcessor.Object, mockHealthMessageProcessor.Object, mockApplicationManifest.Object);

        // make sure the operation does not throw
        egressComponent.ResendHealthMetadata();
    }

    [Fact]
    public async Task EgressComponent_EgressEndpointsChangeAction_Test()
    {
        var componentId = "EgressComponentTestId";
        var testLogger = new TestLogger();
        var mockLogManager = new Mock<ILogManager>();
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockRuntimeAdministrationRegistry = new Mock<IRuntimeAdministrationRegistry>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockEdgeComponentsOperationService = new Mock<IEdgeComponentsOperationService>();
        Action<ConfigurationChangedEventArgs> egressEndpointsChangedAction = null;
        var resendDynamicMetadataCalled = false;
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockOmfDataEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long>());

        mockLogManager.Setup(logManager => logManager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>()))
            .Returns(testLogger);

        mockEgressComponentIdService.Setup(egressComponentIdService => egressComponentIdService.ComponentId).Returns(componentId);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns("a/b/c/d");
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryName()).Returns(UnitTestComponentType);

        mockRuntimeConfigurationRegistry.Setup(configurationRegistry =>
                configurationRegistry.RegisterComponentConfiguration<EgressEndpointConfiguration[]>(
                    componentId, EdgeSystemConstants.DataEndpointsFacetName,
                    It.IsAny<IConfigurationCommandGenerator>(),
                    It.IsAny<Action<ConfigurationChangedEventArgs>>(), It.IsAny<Func<string>>(),
                    It.IsAny<Func<ConfigurationChangedEventArgs, ICollection<string>>>(), It.IsAny<Operations>()))
            .Callback((string componentIdentifier, string facetName,
                IConfigurationCommandGenerator commandGenerator, Action<ConfigurationChangedEventArgs> callback,
                Func<string> helpFunc, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidation,
                Operations supportedOperations) =>
            {
                egressEndpointsChangedAction = callback;
                Assert.NotNull(helpFunc);
            });

        mockOmfDataEndpointManager.Setup(endpointManager => endpointManager.AddRemoveEndpoints(It.IsAny<ConfigurationChangedEventArgs>(), OmfWriterType.Data))
            .Returns(true);

        mockEdgeComponentsOperationService.Setup(operationService => operationService.ResendDynamicMetadata())
            .Callback(() => resendDynamicMetadataCalled = true);

        using var egressComponent = new OmfEgressComponent(mockLogManager.Object, mockEgressComponentIdService.Object,
            mockRuntimeConfigurationRegistry.Object, mockRuntimeAdministrationRegistry.Object, mockConfigurationProvider.Object,
            mockEdgeComponentsOperationService.Object, mockOmfDataEndpointManager.Object,
            mockDiagnosticsMessageProcessor.Object, mockHealthMessageProcessor.Object, mockApplicationManifest.Object);

        await egressComponent.InitializeAsync();
        await egressComponent.StartAsync();

        egressEndpointsChangedAction(new ConfigurationChangedEventArgs(null, null));

        Assert.True(resendDynamicMetadataCalled);

        resendDynamicMetadataCalled = false;

        mockOmfDataEndpointManager.Setup(endpointManager =>
                endpointManager.AddRemoveEndpoints(It.IsAny<ConfigurationChangedEventArgs>(), OmfWriterType.Data))
            .Returns(false);

        egressEndpointsChangedAction.Invoke(new ConfigurationChangedEventArgs(null, null));

        Assert.False(resendDynamicMetadataCalled);
    }

    [Theory]
    [InlineData(DefaultLogLevel, 20, DefaultLogFileSizeLimitBytes)]
    [InlineData(DefaultLogLevel, DefaultLogFileCountLimit, 34636822)]
    [InlineData(LogLevel.Critical, DefaultLogFileCountLimit, DefaultLogFileSizeLimitBytes)]
    public async Task EgressComponent_LoggingConfigurationChange_Test(LogLevel logLevel, int logFileCountLimit, long logFileSizeLimit)
    {
        const string ComponentId = "EgressComponentTestId";
        string configChangeMessage = "Logging configuration change will take effect only on Adapter restart.";
        var testLogger = new TestLogger();
        var mockLogManager = new Mock<ILogManager>();
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockRuntimeAdministrationRegistry = new Mock<IRuntimeAdministrationRegistry>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockEdgeComponentsOperationService = new Mock<IEdgeComponentsOperationService>();
        Action<ConfigurationChangedEventArgs> updateLoggingConfigurationAction = null;
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockLogManager
            .Setup(logManager =>
                logManager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>()))
            .Returns(testLogger);

        mockEgressComponentIdService.Setup(egressComponentIdService => egressComponentIdService.ComponentId).Returns(ComponentId);
        mockRuntimeConfigurationRegistry.Setup(configurationRegistry =>
                configurationRegistry.RegisterComponentConfiguration<LoggerConfiguration>(
                    ComponentId, EdgeSystemConstants.LoggingFacetName, It.IsAny<IConfigurationCommandGenerator>(),
                    It.IsAny<Action<ConfigurationChangedEventArgs>>(), It.IsAny<Func<string>>(),
                    It.IsAny<Func<ConfigurationChangedEventArgs, ICollection<string>>>(), It.IsAny<Operations>()))
            .Callback((string componentIdentifier, string facetName,
                IConfigurationCommandGenerator commandGenerator, Action<ConfigurationChangedEventArgs> callback,
                Func<string> helpFunc, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidation,
                Operations supportedOperations) =>
            {
                updateLoggingConfigurationAction = callback;
                Assert.NotNull(helpFunc);
            });

        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryName()).Returns(UnitTestComponentType);

        using var egressComponent = new OmfEgressComponent(mockLogManager.Object, mockEgressComponentIdService.Object,
            mockRuntimeConfigurationRegistry.Object, mockRuntimeAdministrationRegistry.Object, mockConfigurationProvider.Object,
            mockEdgeComponentsOperationService.Object, mockOmfDataEndpointManager.Object,
            mockDiagnosticsMessageProcessor.Object, mockHealthMessageProcessor.Object, mockApplicationManifest.Object);

        await egressComponent.InitializeAsync();
        await egressComponent.StartAsync();

        var oldLoggerConfiguration = new LoggerConfiguration();
        var newLoggerConfiguration = new LoggerConfiguration
        {
            LogLevel = logLevel,
            LogFileCountLimit = logFileCountLimit,
            LogFileSizeLimitBytes = logFileSizeLimit,
        };

        var configurationChangedEvent = new ConfigurationChangedEventArgs(oldLoggerConfiguration, newLoggerConfiguration);
        updateLoggingConfigurationAction.Invoke(configurationChangedEvent);

        var logMessages = testLogger.GetLogMessages();

        if (logFileCountLimit != DefaultLogFileCountLimit || logFileSizeLimit != DefaultLogFileSizeLimitBytes)
        {
            Assert.Equal(LogLevel.Warning, logMessages.Last().LogLevel);
            Assert.Equal(logMessages.Last().LogMessage, configChangeMessage);
        }
        else
        {
            configChangeMessage = "Component has been started.";
            Assert.Equal(LogLevel.Debug, logMessages.Last().LogLevel);
            Assert.Equal(logMessages.Last().LogMessage, configChangeMessage);
        }
    }

    [Fact]
    public async Task EgressComponent_ResetDataBuffers_Test()
    {
        const string ComponentId = "EgressComponentTestId";
        var testLogger = new TestLogger();
        var mockLogManager = new Mock<ILogManager>();
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockRuntimeAdministrationRegistry = new Mock<IRuntimeAdministrationRegistry>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockEdgeComponentsOperationService = new Mock<IEdgeComponentsOperationService>();
        Func<Task> resetCallbackFunc = null;
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockRuntimeAdministrationRegistry.Setup(runtimeAdministrationRegistry =>
                runtimeAdministrationRegistry.RegisterComponentCallbackFunction(It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Func<Task>>()))
            .Callback((string componentId, string functionName, Func<Task> callback) =>
                {
                    Assert.Equal(ComponentId, componentId);
                    Assert.Equal(EdgeSystemConstants.ResetFunctionName, functionName);
                    resetCallbackFunc = callback;
                });

        IList<string> list;
        Func<Task> callbackFunction;
        bool tryGetCallbackCalled = false;
        var id = (ComponentId, EdgeSystemConstants.ResetFunctionName);
        mockRuntimeAdministrationRegistry.Setup(runtimeAdministrationRegistry =>
                runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(It.IsAny<string>(), out list)).Returns(true);
        mockRuntimeAdministrationRegistry.Setup(runtimeAdministrationRegistry =>
                runtimeAdministrationRegistry.TryGetCallbackFunction(id, out callbackFunction))
            .Callback(((string, string) cid, out Func<Task> theCallbackFunction) =>
            {
                tryGetCallbackCalled = true;
                theCallbackFunction = resetCallbackFunc;
            })
            .Returns(true);

        mockLogManager
            .Setup(logManager =>
                logManager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>()))
            .Returns(testLogger);

        mockEgressComponentIdService.Setup(egressComponentIdService => egressComponentIdService.ComponentId).Returns(ComponentId);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryName()).Returns(UnitTestComponentType);

        using var egressComponent = new OmfEgressComponent(mockLogManager.Object, mockEgressComponentIdService.Object,
            mockRuntimeConfigurationRegistry.Object, mockRuntimeAdministrationRegistry.Object, mockConfigurationProvider.Object,
            mockEdgeComponentsOperationService.Object, mockOmfDataEndpointManager.Object,
            mockDiagnosticsMessageProcessor.Object, mockHealthMessageProcessor.Object, mockApplicationManifest.Object);

        await egressComponent.InitializeAsync();
        await egressComponent.StartAsync();

        using var systemAdministrationController = new SystemAdministrationController(mockRuntimeAdministrationRegistry.Object);
        await systemAdministrationController.ExecuteCallbackFunctionAsync(ComponentId, EdgeSystemConstants.ResetFunctionName);

        Assert.NotNull(resetCallbackFunc);
        Assert.True(tryGetCallbackCalled);
        
        var logMessages = testLogger.GetLogMessages();
        Assert.Equal(LogLevel.Debug, logMessages.Last().LogLevel);
    }
}
