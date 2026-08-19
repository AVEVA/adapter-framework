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
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.AdapterCommon.Health;
using AdapterFramework.Data.Framework.Common.Health;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Health;

public class AdapterHealthService_Tests : IDisposable
{
    private const int ExpectedMetadataItemCount = 2;
    private const string DeviceStatus = "DeviceStatus";
    private const string UnitTestComponentId = "UnitTestComponentId";
    private const string UnitTestComponentType = "UnitTestComponentType";
    private const string DataSourceKey = "DataSource";
    private const string AdapterTypeKey = "AdapterType";
    private const string MachineName = "MachineName";

    private readonly List<DataType> _types = new();
    private readonly List<DataStream> _dataStreams = new();
    private readonly List<string> _data = new();
    private readonly AdapterHealthService _adapterHealthService;
    private readonly Mock<IApplicationManifest> _mockApplicationManifest;
    private bool _disposed;

    public AdapterHealthService_Tests()
    {
        _mockApplicationManifest = new Mock<IApplicationManifest>();
        _mockApplicationManifest.Setup(manifest => manifest.MachineName).Returns(MachineName);
        _mockApplicationManifest.Setup(manifest => manifest.ServiceName).Returns(UnitTestComponentType);
        _adapterHealthService = GetAdapterHealthService();
    }

    [Fact]
    public async Task InitializeSendsTypesAndContainers_Adapter_Test()
    {
        await _adapterHealthService.InitializeAsync();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_dataStreams);

