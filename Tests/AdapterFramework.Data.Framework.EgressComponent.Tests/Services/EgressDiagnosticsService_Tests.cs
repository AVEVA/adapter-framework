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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Common.Constants;
using AdapterFramework.Data.Framework.Common.Diagnostics.Events;
using AdapterFramework.Data.Framework.EgressComponent.Diagnostics;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests.Services;

public class EgressDiagnosticsService_Tests
{
    private const int WaitTime = 3_000;

    private readonly List<DataType> _diagnosticTypes;
    private readonly List<DataStream> _diagnosticContainers;
    private readonly List<string> _diagnosticData;
    private readonly TestLogger _testLogger;
    private readonly IDiagnosticsMessageProcessor _fakeDiagnosticsMessageProcessor;
    private readonly LinkNode _node = new DataTypeLinkNode("abc", "Def");
    private readonly string _id = "Id";

    public EgressDiagnosticsService_Tests()
    {
        _diagnosticTypes = new List<DataType>();
        _diagnosticContainers = new List<DataStream>();
        _diagnosticData = new List<string>();

        _testLogger = new TestLogger();
        _fakeDiagnosticsMessageProcessor = new FakeDiagnosticsMessageProcessor(_diagnosticTypes, _diagnosticContainers, _diagnosticData);
    }

    public static IEnumerable<object[]> GetCounters()
    {
        TestUtilities.CreateDataProtectorInstance(null, null);
        yield return new object[]
        {
            new Dictionary<string, long>()
            {
                { "a", 5 },
                { "b",  10 },
            },
        };
        yield return new object[]
        {
            new Dictionary<string, long>()
            {
                { "a", 5 },
            },
        };
        yield return new object[]
        {
            new Dictionary<string, long>(),
        };
    }

    [Fact]
    public void Constructor_InvalidInput_Test()
    {
        var mockLogger = new Mock<IInstrumentedLogger>();
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var dataEndpointManager = new Mock<IOmfDataEndpointManager>();

        Assert.Throws<ArgumentNullException>(() => new EgressDiagnosticsService(null, mockLogger.Object, _id, dataEndpointManager.Object, _node, null));
        Assert.Throws<ArgumentNullException>(() => new EgressDiagnosticsService(mockDiagnosticsMessageProcessor.Object, null, _id, dataEndpointManager.Object, _node, null));
        Assert.Throws<ArgumentNullException>(() => new EgressDiagnosticsService(mockDiagnosticsMessageProcessor.Object, mockLogger.Object, _id, null, _node, null));
    }

    [Fact]
    public async Task InitializeAsync_Streams_Types_Sent_Test()
    {
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        using var egressDiagnosticsService = new EgressDiagnosticsService(_fakeDiagnosticsMessageProcessor, _testLogger, _id, mockEndpointManager.Object, _node, null);
        await egressDiagnosticsService.InitializeAsync();

        Assert.True(_diagnosticTypes.Count > 0);
    }

    [Theory]
    [MemberData(nameof(GetCounters))]
    public async Task EgressEndpointConfiguration_ResendTypesAndStreamsAsync_NewEgressSendsMessages(Dictionary<string, long> counters)
    {
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long>());

