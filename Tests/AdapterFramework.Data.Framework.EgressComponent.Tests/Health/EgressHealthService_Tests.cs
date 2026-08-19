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
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.EgressComponent.Health;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests.Health;

public class EgressHealthService_Tests : IDisposable
{
    private const string _DeviceStatus = "DeviceStatus";
    private const int HealthServiceTimeout = 1500;

    private readonly List<DataType> _types = new List<DataType>();
    private readonly List<DataStream> _containers = new List<DataStream>();
    private readonly List<string> _data = new List<string>();
    private readonly IHealthMessageProcessor _fakeHealthMessageProcessor;
    private readonly EgressHealthService _adapterHealthService;
    private bool _disposed;

    public EgressHealthService_Tests()
    {
        var logger = new Mock<ILogger>();
        _fakeHealthMessageProcessor = new FakeHealthMessageProcessor(_types, _containers, _data);
        _adapterHealthService = new EgressHealthService(_fakeHealthMessageProcessor, logger.Object, new ApplicationManifest(), "a");
    }

    [Fact]
    public async Task InitializeSendsTypesAndContainers()
    {
        await _adapterHealthService.InitializeAsync();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_containers);
    }

    [Fact]
    public void ResendTypesAndStreams_Test()
    {
        _adapterHealthService.ResendTypesAndStreams();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_containers);
    }

    [Fact]
    public void ResendLinks_Test()
    {
        _adapterHealthService.ResendLinks();
        Assert.Equal(3, _data.Count);
    }

    [Fact]
    public async Task StartAsyncStartsHeartbeat()
    {
        await _adapterHealthService.StartAsync();
        Assert.True(SpinWait.SpinUntil(() => _data.Count > 4, HealthServiceTimeout), $"Data count was only {_data.Count} {string.Join(", ", _data)}");
        Assert.Contains(_data, x => x.Contains(HealthOmfMessageCreatorBase.NextHealthMessageExpected, StringComparison.InvariantCulture));
    }

    [Fact]  
    public void ThrowIfStartNotCalledBeforeSendingDeviceStatus()
    {
        Assert.Throws<InvalidOperationException>(
            () => _adapterHealthService.SendDeviceStatus(DeviceStatus.DeviceInError));
    }

    [Fact]
    public async Task SendDeviceSendsData()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.SendDeviceStatus(DeviceStatus.Good);
        await _adapterHealthService.StopAsync();
        Assert.True(SpinWait.SpinUntil(() => _data.Count > 4, HealthServiceTimeout));
        Assert.Contains(_data, x => x.Contains(HealthOmfMessageCreatorBase.DeviceStatus, StringComparison.InvariantCulture));
    }

    [Fact]
    public async Task DuplicateStatusPreventsDataSend()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.SendDeviceStatus(DeviceStatus.AttemptingFailover);
        _adapterHealthService.SendDeviceStatus(DeviceStatus.AttemptingFailover);
        await _adapterHealthService.StopAsync();

        int count = _data.FindAll(x => x.Contains(_DeviceStatus, StringComparison.InvariantCulture)).Count;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SendMultipleStatusDataSend()
    {
        await _adapterHealthService.StartAsync();
        _adapterHealthService.SendDeviceStatus(DeviceStatus.AttemptingFailover);
        _adapterHealthService.SendDeviceStatus(DeviceStatus.ConnectedNoData);
        await _adapterHealthService.StopAsync();

        int count = _data.FindAll(x => x.Contains(_DeviceStatus, StringComparison.InvariantCulture)).Count;
        Assert.Equal(2, count);
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
            _adapterHealthService?.Dispose();
        }

        _disposed = true;
    }
}

#pragma warning disable SA1402 // File may only contain a single type
public class FakeHealthMessageProcessor : IHealthMessageProcessor
#pragma warning restore SA1402 // File may only contain a single type
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

    public void WriteHealthStreams(DataStream[] dataStreams, MessageAction messageAction)
    {
        _containers.AddRange(dataStreams);
    }

    public void WriteHealthValue<T>(string id, Classification classification, T instance, MessageAction messageAction)
    {
        _data.Add(id);
    }

    public void WriteHealthTypes(DataType[] dataTypes, MessageAction messageAction)
    {
        _types.AddRange(dataTypes);
    }
}