        foreach (var dataStream in _dataStreams)
        {
            var streamIdStructure = dataStream.Id.Split(".");
            Assert.Equal(4, streamIdStructure.Length);
            Assert.Equal(MachineName, streamIdStructure[0]);
            Assert.Equal(UnitTestComponentType, streamIdStructure[1]);
            Assert.Equal(UnitTestComponentId, streamIdStructure[2]);
            Assert.NotEmpty(dataStream.Metadata);
            Assert.Equal(ExpectedMetadataItemCount, dataStream.Metadata.Count);
            Assert.Equal(UnitTestComponentId, dataStream.Metadata[DataSourceKey].ToString());
            Assert.Equal(UnitTestComponentType, dataStream.Metadata[AdapterTypeKey].ToString());
        }
    }

    [Fact]
    public async Task InitializeSendsTypesAndContainers_Eds_Test()
    {
        _mockApplicationManifest.Setup(manifest => manifest.IsEdgeDataStore).Returns(true);
        using var adapterHealthService = GetAdapterHealthService();

        await adapterHealthService.InitializeAsync();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_dataStreams);

        foreach (var dataStream in _dataStreams)
        {
            var streamIdStructure = dataStream.Id.Split(".");
            Assert.Equal(3, streamIdStructure.Length);
            Assert.Equal(MachineName, streamIdStructure[0]);
            Assert.Equal(UnitTestComponentId, streamIdStructure[1]);
            Assert.NotEmpty(dataStream.Metadata);
            Assert.Equal(ExpectedMetadataItemCount, dataStream.Metadata.Count);
            Assert.Equal(UnitTestComponentId, dataStream.Metadata[DataSourceKey].ToString());
            Assert.Equal(UnitTestComponentType, dataStream.Metadata[AdapterTypeKey].ToString());
        }
    }

    [Fact]
    public void ResendTypesAndStreams_Adapter_Test()
    {
        _adapterHealthService.ResendTypesAndStreams();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_dataStreams);
        
        foreach (var dataStream in _dataStreams)
        {
            var streamIdStructure = dataStream.Id.Split(".");
            Assert.Equal(4, streamIdStructure.Length);
            Assert.Equal(MachineName, streamIdStructure[0]);
            Assert.Equal(UnitTestComponentType, streamIdStructure[1]);
            Assert.Equal(UnitTestComponentId, streamIdStructure[2]);
            Assert.NotEmpty(dataStream.Metadata);
            Assert.Equal(ExpectedMetadataItemCount, dataStream.Metadata.Count);
            Assert.Equal(UnitTestComponentId, dataStream.Metadata[DataSourceKey].ToString());
            Assert.Equal(UnitTestComponentType, dataStream.Metadata[AdapterTypeKey].ToString());
        }
    }

    [Fact]
    public void ResendTypesAndStreams_Eds_Test()
    {
        _mockApplicationManifest.Setup(manifest => manifest.IsEdgeDataStore).Returns(true);

        using var adapterHealthService = GetAdapterHealthService();
        adapterHealthService.ResendTypesAndStreams();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_dataStreams);
        
        foreach (var dataStream in _dataStreams)
        {
            var streamIdStructure = dataStream.Id.Split(".");
            Assert.Equal(3, streamIdStructure.Length);
            Assert.Equal(MachineName, streamIdStructure[0]);
            Assert.Equal(UnitTestComponentId, streamIdStructure[1]);
            Assert.NotEmpty(dataStream.Metadata);
            Assert.Equal(ExpectedMetadataItemCount, dataStream.Metadata.Count);
            Assert.Equal(UnitTestComponentId, dataStream.Metadata[DataSourceKey].ToString());
            Assert.Equal(UnitTestComponentType, dataStream.Metadata[AdapterTypeKey].ToString());
        }
    }

    [Fact]
    public void ResendLinks_Test()
    {
        using var adapterHealthService = GetAdapterHealthService();
        adapterHealthService.ResendLinks();
        Assert.Equal(3, _data.Count);
    }

    [Fact]
    public async Task StartAsyncStartsHeartbeat()
    {
        await _adapterHealthService.StartAsync();
        await Task.Delay(2000);
        Assert.Contains(_data, x => x.Contains(HealthOmfMessageCreatorBase.NextHealthMessageExpected, StringComparison.InvariantCulture));
    }

    [Fact]
    public void ThrowIfStartNotCalledBeforeSendingDeviceStatus()
    {
        Assert.Throws<InvalidOperationException>(() => _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.DeviceInError));
    }

    [Fact]
    public async Task SendDeviceSendsData()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.Good);
        await _adapterHealthService.StopAsync();
        await Task.Delay(500);
        Assert.Contains(_data, x => x.Contains(DeviceStatus, StringComparison.InvariantCulture));
    }

    [Fact]
    public async Task DuplicateStatusPreventsDataSend()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.AttemptingFailover);
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.AttemptingFailover);
        await _adapterHealthService.StopAsync();

        int count = _data.FindAll(x => x.Contains(DeviceStatus, StringComparison.InvariantCulture)).Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SendMultipleStatusDataSend()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.AttemptingFailover);
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.ConnectedNoData);
        await _adapterHealthService.StopAsync();

        var count = _data.FindAll(x => x.Contains(DeviceStatus, StringComparison.InvariantCulture)).Count;
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task UpdateDeviceStatusAndSuppress_Test()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.UpdateDeviceStatusAndSuppress(Abstractions.Health.DeviceStatus.AttemptingFailover);
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.DeviceInError);
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.ConnectedNoData);
        await _adapterHealthService.StopAsync();

        var count = _data.FindAll(x => x.Contains(DeviceStatus, StringComparison.InvariantCulture)).Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ActivateDeviceStatusUpdates_Test()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.UpdateDeviceStatusAndSuppress(Abstractions.Health.DeviceStatus.AttemptingFailover);
        _adapterHealthService.ActivateDeviceStatusUpdates();
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.DeviceInError);
        _adapterHealthService.SendDeviceStatus(Abstractions.Health.DeviceStatus.ConnectedNoData);
        await _adapterHealthService.StopAsync();

        var count = _data.FindAll(x => x.Contains(DeviceStatus, StringComparison.InvariantCulture)).Count;
        Assert.Equal(3, count);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _adapterHealthService.Dispose();
        }

        _disposed = true;
    }

    private AdapterHealthService GetAdapterHealthService() =>
        new(new FakeHealthMessageProcessor(_types, _dataStreams, _data),
            new Mock<ILogger>().Object,
            _mockApplicationManifest.Object,
            UnitTestComponentId,
            UnitTestComponentType,
            "c",
            "d");

    internal class FakeHealthMessageProcessor : IHealthMessageProcessor
    {
        private readonly List<DataType> _types;
        private readonly List<DataStream> _containers;
        private readonly List<string> _data;

        public FakeHealthMessageProcessor(List<DataType> types, List<DataStream> containers, List<string> data)
        {
            _types = types;
            _containers = containers;
            _data = data;
        }

        public MetadataInfo StreamMetadataLevel { get; set; }

        public void WriteHealthStreams(DataStream[] dataStreams, MessageAction messageAction = MessageAction.Update)
        {
            _containers.AddRange(dataStreams);
        }

        public void WriteHealthValue<T>(string id, Classification classification, T instance, MessageAction messageAction = MessageAction.Create)
        {
            _data.Add(id);
        }

        public void WriteHealthTypes(DataType[] dataTypes, MessageAction messageAction = MessageAction.Create)
        {
            _types.AddRange(dataTypes);
        }
    }
}