        using var egressService = new EgressDiagnosticsService(_fakeDiagnosticsMessageProcessor, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(counters);
        await Task.Delay(2000);
        _diagnosticTypes.Clear();
        _diagnosticContainers.Clear();
        _diagnosticData.Clear();

        egressService.ResendTypesAndStreams();

        Assert.NotEmpty(_diagnosticTypes);
        Assert.True(counters.Count <= _diagnosticContainers.Count);
        
        if (counters.Count != 0)
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Once);
        }
        else
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Never);
        }
    }

    [Fact]
    public void EgressEndpointConfiguration_DuplicateEndpoint_ValidInput_Test()
    {
        var endpointConfiguration = new EgressEndpointConfiguration
        {
            Endpoint = "https://localhost:443",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var endpointConfiguration2 = new EgressEndpointConfiguration
        {
            Endpoint = "https://example.com",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var configs = new[] { endpointConfiguration, endpointConfiguration2 };
        var configChangedArgs = new ConfigurationChangedEventArgs(null, configs);
        var validationResults = EgressEndpointConfiguration.CheckForDuplicateEndpoints(configChangedArgs);
        Assert.Empty(validationResults);
    }

    [Theory]
    [MemberData(nameof(GetCounters))]
    public async Task EgressEndpointConfiguration_StartAsync_SendsMessages(Dictionary<string, long> counters)
    {
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(counters);

        using var egressService = new EgressDiagnosticsService(_fakeDiagnosticsMessageProcessor, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        Assert.True(SpinWait.SpinUntil(() => counters.Count <= _diagnosticContainers.Count, WaitTime));
        
        if (counters.Count != 0)
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Once);
        }
        else
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Never);
        }
    }

    [Theory]
    [MemberData(nameof(GetCounters))]
    public async Task EgressEndpointConfiguration_StartAsync_NewEgressSendsMessages(Dictionary<string, long> counters)
    {
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long>());

        using var egressService = new EgressDiagnosticsService(_fakeDiagnosticsMessageProcessor, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(counters);
        Assert.True(SpinWait.SpinUntil(() => counters.Count <= _diagnosticContainers.Count, WaitTime));
        
        if (counters.Count != 0)
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Once);
        }
        else
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Never);
        }
    }

    [Theory]
    [MemberData(nameof(GetCounters))]
    public async Task EgressEndpointConfiguration_ResendTypesStreams_SendsMessages(Dictionary<string, long> counters)
    {
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(counters);

        using var egressService = new EgressDiagnosticsService(_fakeDiagnosticsMessageProcessor, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        Assert.True(SpinWait.SpinUntil(() => counters.Count <= _diagnosticContainers.Count, WaitTime));

        _diagnosticTypes.Clear();
        _diagnosticContainers.Clear();
        _diagnosticData.Clear();

        egressService.ResendTypesAndStreams();

        Assert.True(counters.Count <= _diagnosticContainers.Count);
        Assert.NotEmpty(_diagnosticTypes);
        Assert.True(counters.Count <= _diagnosticData.Count);
        
        if (counters.Count != 0)
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Once);
        }
        else
        {
            mockHealthService.Verify(x => x.ResendTypesAndStreams(), Times.Never);
        }
    }

    [Fact]
    public async Task Omf20_StartAsync_PublishesResourceIoRateStreamsLinksAndValues()
    {
        const string EndpointId = "ep1";
        var recorder = new RecordingDiagnosticsMessageProcessor();
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long> { { EndpointId, 78 } });
        mockEndpointManager.Setup(x => x.GetAndResetEgressedResourceCounters())
            .Returns(new Dictionary<string, OmfResourceCounts> { { EndpointId, new OmfResourceCounts(60, 6, 12) } });

        using var egressService = new EgressDiagnosticsService(recorder, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object, OmfVersion.Omf20);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        var ioRateStreamId = $"{_id}.{EndpointId}.{DiagnosticsConstants.IoRateStreamName}";
        var streamIoRateStreamId = $"{_id}.{EndpointId}.{DiagnosticsConstants.StreamIoRateStreamName}";
        var assetIoRateStreamId = $"{_id}.{EndpointId}.{DiagnosticsConstants.AssetIoRateStreamName}";
        var eventIoRateStreamId = $"{_id}.{EndpointId}.{DiagnosticsConstants.EventIoRateStreamName}";
        var expectedStreamIds = new[] { ioRateStreamId, streamIoRateStreamId, assetIoRateStreamId, eventIoRateStreamId };

        Assert.True(SpinWait.SpinUntil(() => expectedStreamIds.All(id => recorder.GetIoRate(id).HasValue), WaitTime));

        var streams = recorder.GetStreams();
        foreach (var streamId in expectedStreamIds)
        {
            var stream = Assert.Single(streams, s => s.Id == streamId);
            Assert.Equal(DiagnosticsConstants.IoRateTypeId, stream.TypeId);
            Assert.Equal(streamId[(_id.Length + 1)..], stream.Name);
            Assert.Contains(streamId, recorder.GetLinkTargets());
        }

        // A single one-second sample has been taken, so each rate equals that second's count.
        Assert.Equal(78, recorder.GetIoRate(ioRateStreamId));
        Assert.Equal(60, recorder.GetIoRate(streamIoRateStreamId));
        Assert.Equal(6, recorder.GetIoRate(assetIoRateStreamId));
        Assert.Equal(12, recorder.GetIoRate(eventIoRateStreamId));
    }

    [Fact]
    public async Task Omf20_ResendTypesAndStreams_ResendsResourceIoRateStreams()
    {
        const string EndpointId = "ep1";
        var recorder = new RecordingDiagnosticsMessageProcessor();
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long> { { EndpointId, 0 } });

        using var egressService = new EgressDiagnosticsService(recorder, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object, OmfVersion.Omf20);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        Assert.True(SpinWait.SpinUntil(() => recorder.GetStreams().Count == 4, WaitTime));

        recorder.Clear();
        egressService.ResendTypesAndStreams();

        Assert.Equal(4, recorder.GetStreams().Count);
        Assert.Equal(4, recorder.GetLinkTargets().Count);
    }

    [Fact]
    public async Task Omf12_StartAsync_PublishesOnlyIoRateStream()
    {
        const string EndpointId = "ep1";
        var recorder = new RecordingDiagnosticsMessageProcessor();
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long> { { EndpointId, 5 } });

        using var egressService = new EgressDiagnosticsService(recorder, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object, OmfVersion.Omf12);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        var ioRateStreamId = $"{_id}.{EndpointId}.{DiagnosticsConstants.IoRateStreamName}";
        Assert.True(SpinWait.SpinUntil(() => recorder.GetIoRate(ioRateStreamId).HasValue, WaitTime));

        Assert.Equal(ioRateStreamId, Assert.Single(recorder.GetStreams()).Id);
        mockEndpointManager.Verify(x => x.GetAndResetEgressedResourceCounters(), Times.Never);
    }

    [Fact]
    public async Task Omf20_EndpointRemoved_StopsPublishingItsResourceIoRates()
    {
        var recorder = new RecordingDiagnosticsMessageProcessor();
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long> { { "ep1", 1 }, { "ep2", 1 } });

        using var egressService = new EgressDiagnosticsService(recorder, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object, OmfVersion.Omf20);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        Assert.True(SpinWait.SpinUntil(() => recorder.GetStreams().Count == 8, WaitTime));

        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long> { { "ep1", 1 } });
        recorder.Clear();

        // Removing an endpoint resends the remaining streams, which only include ep1.
        Assert.True(SpinWait.SpinUntil(() => recorder.GetStreams().Count == 4, WaitTime));
        Assert.All(recorder.GetStreams(), s => Assert.StartsWith($"{_id}.ep1.", s.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Omf20_ResourceIoRateWriteFailure_DoesNotStopIoRate()
    {
        const string EndpointId = "ep1";
        var streamIoRateStreamId = $"{_id}.{EndpointId}.{DiagnosticsConstants.StreamIoRateStreamName}";
        var recorder = new RecordingDiagnosticsMessageProcessor { FailValueWritesForStreamId = streamIoRateStreamId };
        var mockHealthService = new Mock<IEdgeComponentHealthService>();
        var mockEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockEndpointManager.Setup(x => x.GetAndResetEgressedValuesCounters()).Returns(new Dictionary<string, long> { { EndpointId, 3 } });

        using var egressService = new EgressDiagnosticsService(recorder, _testLogger, _id, mockEndpointManager.Object, _node, mockHealthService.Object, OmfVersion.Omf20);
        await egressService.InitializeAsync();
        await egressService.StartAsync();

        var ioRateStreamId = $"{_id}.{EndpointId}.{DiagnosticsConstants.IoRateStreamName}";
        Assert.True(SpinWait.SpinUntil(() => recorder.GetIoRate(ioRateStreamId).HasValue, WaitTime));
        Assert.True(SpinWait.SpinUntil(() => _testLogger.ContainsMessage("resource IORate"), WaitTime));
        Assert.Equal(3, recorder.GetIoRate(ioRateStreamId));
    }

    private sealed class RecordingDiagnosticsMessageProcessor : IDiagnosticsMessageProcessor
    {
        private readonly object _lock = new();
        private readonly List<DataStream> _streams = new();
        private readonly List<string> _linkTargets = new();
        private readonly Dictionary<string, double> _ioRates = new();

        public MetadataInfo StreamMetadataLevel { get; set; }

        public bool SystemDiagnosticsEnabled { get; set; } = true;

        public string StreamIdPrefix => null;

        public string FailValueWritesForStreamId { get; init; }

        public void WriteDiagnosticsTypes(DataType[] dataTypes)
        {
        }

        public void WriteDiagnosticsStreams(DataStream[] dataStreams)
        {
            lock (_lock)
            {
                _streams.AddRange(dataStreams);
            }
        }

        public void WriteDiagnosticsValue<T>(string id, Classification classification, T instance)
        {
            if (id == FailValueWritesForStreamId)
            {
                throw new InvalidOperationException("Simulated diagnostics write failure.");
            }

            lock (_lock)
            {
                switch (instance)
                {
                    case Link link:
                        _linkTargets.Add(link.Target.Id);
                        break;
                    case IoRateEvent ioRateEvent:
                        _ioRates[id] = ioRateEvent.IORate;
                        break;
                }
            }
        }

        public List<DataStream> GetStreams()
        {
            lock (_lock)
            {
                return _streams.ToList();
            }
        }

        public List<string> GetLinkTargets()
        {
            lock (_lock)
            {
                return _linkTargets.ToList();
            }
        }

        public double? GetIoRate(string streamId)
        {
            lock (_lock)
            {
                return _ioRates.TryGetValue(streamId, out var value) ? value : null;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _streams.Clear();
                _linkTargets.Clear();
                _ioRates.Clear();
            }
        }
    }
}
