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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Failover.Configuration;
using AdapterFramework.Data.Framework.Failover.Entities;
using AdapterFramework.Data.Framework.Failover.Interfaces;
using AdapterFramework.Data.Framework.Serialization;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace AdapterFramework.Data.Framework.Failover.Tests;

public class FailoverEndpointManager_Tests
{
    private const int StartAsyncTimeout = 2000;

    private readonly Mock<IFailoverEndpointClient> _endpointClientMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<IFailoverDataMessageProcessor> _dataMessageProcessorMock;
    private readonly Mock<IEdgeDataProtector> _dataProtectorMock;
    private readonly Mock<IApplicationManifest> _applicationManifestMock;
    private readonly Mock<ISerializer> _serializerMock;
    private readonly List<string> _urisSent;
    private readonly List<byte[]> _bodySent;

    public FailoverEndpointManager_Tests()
    {
        _urisSent = new List<string>();
        _bodySent = new List<byte[]>();

        _endpointClientMock = new Mock<IFailoverEndpointClient>();
        _endpointClientMock.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>(), It.IsAny<HttpVerb>()))
            .Callback((string requestUri, byte[] msgBody, CancellationToken cancellationToken, HttpVerb verb) =>
            {
                _urisSent.Add(requestUri);
                _bodySent.Add(msgBody);
            });

        _loggerMock = new Mock<ILogger>();
        _dataProtectorMock = new Mock<IEdgeDataProtector>();
        _applicationManifestMock = new Mock<IApplicationManifest>();
        _dataMessageProcessorMock = new Mock<IFailoverDataMessageProcessor>();
        _serializerMock = new Mock<ISerializer>();
    }

    [Fact]
    public async Task FailoverEndpointManager_StartAsync_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        await endpointManager.StartAsync(CancellationToken.None);

        // potential heartbeat sent
        Assert.True(_urisSent.Count == 2 || _urisSent.Count == 3);
        Assert.True(_urisSent.Select(x => x.Contains("group", StringComparison.OrdinalIgnoreCase)).Any());
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_StartAsync_NoConfigurationTest()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.Empty(_urisSent);
    }

    [Fact]
    public async Task FailoverEndpointManager_StopAsync_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        await endpointManager.StopAsync(CancellationToken.None);
        Assert.Single(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfigurationWhileRunning_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object,
            _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);

        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });

        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var newConfig = new ClientFailoverConfiguration()
        { 
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, newConfig), CancellationToken.None);

        Assert.Equal(4, _urisSent.Count);
        Assert.All(_urisSent, uri => uri.Contains("group", StringComparison.OrdinalIgnoreCase));
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfigurationStopped_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });

        var newConfig = new ClientFailoverConfiguration() { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "aaa", FailoverTimeout = TimeSpan.FromSeconds(20) };
        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, newConfig), CancellationToken.None);

        Assert.Empty(_urisSent);
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfigurationToNull_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });

        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var newConfig = new ClientFailoverConfiguration() { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "aaa", FailoverTimeout = TimeSpan.FromSeconds(20), Mode = FailoverMode.Hot };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, newConfig), CancellationToken.None);
        _urisSent.Clear();

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(newConfig, null), CancellationToken.None);

        Assert.Single(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfigurationToEmpty_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => { return 0f; }, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var newConfig = new ClientFailoverConfiguration() { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "aaa", FailoverTimeout = TimeSpan.FromSeconds(20), Mode = FailoverMode.Hot };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, newConfig), CancellationToken.None);
        _urisSent.Clear();

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(newConfig, null), CancellationToken.None);

        Assert.Single(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfigurationToModeNone_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => { return 0f; }, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var oldConfig = new ClientFailoverConfiguration() { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "aaa", FailoverTimeout = TimeSpan.FromSeconds(20), Mode = FailoverMode.Hot };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, oldConfig), CancellationToken.None);
        _urisSent.Clear();

        var newConfig = new ClientFailoverConfiguration() { ClientId = "a", ClientSecret = "b", Endpoint = "http://localhost:9999", FailoverGroupId = "aaa", FailoverTimeout = TimeSpan.FromSeconds(20), Mode = FailoverMode.NotConfigured };
        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldConfig, newConfig), CancellationToken.None);

        Assert.Single(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfiguration_DifferentEndpoint_Restarted_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => { return 0f; }, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var oldConfig = new ClientFailoverConfiguration() 
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, oldConfig), CancellationToken.None);
        _urisSent.Clear();

        var newConfig = new ClientFailoverConfiguration() 
        { 
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9998",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldConfig, newConfig), CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 0, StartAsyncTimeout));
        Assert.NotEmpty(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("group", StringComparison.OrdinalIgnoreCase)).Any());
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
        Assert.True(_urisSent.Select(x => x.Contains("heartbeat", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfiguration_DifferentValidateEndpointCertificate_Restarted_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => { return 0f; }, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var oldConfig = new ClientFailoverConfiguration()
        { 
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            ValidateEndpointCertificate = true,
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, oldConfig), CancellationToken.None);
        _urisSent.Clear();

        var newConfig = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            ValidateEndpointCertificate = false,
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldConfig, newConfig), CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 0, StartAsyncTimeout));
        Assert.NotEmpty(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("group", StringComparison.OrdinalIgnoreCase)).Any());
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
        Assert.True(_urisSent.Select(x => x.Contains("heartbeat", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfiguration_DifferentFailoverGroupId_ReRegistered_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => { return 0f; }, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var oldConfig = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            ValidateEndpointCertificate = true,
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, oldConfig), CancellationToken.None);
        _urisSent.Clear();

        var newConfig = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "bbb",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            ValidateEndpointCertificate = true,
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldConfig, newConfig), CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 0, StartAsyncTimeout));
        Assert.NotEmpty(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("group", StringComparison.OrdinalIgnoreCase)).Any());
        Assert.True(_urisSent.Select(x => x.Contains("session", StringComparison.OrdinalIgnoreCase)).Any());
        Assert.True(_urisSent.Select(x => x.Contains("heartbeat", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfiguration_DifferentFailoverTimeout_ResendHeartbeat_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => { return 0f; }, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var oldConfig = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            ValidateEndpointCertificate = true,
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, oldConfig), CancellationToken.None);
        _urisSent.Clear();
        _bodySent.Clear();

        var newConfig = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(25),
            ValidateEndpointCertificate = true,
            Mode = FailoverMode.Hot,
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldConfig, newConfig), CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 0, StartAsyncTimeout));

        Assert.Single(_urisSent);
        Assert.True(_urisSent.Select(x => x.Contains("heartbeat", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfiguration_TrivialUpdate_NoMessageOut_Test()
    {
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);
        await endpointManager.UpdateConfigurationAsync(GetClientFailoverConfiguration(), CancellationToken.None);
        endpointManager.Initialize((x, y) => { }, () => { return 0f; }, (x) => { });
        await endpointManager.StartAsync(CancellationToken.None);

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 2, StartAsyncTimeout));
        _urisSent.Clear();
        _bodySent.Clear();

        var oldConfig = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            ValidateEndpointCertificate = true,
            Description = "oldConfig",
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, oldConfig), CancellationToken.None);
        _urisSent.Clear();
        _bodySent.Clear();

        var newConfig = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "aaa",
            FailoverTimeout = TimeSpan.FromSeconds(20),
            ValidateEndpointCertificate = true,
            Description = "newConfig",
        };

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldConfig, newConfig), CancellationToken.None);

        Assert.Empty(_urisSent);
    }

    [Fact]
    public async Task FailoverEndpointManager_SendHeartbeat_Test()
    {
        _endpointClientMock.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>(), It.IsAny<HttpVerb>()))
            .Callback((string requestUri, byte[] msgBody, CancellationToken cancellationToken, HttpVerb verb) =>
            {
                _urisSent.Add(requestUri);
                _bodySent.Add(msgBody);
            })
            .ReturnsAsync(new EndpointResponse(ResponseStatusEnum.Success, JsonSerializer.Serialize(new FailoverMessage() { LastDataProcessedTime = DateTime.UtcNow, Role = FailoverRole.Primary })));
        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, new OmfJsonSerializer(), _endpointClientMock.Object);
        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });
        var clientFailoverConfiguration = GetClientFailoverConfiguration().NewValue as ClientFailoverConfiguration;
        clientFailoverConfiguration.FailoverTimeout = TimeSpan.FromSeconds(15);
        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(null, clientFailoverConfiguration), CancellationToken.None);
        await endpointManager.StartAsync(CancellationToken.None);
        _urisSent.Clear();
        _bodySent.Clear();

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 0, StartAsyncTimeout));
        Assert.True(_urisSent.Select(x => x.Contains("heartbeat", StringComparison.OrdinalIgnoreCase)).Any());
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public async Task FailoverEndpointManager_LastDataProcessedTime_Adjusted_Test(int failoverTimeoutSeconds)
    {
        var failoverTimeout = TimeSpan.FromSeconds(failoverTimeoutSeconds);

        var lastDataProcessedTime = DateTime.UtcNow;
        var expectedLastDataProcessedTime = lastDataProcessedTime.Subtract(failoverTimeout / 2);
        var expectedRole = FailoverRole.Primary;

        _endpointClientMock.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>(), It.IsAny<HttpVerb>()))
            .Callback((string requestUri, byte[] msgBody, CancellationToken cancellationToken, HttpVerb verb) =>
            {
                _urisSent.Add(requestUri);
                _bodySent.Add(msgBody);
            }).ReturnsAsync(new EndpointResponse(ResponseStatusEnum.Success, JsonSerializer.Serialize(new FailoverMessage() { LastDataProcessedTime = lastDataProcessedTime, Role = expectedRole })));

        var receivedLastDataProcessedTime = DateTime.MinValue;
        var receivedFailoverRole = FailoverRole.PendingPrimary;

        _dataMessageProcessorMock.Setup(messageProcessor => messageProcessor.UpdateState(It.IsAny<FailoverRole>(), It.IsAny<DateTime>())).Callback((FailoverRole role, DateTime time) =>
        {
            receivedFailoverRole = role;
            receivedLastDataProcessedTime = time;
        });

        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object,
            new OmfJsonSerializer(), _endpointClientMock.Object);

        endpointManager.Initialize((x, y) => { }, () => 5.5f, (x) => { });
        var clientFailoverConfiguration = GetClientFailoverConfiguration();
        ((ClientFailoverConfiguration)clientFailoverConfiguration.NewValue).FailoverTimeout = failoverTimeout;
        await endpointManager.UpdateConfigurationAsync(clientFailoverConfiguration, CancellationToken.None);
        await endpointManager.StartAsync(CancellationToken.None);
        _urisSent.Clear();
        _bodySent.Clear();

        Assert.True(SpinWait.SpinUntil(() => _bodySent.Count > 0, StartAsyncTimeout));
        Assert.Equal(expectedLastDataProcessedTime, receivedLastDataProcessedTime);
        Assert.Equal(expectedRole, receivedFailoverRole);
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfigToNull_RoleChange_Test()
    {
        var receivedFailoverRole = FailoverRole.Primary;
        _dataMessageProcessorMock.Setup(messageProcessor => messageProcessor.UpdateState(It.IsAny<FailoverRole>(), It.IsAny<DateTime>()))
            .Callback((FailoverRole role, DateTime time) =>
            {
                receivedFailoverRole = role;
            }); 
        _dataMessageProcessorMock.Setup(x => x.CurrentFailoverRole).Returns(FailoverRole.Primary);

        var oldClientFailoverConfig = GetClientFailoverConfiguration();

        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);

        await endpointManager.UpdateConfigurationAsync(oldClientFailoverConfig, CancellationToken.None);

        FailoverRole oldFailoverRole = FailoverRole.Primary;
        FailoverRole newFailoverRole = FailoverRole.Primary;
        endpointManager.Initialize((old, newRole) => 
            {
                oldFailoverRole = old;
                newFailoverRole = newRole;
            },
            () => 5.5f, (x) => { });

        await endpointManager.StartAsync(CancellationToken.None);

        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldClientFailoverConfig.NewValue, null), CancellationToken.None);

        // check that role change action was invoked with role Primary -> Secondary
        Assert.Equal(FailoverRole.Primary, oldFailoverRole);
        Assert.Equal(FailoverRole.Secondary, newFailoverRole);

        // check that UpdateState was called with role update to Secondary
        Assert.Equal(FailoverRole.Secondary, receivedFailoverRole);
    }

    [Fact]
    public async Task FailoverEndpointManager_UpdateConfigToModeNone_RoleChange_Test()
    {
        var receivedFailoverRole = FailoverRole.Primary;
        _dataMessageProcessorMock.Setup(messageProcessor => messageProcessor.UpdateState(It.IsAny<FailoverRole>(), It.IsAny<DateTime>()))
            .Callback((FailoverRole role, DateTime time) =>
            {
                receivedFailoverRole = role;
            });
        _dataMessageProcessorMock.Setup(x => x.CurrentFailoverRole).Returns(FailoverRole.Primary);

        var oldClientFailoverConfig = GetClientFailoverConfiguration();

        using var endpointManager = new FailoverEndpointManager(_loggerMock.Object, _dataProtectorMock.Object, _applicationManifestMock.Object, _dataMessageProcessorMock.Object, _serializerMock.Object, _endpointClientMock.Object);

        await endpointManager.UpdateConfigurationAsync(oldClientFailoverConfig, CancellationToken.None);

        FailoverRole oldFailoverRole = FailoverRole.Primary;
        FailoverRole newFailoverRole = FailoverRole.Primary;
        endpointManager.Initialize((old, newRole) =>
        {
            oldFailoverRole = old;
            newFailoverRole = newRole;
        },
            () => 5.5f, (x) => { });

        await endpointManager.StartAsync(CancellationToken.None);

        var newClientFailoverConfig = GetClientFailoverConfiguration();
        ((ClientFailoverConfiguration)newClientFailoverConfig.NewValue).Mode = FailoverMode.NotConfigured;
        await endpointManager.UpdateConfigurationAsync(new ConfigurationChangedEventArgs(oldClientFailoverConfig.NewValue, newClientFailoverConfig.NewValue), CancellationToken.None);

        // check that role change action was invoked with role Primary -> Secondary
        Assert.Equal(FailoverRole.Primary, oldFailoverRole);
        Assert.Equal(FailoverRole.Secondary, newFailoverRole);

        // check that UpdateState was called with role update to Secondary
        Assert.Equal(FailoverRole.Secondary, receivedFailoverRole);
    }

    private static ConfigurationChangedEventArgs GetClientFailoverConfiguration()
    {
        var configuration = new ClientFailoverConfiguration()
        {
            ClientId = "a",
            ClientSecret = "b",
            Endpoint = "http://localhost:9999",
            FailoverGroupId = "a",
            FailoverTimeout = TimeSpan.FromSeconds(100),
            Mode = FailoverMode.Cold,
        };

        return new ConfigurationChangedEventArgs(null, configuration);
    }
}
