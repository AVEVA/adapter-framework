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
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.EgressComponent;
using AdapterFramework.Data.Framework.Extensions;
using Xunit;

namespace AdapterFramework.Data.Framework.Diagnostics.Tests;

public class EdgeDiagnosticsService_Tests : IDisposable
{
    private readonly List<DataType> _types = new();
    private readonly List<DataStream> _containers = new();
    private readonly List<string> _data = new();
    private readonly IEdgeDiagnosticsService _edgeDiagnosticsService;
    private readonly CancellationToken _cancellationToken = CancellationToken.None;

    public EdgeDiagnosticsService_Tests()
    {
        var logger = new Mock<ILogger>();
        var configurationProvider = new Mock<IConfigurationProvider>();

        var fakeHealthMessageProcessor = new FakeHealthMessageProcessor(_types, _containers, _data);
        var diagnosticsMessageProcessor = new DiagnosticsMessageProcessor(configurationProvider.Object, fakeHealthMessageProcessor, new ApplicationManifest())
        {
            SystemDiagnosticsEnabled = true,
        };

        _edgeDiagnosticsService = new EdgeDiagnosticsService(logger.Object, configurationProvider.Object, new ApplicationManifest(),
            diagnosticsMessageProcessor);
    }

    [Fact]
    public async Task InitializeSendsTypesAndContainers()
    {
        await _edgeDiagnosticsService.InitializeAsync(_cancellationToken);

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_containers);
    }

    [Fact]
    public void ResendTypesAndStreams_Test()
    {
        _types.Clear();
        _containers.Clear();

        _edgeDiagnosticsService.ResendTypesAndStreams();

        Assert.NotEmpty(_types);
        Assert.NotEmpty(_containers);
    }

    [Fact]
    public void EnableDiagnostics_Test()
    {
        var logger = new Mock<ILogger>();
        var configurationProvider = new Mock<IConfigurationProvider>();
        var fakeHealthMessageProcessor = new Mock<FakeHealthMessageProcessor>(_types, _containers, _data);
        var diagnosticsMessageProcessor = new DiagnosticsMessageProcessor(configurationProvider.Object, fakeHealthMessageProcessor.Object, new ApplicationManifest());
        var edgeDiagnosticsService = new Mock<EdgeDiagnosticsService>(logger.Object, configurationProvider.Object, new ApplicationManifest(),
            diagnosticsMessageProcessor);

        diagnosticsMessageProcessor.SystemDiagnosticsEnabled = true;

        _data.Clear();

        edgeDiagnosticsService.Object.UpdateDiagnostics();

        Assert.False(_data.IsEmpty());
    }

    [Fact]
    public void DisableDiagnostics_Test()
    {
        var logger = new Mock<ILogger>();
        var configurationProvider = new Mock<IConfigurationProvider>();
        var fakeHealthMessageProcessor = new Mock<FakeHealthMessageProcessor>(_types, _containers, _data);
        var diagnosticsMessageProcessor = new DiagnosticsMessageProcessor(configurationProvider.Object, fakeHealthMessageProcessor.Object, new ApplicationManifest());
        var edgeDiagnosticsService = new Mock<EdgeDiagnosticsService>(logger.Object, configurationProvider.Object, new ApplicationManifest(),
            diagnosticsMessageProcessor);

        diagnosticsMessageProcessor.SystemDiagnosticsEnabled = false;

        _data.Clear();

        edgeDiagnosticsService.Object.UpdateDiagnostics();

        Assert.True(_data.IsEmpty());
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Protected implementation of Dispose pattern.
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _edgeDiagnosticsService?.Dispose();
        }
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

    public void WriteHealthTypes(DataType[] dataTypes, MessageAction messageAction)
    {
        _types.AddRange(dataTypes);
    }

    public void WriteHealthStreams(DataStream[] dataStreams, MessageAction messageAction)
    {
        _containers.AddRange(dataStreams);
    }

    public void WriteHealthValue<T>(string id, Classification classification, T instance, MessageAction messageAction)
    {
        _data.Add(id);
    }
}
