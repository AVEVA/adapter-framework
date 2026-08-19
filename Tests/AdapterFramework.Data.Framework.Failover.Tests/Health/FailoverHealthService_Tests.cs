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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Failover.Configuration;
using AdapterFramework.Data.Framework.Failover.Health;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests.Health;

public class FailoverHealthService_Tests : IDisposable
{
    private const string MachineName = "Machine";
    private const string ServiceName = "Service";
    private const string FailoverHealthType = "FailoverHealth";
    private const int HealthServiceTimeout = 1000;

    private readonly ClientFailoverConfiguration _clientFailoverConfiguration;
    private readonly IHealthMessageProcessor _fakeHealthMessageProcessor;
    private readonly FailoverHealthService _healthService;
    private readonly List<DataType> _types = new();
    private readonly List<DataStream> _streams = new();
    private readonly List<string> _data = new();

    private bool _disposed;

    public FailoverHealthService_Tests()
    {
        var logger = new Mock<ILogger>();

        var productVersion = new Version(1, 1, 0);
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        mockApplicationManifest.Setup(manifest => manifest.MachineName).Returns(MachineName);
        mockApplicationManifest.Setup(manifest => manifest.ServiceName).Returns(ServiceName);
        mockApplicationManifest.Setup(manifest => manifest.ProductVersion).Returns(productVersion);

        _clientFailoverConfiguration = new() { FailoverGroupId = string.Empty, Mode = FailoverMode.NotConfigured, };
        _fakeHealthMessageProcessor = new FakeHealthMessageProcessor(_types, _streams, _data);
        _healthService = new FailoverHealthService(_fakeHealthMessageProcessor, logger.Object, mockApplicationManifest.Object, _clientFailoverConfiguration);
    }

    #region Tests

    [Fact]
    public async Task Initialize_Test()
    {
        await _healthService.InitializeAsync();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_streams);
        Assert.NotEmpty(_data);

