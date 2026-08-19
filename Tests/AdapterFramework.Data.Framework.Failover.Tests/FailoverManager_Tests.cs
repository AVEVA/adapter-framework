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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Failover.Configuration;
using AdapterFramework.Data.Framework.Failover.Diagnostics;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests;

public class FailoverManager_Tests
{
    private const int Timeout = 2000;
    private const string MachineName = "MachineName";
    private const string ServiceName = "ServiceName";

    private const string FailoverHealthType = "FailoverHealth";
    private const string GroupIdProperty = "Failover Group ID";
    private const string FailoverModeProperty = "Failover Mode";
    private const string FailoverEndpointProperty = "Failover Endpoint";

    private readonly Version _productVersion = new(1, 1, 0);

    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IApplicationManifest> _mockApplicationManifest;
    private readonly Mock<IDiagnosticsMessageProcessor> _mockDiagnosticsProcessor;
    private readonly Mock<IHealthMessageProcessor> _mockHealthProcessor;
    private readonly Mock<IConfigurationProvider> _mockConfigurationProvider;
    private readonly Mock<IFailoverDataMessageProcessor> _mockFailoverDataMessageProcessor;
    private readonly Mock<IFailoverEndpointManager> _mockFailoverEndpointManager;

    public FailoverManager_Tests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockApplicationManifest = GetSetupMockApplicationManifest();
        _mockDiagnosticsProcessor = new Mock<IDiagnosticsMessageProcessor>();
        _mockHealthProcessor = new Mock<IHealthMessageProcessor>();
        _mockConfigurationProvider = new Mock<IConfigurationProvider>();
        _mockFailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        _mockFailoverEndpointManager = new Mock<IFailoverEndpointManager>();
    }

    #region Tests

    [Fact]
    public void FailoverManager_Initialize_Test()
    {
        var supportedFailoverModes = FailoverMode.Hot;

        var endpointManagerInitialized = false;
        _mockFailoverEndpointManager.Setup(x => x.Initialize(It.IsAny<Action<FailoverRole, FailoverRole>>(), It.IsAny<Func<float>>(),
            It.IsAny<Action<DeviceStatus>>())).Callback(() => { endpointManagerInitialized = true; });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);

        Assert.True(endpointManagerInitialized);
    }

    [Fact]
    public async Task FailoverManager_StartStop_InitialConfigurationError_Test()
    {
        var supportedFailoverModes = FailoverMode.Hot;
        List<string> logMessages = new();

        _mockLogger.Setup(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        ClientFailoverConfiguration configuration;
        ICollection<string> getConfigurationErrors = new List<string>() { "Test" };
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out configuration, out getConfigurationErrors)).Returns(false);

        var state = new FailoverState();
        ICollection<string> getStateErrors = null;
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.FailoverStateFacetName,
            out state, out getStateErrors)).Returns(true);

        var modeUpdated = false;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateMode(It.IsAny<FailoverMode>()))
            .Callback((FailoverMode mode) =>
            {
                modeUpdated = false;
            });

        ClientFailoverConfiguration updatedconfiguration = null;
        _mockFailoverEndpointManager.Setup(x => x.UpdateConfigurationAsync(It.IsAny<ConfigurationChangedEventArgs>(), It.IsAny<CancellationToken>()))
            .Callback((ConfigurationChangedEventArgs configurationChangedEvent, CancellationToken ct) =>
            {
                updatedconfiguration = configurationChangedEvent.NewValue as ClientFailoverConfiguration;
            });

        var failoverEndpointManagerStarted = false;
        var failoverEndpointManagerStopped = false;
        _mockFailoverEndpointManager.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                failoverEndpointManagerStarted = true;
            });
        _mockFailoverEndpointManager.Setup(x => x.StopAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                failoverEndpointManagerStopped = true;
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        Assert.Single(logMessages);
        Assert.False(modeUpdated);
        Assert.Null(updatedconfiguration);
        Assert.False(failoverEndpointManagerStarted);
        Assert.False(failoverEndpointManagerStopped);

        await failoverManager.StopAsync(CancellationToken.None);

        Assert.True(failoverEndpointManagerStopped);
    }

    [Theory]
    [InlineData(false, FailoverMode.Hot)]
    [InlineData(true, FailoverMode.Warm)]
    public async Task FailoverManager_StartStop_InitialConfigurationLoaded_Test(bool configurationAvailable, FailoverMode expectedFailoverMode)
    {
        var supportedFailoverModes = FailoverMode.Hot;
        List<string> logMessages = new();
        _mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var initialFailoverGroupId = "TestGroupId";
        var initialFailoverMode = FailoverMode.Warm;
        var configuration = new ClientFailoverConfiguration()
        {
            FailoverGroupId = initialFailoverGroupId,
            Mode = initialFailoverMode,
        };

        ICollection<string> getErrors = null;
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out configuration, out getErrors))
            .Returns(configurationAvailable);

        var updatedMode = FailoverMode.Hot;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateMode(It.IsAny<FailoverMode>()))
            .Callback((FailoverMode mode) =>
            {
                updatedMode = mode;
            });

        ClientFailoverConfiguration updatedconfiguration = null;
        _mockFailoverEndpointManager.Setup(x => x.UpdateConfigurationAsync(It.IsAny<ConfigurationChangedEventArgs>(), It.IsAny<CancellationToken>()))
            .Callback((ConfigurationChangedEventArgs configurationChangedEvent, CancellationToken ct) =>
            {
                updatedconfiguration = configurationChangedEvent.NewValue as ClientFailoverConfiguration;
            });

        var failoverEndpointManagerStarted = false;
        var failoverEndpointManagerStopped = false;
        _mockFailoverEndpointManager.Setup(x => x.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                failoverEndpointManagerStarted = true;
            });
        _mockFailoverEndpointManager.Setup(x => x.StopAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                failoverEndpointManagerStopped = true;
            });

        var healthTypesSent = false;
        _mockHealthProcessor.Setup(mh => mh.WriteHealthTypes(It.IsAny<DataType[]>(), It.IsAny<MessageAction>()))
            .Callback(() =>
            {
                healthTypesSent = true;
            });

        var healthStreamsSent = false;
        _mockHealthProcessor.Setup(mh => mh.WriteHealthStreams(It.IsAny<DataStream[]>(), MessageAction.Default))
            .Callback(() =>
            {
                healthStreamsSent = true;
            });

        var healthDataSent = false;
        _mockHealthProcessor.Setup(mh => mh.WriteHealthValue(It.IsAny<string>(), It.IsAny<Classification>(),
            It.IsAny<object>(), It.IsAny<MessageAction>()))
            .Callback(() =>
            {
                healthDataSent = true;
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        Assert.Equal(configurationAvailable, updatedconfiguration != null);
        Assert.Equal(expectedFailoverMode, updatedMode);
        Assert.Equal(configurationAvailable, failoverEndpointManagerStarted);
        Assert.False(failoverEndpointManagerStopped);

        if (updatedconfiguration != null)
        {
            Assert.Equal(initialFailoverGroupId, updatedconfiguration.FailoverGroupId);
        }

        Assert.Equal(configurationAvailable, healthTypesSent);
        Assert.Equal(configurationAvailable, healthStreamsSent);
        Assert.Equal(configurationAvailable, healthDataSent);

        await failoverManager.StopAsync(CancellationToken.None);

        Assert.True(failoverEndpointManagerStopped);
    }

    [Fact]
    public async Task FailoverManager_Health_Behavior_Test()
    {
        var supportedFailoverModes = FailoverMode.Hot;
        var initialFailoverGroupId = "TestGroupId";
        var initialFailoverMode = FailoverMode.Warm;
        var configuration = new ClientFailoverConfiguration()
        {
            FailoverGroupId = initialFailoverGroupId,
            Mode = initialFailoverMode,
        };

        var healthTypesSent = false;
        _mockHealthProcessor.Setup(mh => mh.WriteHealthTypes(It.IsAny<DataType[]>(), It.IsAny<MessageAction>()))
            .Callback(() =>
            {
                healthTypesSent = true;
            });

        var healthStreamsSent = false;
        _mockHealthProcessor.Setup(mh => mh.WriteHealthStreams(It.IsAny<DataStream[]>(), MessageAction.Default))
            .Callback(() =>
            {
                healthStreamsSent = true;
            });

        var healthDataSent = false;
        _mockHealthProcessor.Setup(mh => mh.WriteHealthValue(It.IsAny<string>(), It.IsAny<Classification>(),
            It.IsAny<object>(), It.IsAny<MessageAction>()))
            .Callback(() =>
            {
                healthDataSent = true;
            });

        ICollection<string> getErrors = null;
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out configuration, out getErrors))
            .Returns(false);

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        Assert.False(healthTypesSent);
        Assert.False(healthStreamsSent);
        Assert.False(healthDataSent);

        failoverManager.UpdateFailoverConfiguration(new ConfigurationChangedEventArgs(null, configuration));

        Assert.True(SpinWait.SpinUntil(() => healthDataSent, Timeout));
        Assert.True(SpinWait.SpinUntil(() => healthStreamsSent, Timeout));
        Assert.True(healthTypesSent);
    }

    [Fact]
    public async Task FailoverManager_StartStop_InitialStateError_Test()
    {
        var supportedFailoverModes = FailoverMode.Hot;
        List<string> logMessages = new();

        _mockLogger.Setup(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var state = new FailoverState();
        var configuration = new ClientFailoverConfiguration()
        {
            FailoverGroupId = "TestGroup",
            Mode = FailoverMode.Hot,
        };

        ICollection<string> getStateErrors = new List<string>() { "Test" };
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.FailoverStateFacetName,
            out state, out getStateErrors)).Returns(false);

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out configuration, out getStateErrors)).Returns(true);

        var updatedRole = FailoverRole.Primary;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateState(It.IsAny<FailoverRole>(), It.IsAny<DateTime>()))
            .Callback((FailoverRole role, DateTime _) =>
            {
                updatedRole = role;
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        Assert.Single(logMessages);
        Assert.Equal(FailoverRole.Secondary, updatedRole);
    }

    [Theory]
    [InlineData(FailoverRole.Secondary, false)]
    [InlineData(FailoverRole.Primary, true)]
    public async Task FailoverManager_StartStop_NoInitialState_Test(FailoverRole role, bool stateExist)
    {
        var supportedFailoverModes = FailoverMode.Hot;
        List<string> logMessages = new();
        _mockLogger.Setup(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var state = new FailoverState()
        {
            AdapterState = AdapterState.Shutdown,
            Role = FailoverRole.Primary,
        };

        var configuration = new ClientFailoverConfiguration()
        {
            FailoverGroupId = "TestGroup",
            Mode = FailoverMode.Hot,
        };

        ICollection<string> getStateErrors = null;
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.FailoverStateFacetName,
            out state, out getStateErrors)).Returns(stateExist);

        var updatedRole = FailoverRole.Primary;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateState(It.IsAny<FailoverRole>(), It.IsAny<DateTime>()))
            .Callback((FailoverRole role, DateTime _) =>
            {
                updatedRole = role;
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out configuration, out getStateErrors)).Returns(true);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        Assert.Empty(logMessages);
        Assert.Equal(role, updatedRole);
    }

    [Fact]
    public void FailoverManager_UpdateConfiguration_ConfigurationNull_Test()
    {
        var supportedFailoverModes = FailoverMode.Hot;

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);

        Assert.Throws<ArgumentNullException>(() => failoverManager.UpdateFailoverConfiguration(null));
    }

    [Fact]
    public async Task FailoverManager_UpdateConfiguration_ConfigurationEmpty_Test()
    {
        bool linksResent = false;
        var existingConfiguration = new ClientFailoverConfiguration()
        {
            FailoverGroupId = "TestGroup",
            Mode = FailoverMode.Hot,
            Endpoint = "http://localhost:9999",
        };

        var supportedFailoverModes = FailoverMode.Hot;
        var messageProcessorMode = supportedFailoverModes;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateMode(It.IsAny<FailoverMode>()))
            .Callback((FailoverMode mode) =>
            {
                messageProcessorMode = mode;
            });

        ClientFailoverConfiguration updatedconfiguration = null;
        _mockFailoverEndpointManager.Setup(x => x.UpdateConfigurationAsync(It.IsAny<ConfigurationChangedEventArgs>(), It.IsAny<CancellationToken>()))
            .Callback((ConfigurationChangedEventArgs configurationChangedEvent, CancellationToken ct) =>
            {
                updatedconfiguration = configurationChangedEvent.NewValue as ClientFailoverConfiguration;
            });

        ICollection<string> errors = null;
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out existingConfiguration, out errors)).Returns(true);

        _mockHealthProcessor.Setup(x => x.WriteHealthValue(Tokens.Link, Classification.Static, It.IsAny<It.IsAnyType>(), It.IsAny<MessageAction>()))
            .Callback(() =>
            {
                linksResent = true;
            });

        Dictionary<string, object> failoverComponentAsset = null;
        _mockHealthProcessor.Setup(x => x.WriteHealthValue(FailoverHealthType, Classification.Static, It.IsAny<It.IsAnyType>(), It.IsAny<MessageAction>()))
            .Callback((string assetId, Classification classification, object instance, MessageAction messageAction) =>
            {
                failoverComponentAsset = (Dictionary<string, object>)instance;
            });

        FailoverStatusEvent failoverStatus = null;
        _mockDiagnosticsProcessor.Setup(x => x.WriteDiagnosticsValue(It.IsAny<string>(), Classification.Dynamic, It.IsAny<FailoverStatusEvent>()))
            .Callback((string id, Classification classification, FailoverStatusEvent failoverStatusEvent) =>
            {
                failoverStatus = failoverStatusEvent;
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        linksResent = false;
        failoverManager.UpdateFailoverConfiguration(new ConfigurationChangedEventArgs(existingConfiguration, null));

        await Task.Delay(500);

        Assert.False(linksResent);
        Assert.Equal(FailoverMode.NotConfigured, messageProcessorMode);
        Assert.Null(updatedconfiguration);

        // validating asset
        Assert.Equal(string.Empty, failoverComponentAsset[GroupIdProperty]);
        Assert.Equal(string.Empty, failoverComponentAsset[FailoverModeProperty]);
        Assert.Equal(string.Empty, failoverComponentAsset[FailoverEndpointProperty]);

        // validating stream
        Assert.Equal(0, failoverStatus.FailoverScore);
        Assert.Equal(string.Empty, failoverStatus.FailoverRole);
    }

    [Fact]
    public async Task FailoverManager_UpdateConfiguration_ConfigurationNotEmpty_Test()
    {
        bool linksResent = false;

        var supportedFailoverModes = FailoverMode.Hot;
        var updatedMode = FailoverMode.Hot;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateMode(It.IsAny<FailoverMode>()))
            .Callback((FailoverMode mode) =>
            {
                updatedMode = mode;
            });

        ClientFailoverConfiguration updatedconfiguration = null;
        _mockFailoverEndpointManager.Setup(x => x.UpdateConfigurationAsync(It.IsAny<ConfigurationChangedEventArgs>(), It.IsAny<CancellationToken>()))
            .Callback((ConfigurationChangedEventArgs configurationChangedEvent, CancellationToken ct) =>
            {
                updatedconfiguration = configurationChangedEvent.NewValue as ClientFailoverConfiguration;
            });

        _mockHealthProcessor.Setup(x => x.WriteHealthValue(Tokens.Link, Classification.Static, It.IsAny<It.IsAnyType>(), It.IsAny<MessageAction>())).Callback(() => linksResent = true);

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        var failoverGroupIdToUpdate = "TestGroupId";
        var failoverModeToUpdate = FailoverMode.Warm;
        var configurationChangeEventArgs = new ConfigurationChangedEventArgs(null,
            new ClientFailoverConfiguration()
            {
                FailoverGroupId = failoverGroupIdToUpdate,
                Mode = failoverModeToUpdate,
            });

        linksResent = false;
        failoverManager.UpdateFailoverConfiguration(configurationChangeEventArgs);
        Assert.True(SpinWait.SpinUntil(() => updatedMode == failoverModeToUpdate, Timeout));

        Assert.NotNull(updatedconfiguration);
        Assert.Equal(failoverGroupIdToUpdate, updatedconfiguration.FailoverGroupId);
        Assert.True(linksResent);
    }

    [Fact]
    public async Task FailoverManager_UpdateConfiguration_ConfigurationNotEmpty_ModeChangeCallback_Test()
    {
        bool linksResent = false;

        var supportedFailoverModes = FailoverMode.Hot;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateMode(It.IsAny<FailoverMode>()))
            .Callback((FailoverMode mode) =>
            {
                _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverMode).Returns(mode);
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        _mockHealthProcessor.Setup(x => x.WriteHealthValue(Tokens.Link, Classification.Static, It.IsAny<It.IsAnyType>(), It.IsAny<MessageAction>())).Callback(() => linksResent = true);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        var modeChangeCallbackId = "TestCallback";
        var callbackedOldMode = FailoverMode.Hot;
        var callbackedNewMode = FailoverMode.Hot;
        failoverManager.RegisterFailoverModeChangeCallback(modeChangeCallbackId, async (oldMode, newMode) =>
        {
            callbackedOldMode = oldMode;
            callbackedNewMode = newMode;
            await Task.CompletedTask;
        });

        var failoverGroupIdToUpdate = "TestGroupId";
        var failoverModeToUpdate = FailoverMode.Warm;
        var configurationChangeEventArgs = new ConfigurationChangedEventArgs(null,
            new ClientFailoverConfiguration()
            {
                FailoverGroupId = failoverGroupIdToUpdate,
                Mode = failoverModeToUpdate,
            });

        linksResent = false;
        failoverManager.UpdateFailoverConfiguration(configurationChangeEventArgs);
        Assert.True(SpinWait.SpinUntil(() => callbackedOldMode == FailoverMode.NotConfigured, Timeout));
        Assert.Equal(failoverModeToUpdate, callbackedNewMode);
        Assert.True(linksResent);
    }

    [Fact]
    public async Task FailoverManager_UpdateConfiguration_Cancelled_Test()
    {
        ClientFailoverConfiguration updatedconfiguration = null;
        bool updateCancelled = false;

        var supportedFailoverModes = FailoverMode.Hot | FailoverMode.Warm;
        var updatedMode = FailoverMode.Hot;
        var failoverGroupIdToUpdate = "TestGroupId";

        _mockFailoverEndpointManager.Setup(x => x.UpdateConfigurationAsync(It.IsAny<ConfigurationChangedEventArgs>(), It.IsAny<CancellationToken>()))
            .Callback((ConfigurationChangedEventArgs configurationChangedEvent, CancellationToken ct) =>
            {
                try
                {
                    Task.Delay(1000, ct).GetAwaiter().GetResult();
                    updatedconfiguration = configurationChangedEvent.NewValue as ClientFailoverConfiguration;
                }
                catch (OperationCanceledException)
                {
                    updateCancelled = true;
                }
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        var configurationChangeEventArgs = new ConfigurationChangedEventArgs(null,
            new ClientFailoverConfiguration()
            {
                FailoverGroupId = failoverGroupIdToUpdate,
                Mode = FailoverMode.Warm,
            });
        var configurationChangeEventArgs2 = new ConfigurationChangedEventArgs(null,
            new ClientFailoverConfiguration()
            {
                FailoverGroupId = failoverGroupIdToUpdate,
                Mode = updatedMode,
            });

        failoverManager.UpdateFailoverConfiguration(configurationChangeEventArgs);

        // kick off the second update which will cancel the first
        failoverManager.UpdateFailoverConfiguration(configurationChangeEventArgs2);

        Assert.True(SpinWait.SpinUntil(() => updatedconfiguration != null, Timeout));
        Assert.True(updateCancelled);
        Assert.Equal(failoverGroupIdToUpdate, updatedconfiguration.FailoverGroupId);
        Assert.Equal(updatedMode, updatedconfiguration.Mode);
    }

    [Fact]
    public async Task FailoverManager_UpdateConfiguration_Cancelled_InStop_Test()
    {
        ClientFailoverConfiguration updatedconfiguration = null;
        bool updateCancelled = false;

        var supportedFailoverModes = FailoverMode.Warm;
        var failoverGroupIdToUpdate = "TestGroupId";

        _mockFailoverEndpointManager.Setup(x => x.UpdateConfigurationAsync(It.IsAny<ConfigurationChangedEventArgs>(), It.IsAny<CancellationToken>()))
            .Callback((ConfigurationChangedEventArgs configurationChangedEvent, CancellationToken ct) =>
            {
                try
                {
                    Task.Delay(1000, ct).GetAwaiter().GetResult();
                    updatedconfiguration = configurationChangedEvent.NewValue as ClientFailoverConfiguration;
                }
                catch (OperationCanceledException)
                {
                    updateCancelled = true;
                }
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        var configurationChangeEventArgs = new ConfigurationChangedEventArgs(null,
            new ClientFailoverConfiguration()
            {
                FailoverGroupId = failoverGroupIdToUpdate,
                Mode = FailoverMode.Warm,
            });

        failoverManager.UpdateFailoverConfiguration(configurationChangeEventArgs);

        await failoverManager.StopAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => updateCancelled, Timeout));
        Assert.Null(updatedconfiguration);
    }

    [Fact]
    public void FailoverManager_ValidateFailoverConfiguration_NullConfiguration_Test()
    {
        var supportedFailoverModes = FailoverMode.Hot;

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);

        var errors = failoverManager.ValidateFailoverConfiguration(null);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void FailoverManager_ValidateFailoverConfiguration_EmptyConfiguration_Test()
    {
        var supportedFailoverModes = FailoverMode.Hot;

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);

        var configurationChangeEventArgs = new ConfigurationChangedEventArgs(null, null);
        var errors = failoverManager.ValidateFailoverConfiguration(configurationChangeEventArgs);

        Assert.Empty(errors);
    }

    [Fact]
    public void FailoverManager_ValidateFailoverConfiguration_UnsupportedFailoverModes_Test()
    {
        var supportedFailoverModes = FailoverMode.Cold | FailoverMode.Hot;

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);

        var configurationChangeEventArgs = new ConfigurationChangedEventArgs(null,
            new ClientFailoverConfiguration()
            {
                Mode = FailoverMode.Warm,
            });

        var errors = failoverManager.ValidateFailoverConfiguration(configurationChangeEventArgs);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void FailoverManager_GetCurrentFailoverState_Uninitialized_Test()
    {
        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        var currentFailoverState = failoverManager.GetCurrentFailoverState();

        Assert.Null(currentFailoverState);
    }

    [Fact]
    public void FailoverManager_GetCurrentFailoverState_Initialized_Test()
    {
        var supportedFailoverModes = FailoverMode.Cold | FailoverMode.Hot;
        var expectedFailoverRole = FailoverRole.Primary;
        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverRole).Returns(expectedFailoverRole);
        var expectedLastDataProcessTime = DateTime.UtcNow;
        _mockFailoverDataMessageProcessor.Setup(x => x.LastDataProcessedTime).Returns(expectedLastDataProcessTime);

        var mockFailoverEndpointManager = new Mock<IFailoverEndpointManager>();

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(supportedFailoverModes);

        var currentFailoverState = failoverManager.GetCurrentFailoverState();

        Assert.NotNull(currentFailoverState);
        Assert.Equal(expectedFailoverRole, currentFailoverState.Role);
        Assert.Equal(expectedLastDataProcessTime, currentFailoverState.LastDataProcessedTime);
    }

    [Fact]
    public async Task FailoverManager_StartColdSecondaryZeroToNonZero_Test()
    {
        var timeout = TimeSpan.FromSeconds(1);

        var supportedFailoverModes = FailoverMode.Cold;
        List<string> logMessages = new();
        _mockLogger.Setup(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var clientFailoverConfig = new ClientFailoverConfiguration { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "A", FailoverTimeout = timeout };
        ICollection<string> getStateErrors = null;

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out clientFailoverConfig, out getStateErrors)).Returns(true);

        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverRole).Returns(FailoverRole.Secondary);
        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverMode).Returns(FailoverMode.Cold);

        using var failoverEndpointManager = new TestFailoverEndpointManager();

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, failoverEndpointManager);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        var healthServiceMock = new Mock<IFailoverService>();
        failoverManager.AddComponentHealthService("a", healthServiceMock.Object);

        Assert.Equal(0, failoverEndpointManager.FailoverStatusFunc());
        await Task.Delay(100);
        await Task.Delay(timeout * FailoverManager.StartupToGoodTimeMultiplier);
        Assert.Equal(1, failoverEndpointManager.FailoverStatusFunc());
    }

    [Fact]
    public async Task FailoverManager_ColdPrimaryFailureTransitionToSecondary_Test()
    {
        var timeout = TimeSpan.FromSeconds(1);

        var supportedFailoverModes = FailoverMode.Cold;
        List<string> logMessages = new();
        _mockLogger.Setup(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var clientFailoverConfig = new ClientFailoverConfiguration { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "A", FailoverTimeout = timeout };
        ICollection<string> getStateErrors = null;

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out clientFailoverConfig, out getStateErrors)).Returns(true);

        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverRole).Returns(FailoverRole.Primary);
        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverMode).Returns(FailoverMode.Cold);

        using var failoverEndpointManager = new TestFailoverEndpointManager();

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, failoverEndpointManager);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        var healthServiceMock = new Mock<IFailoverService>();
        healthServiceMock.Setup(x => x.GetDataSelectionItemCount()).Returns(1);
        healthServiceMock.Setup(x => x.GetFailoverScore()).Returns(100);

        failoverManager.AddComponentHealthService("a", healthServiceMock.Object);

        Assert.Equal(100f, failoverEndpointManager.FailoverStatusFunc());
        healthServiceMock.Setup(x => x.GetDeviceStatus()).Returns(DeviceStatus.DeviceInError);
        failoverEndpointManager.FailoverStatusFunc();

        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverRole).Returns(FailoverRole.Secondary);

        Assert.Equal(0, failoverEndpointManager.FailoverStatusFunc());
        await Task.Delay(100);
        await Task.Delay(timeout * FailoverManager.BadToGoodTimeMultiplier);
        Assert.Equal(1, failoverEndpointManager.FailoverStatusFunc());
    }

    [Fact]
    public async Task FailoverManager_StartAsync_RoleUpdate_Test()
    {
        var expectedRole = FailoverRole.Primary;
        var state = new FailoverState();
        var configuration = new ClientFailoverConfiguration()
        {
            FailoverGroupId = "TestGroup",
            Mode = FailoverMode.Hot,
        };

        state.AdapterState = AdapterState.Shutdown;
        state.Role = expectedRole;
        ICollection<string> getStateErrors = null;
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.FailoverStateFacetName,
            out state, out getStateErrors)).Returns(true);

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out configuration, out getStateErrors)).Returns(true);

        var updatedDataMessageProcessorRole = FailoverRole.Secondary;
        _mockFailoverDataMessageProcessor.Setup(x => x.UpdateState(It.IsAny<FailoverRole>(), It.IsAny<DateTime>()))
            .Callback((FailoverRole role, DateTime lastDataProcessedTime) =>
            {
                updatedDataMessageProcessorRole = role;
            });
        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverRole).Returns(expectedRole);

        var diagnosticsFailoverRole = string.Empty;
        _mockDiagnosticsProcessor.Setup(x => x.WriteDiagnosticsValue(It.IsAny<string>(), Classification.Dynamic, It.IsAny<FailoverStatusEvent>()))
            .Callback((string id, Classification classification, FailoverStatusEvent failoverStatus) =>
            {
                diagnosticsFailoverRole = failoverStatus.FailoverRole;
            });

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, _mockFailoverEndpointManager.Object);

        failoverManager.Initialize(FailoverMode.Hot);
        await failoverManager.StartAsync(CancellationToken.None);

        // check that both data message processor and failover diagnostics have role of Primary
        Assert.Equal(expectedRole.ToString(), diagnosticsFailoverRole);
        Assert.Equal(expectedRole, updatedDataMessageProcessorRole);
    }

    [Fact]
    public async Task FailoverManager_SendsValueBetweenZeroAndOneHundred_Test()
    {
        var timeout = TimeSpan.FromSeconds(1);

        var supportedFailoverModes = FailoverMode.Cold;
        List<string> logMessages = new();
        _mockLogger.Setup(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var clientFailoverConfig = new ClientFailoverConfiguration { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "A", FailoverTimeout = timeout };
        ICollection<string> getStateErrors = null;

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out clientFailoverConfig, out getStateErrors)).Returns(true);

        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverRole).Returns(FailoverRole.Primary);
        _mockFailoverDataMessageProcessor.Setup(x => x.CurrentFailoverMode).Returns(FailoverMode.Cold);

        using var failoverEndpointManager = new TestFailoverEndpointManager();

        using var failoverManager = new FailoverManager(_mockLogger.Object, _mockApplicationManifest.Object, _mockConfigurationProvider.Object,
            _mockDiagnosticsProcessor.Object, _mockHealthProcessor.Object, _mockFailoverDataMessageProcessor.Object, failoverEndpointManager);

        failoverManager.Initialize(supportedFailoverModes);
        await failoverManager.StartAsync(CancellationToken.None);

        var healthServiceMock = new Mock<IFailoverService>();
        healthServiceMock.Setup(x => x.GetDataSelectionItemCount()).Returns(1);

        failoverManager.AddComponentHealthService("a", healthServiceMock.Object);

        healthServiceMock.Setup(x => x.GetFailoverScore()).Returns(200);
        Assert.Equal(100f, failoverEndpointManager.FailoverStatusFunc());
        healthServiceMock.Setup(x => x.GetFailoverScore()).Returns(-100);
        Assert.Equal(0f, failoverEndpointManager.FailoverStatusFunc());
        healthServiceMock.Setup(x => x.GetFailoverScore()).Returns(50);
        Assert.Equal(50f, failoverEndpointManager.FailoverStatusFunc());
    }

    #endregion

    #region Private methods

    private Mock<IApplicationManifest> GetSetupMockApplicationManifest()
    {
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        mockApplicationManifest.Setup(manifest => manifest.MachineName).Returns(MachineName);
        mockApplicationManifest.Setup(manifest => manifest.ServiceName).Returns(ServiceName);
        mockApplicationManifest.Setup(manifest => manifest.ProductVersion).Returns(_productVersion);

        return mockApplicationManifest;
    }

    #endregion
}