        ValidateTypesStreamsData();
    }

    [Fact]
    public void NotInitialized_Test()
    {
        Assert.Throws<InvalidOperationException>(() => _healthService.SendDeviceStatus(DeviceStatus.DeviceInError));
    }

    [Fact]
    public void ResendTypesAndStreams_Test()
    {
        _healthService.ResendTypesAndStreams();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_streams);
        Assert.NotEmpty(_data);

        ValidateTypesStreamsData();
    }

    [Fact]
    public void ResendLinks_Test()
    {
        _healthService.ResendLinks();
        Assert.NotEmpty(_data);
        ValidateLinks();
    }

    [Fact]
    public async Task SendClientConfiguration_Test()
    {
        ClientFailoverConfiguration clientConfig = new();

        await _healthService.StartAsync();
        Assert.Throws<ArgumentNullException>(() => _healthService.SendClientConfiguration(null));
        Assert.Throws<ArgumentNullException>(() => _healthService.SendClientConfiguration(clientConfig));

        clientConfig.FailoverGroupId = null;
        Assert.Throws<ArgumentNullException>(() => _healthService.SendClientConfiguration(clientConfig));

        await _healthService.StopAsync();
    }

    [Fact]
    public async Task ResetFailoverAsset_Test()
    {
        ClientFailoverConfiguration clientConfig = new()
        {
            FailoverGroupId = "groupA",
            Mode = FailoverMode.Hot,
            Endpoint = "https:\\testurl",
        };

        await _healthService.StartAsync();
        Assert.Single(_data.FindAll(x => x.Contains(FailoverHealthType, StringComparison.InvariantCultureIgnoreCase)));

        _healthService.SendClientConfiguration(clientConfig);
        Assert.Equal(2, _data.FindAll(x => x.Contains(FailoverHealthType, StringComparison.InvariantCultureIgnoreCase)).Count);

        _healthService.ResetFailoverAsset();
        Assert.Equal(3, _data.FindAll(x => x.Contains(FailoverHealthType, StringComparison.InvariantCultureIgnoreCase)).Count);

        _healthService.ResetFailoverAsset();
        Assert.Equal(4, _data.FindAll(x => x.Contains(FailoverHealthType, StringComparison.InvariantCultureIgnoreCase)).Count);

        await _healthService.StopAsync();
    }

    [Fact]
    public async Task StartAsync_StartsHeartbeat_Test()
    {
        await _healthService.StartAsync();
        Assert.True(SpinWait.SpinUntil(() => _data.Count > 4, HealthServiceTimeout));
        Assert.Contains(_data, x => x.Contains(HealthOmfMessageCreatorBase.NextHealthMessageExpected, StringComparison.InvariantCulture));
    }

    [Fact]
    public async Task StartAsync_SendsDeviceStatus_Test()
    {
        await _healthService.StartAsync();
        _healthService.SendDeviceStatus(DeviceStatus.AttemptingFailover);
        _healthService.SendDeviceStatus(DeviceStatus.ConnectedNoData);
        await _healthService.StopAsync();

        var count = _data.FindAll(x => x.Contains(HealthOmfMessageCreatorBase.DeviceStatus, StringComparison.InvariantCulture)).Count;
        Assert.Equal(2, count);
    }

    #endregion

    #region Disposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        _healthService?.Dispose();
        _disposed = true;
    }

    #endregion

    #region Private methods

    private void ValidateTypesStreamsData()
    {
        Assert.Equal(2, _types.FindAll(x => x.Classification.Equals(Classification.Dynamic.ToString(), StringComparison.InvariantCultureIgnoreCase)).Count);
        Assert.Single(_types.FindAll(x => x.Classification.Equals(Classification.Static.ToString(), StringComparison.InvariantCultureIgnoreCase)));

        foreach (var stream in _streams)
        {
            var streamIdStructure = stream.Id.Split(".");
            Assert.Equal(4, streamIdStructure.Length);
            Assert.Equal(MachineName, streamIdStructure[0]);
            Assert.Equal(ServiceName, streamIdStructure[1]);
            Assert.Equal(EdgeSystemConstants.FailoverComponentType, streamIdStructure[2]);
            Assert.True((streamIdStructure[3] == HealthOmfMessageCreatorBase.DeviceStatus)
                || (streamIdStructure[3] == HealthOmfMessageCreatorBase.NextHealthMessageExpected));
            Assert.Null(stream.Metadata);
        }

        Assert.Equal(3, _data.FindAll(x => x.Contains(Tokens.Link, StringComparison.InvariantCultureIgnoreCase)).Count);
        
        Assert.Single(_data.FindAll(x => x.Contains(FailoverHealthType, StringComparison.InvariantCultureIgnoreCase)));
    }

    private void ValidateLinks()
    {
        Assert.Equal(3, _data.FindAll(x => x.Contains(Tokens.Link, StringComparison.InvariantCultureIgnoreCase)).Count);
    }

    #endregion

    #region Fake Health Message Processor

    internal class FakeHealthMessageProcessor : IHealthMessageProcessor
    {
        private readonly List<DataType> _types;
        private readonly List<DataStream> _streams;
        private readonly List<string> _data;

        public FakeHealthMessageProcessor(List<DataType> types, List<DataStream> containers, List<string> data)
        {
            _types = types;
            _streams = containers;
            _data = data;
        }

        public MetadataInfo StreamMetadataLevel { get; set; }

        public void WriteHealthStreams(DataStream[] dataStreams, MessageAction messageAction = MessageAction.Default)
        {
            _streams.AddRange(dataStreams);
        }

        public void WriteHealthValue<T>(string id, Classification classification, T instance, MessageAction messageAction = MessageAction.Default)
        {
            _data.Add(id);
        }

        public void WriteHealthTypes(DataType[] dataTypes, MessageAction messageAction = MessageAction.Default)
        {
            _types.AddRange(dataTypes);
        }
    }

    #endregion
}
