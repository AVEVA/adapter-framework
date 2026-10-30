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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.DataFlow;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using AdapterFramework.Data.Framework.MessageProcessor;
using AdapterFramework.Data.Framework.MessageProcessor.DataFilters;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests;

public class RangeDeleteTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OmfVersion.Omf12)]
    [InlineData(OmfVersion.Omf13)]
    public void RangeDelete_RejectsUnsupportedVersions(OmfVersion version)
    {
        var sink = new Mock<IMessageProcessor>(MockBehavior.Strict);
        var adapter = new AdapterMessageProcessor(sink.Object, version);
        var range = new StreamingDataRange(Start, Start.AddHours(1));
        Assert.Throws<NotSupportedException>(() => adapter.DeleteDynamicValuesInRange(new TestDataSelectionItem("Stream"), range));
        using var egress = CreateProcessor(new ConcurrentQueue<ISerializedOmfMessage>(), version);
        Assert.Throws<NotSupportedException>(() => egress.DeleteDynamicValuesInRange("Stream", range));
        sink.VerifyNoOtherCalls();
    }

    [Fact]
    public void RangeDelete_ValidatesBoundsAndArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamingDataRange(Start, Start.AddTicks(-1)));
        var range = new StreamingDataRange(Start.ToOffset(TimeSpan.FromHours(3)), Start);
        Assert.Equal(TimeSpan.Zero, range.StartTime.Offset);
        Assert.Equal(Start, range.EndTime);
        Assert.Throws<ArgumentNullException>(() => new RangeDeleteMessage("Stream", null));
        Assert.Throws<ArgumentException>(() => new RangeDeleteMessage(" ", range));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RangeDeleteMessage("Stream", range, (PartitionKey)255));
        var adapter = new AdapterMessageProcessor(new Mock<IMessageProcessor>().Object, OmfVersion.Omf20);
        Assert.Throws<ArgumentNullException>(() => adapter.DeleteDynamicValuesInRange(null, range));
        Assert.Throws<ArgumentNullException>(() => adapter.DeleteDynamicValuesInRange(new TestDataSelectionItem("Stream"), null));
    }

    [Fact]
    public void RangeDelete_BypassesFiltersClearsCacheAndPreservesPrefix()
    {
        var sink = new Mock<IMessageProcessor>();
        var instrumented = new InstrumentedMessageProcessor(sink.Object, NullLogger.Instance, "Test", "Test");
        instrumented.SetStreamIdPrefix("Prefix.");
        var adapter = new AdapterMessageProcessor(instrumented, OmfVersion.Omf20);
        var selection = new TestDataSelectionItem("Stream")
        {
            DataFilterId = "Filter",
            DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<int>>(),
        };
        adapter.ProcessDataFiltersConfigurationChanges([new DataFiltersConfiguration { Id = "Filter", AbsoluteDeadband = 100 }]);
        var range = new StreamingDataRange(Start, Start.AddHours(1));
        adapter.DeleteDynamicValuesInRange(selection, range, PartitionKey.Key7);
        sink.Verify(p => p.DeleteDynamicValuesInRange("Prefix.Stream", range, PartitionKey.Key7), Times.Once);
        sink.VerifyNoOtherCalls();
        Assert.Null(selection.DataFilterCache);
        Assert.Equal(0, instrumented.GetAndResetEventsCounter());
    }

    [Fact]
    public void RangeDelete_PreservesWriteDeleteWriteOrderAndSeparateRanges()
    {
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        using (var processor = CreateProcessor(sent))
        {
            processor.WriteDynamicValue("Stream", new TimeIndexedValue<int> { Timestamp = Start.UtcDateTime, Value = 1 }, MessageAction.Create, PartitionKey.Key7);
            processor.DeleteDynamicValuesInRange("Stream", new StreamingDataRange(Start, Start.AddHours(1)), PartitionKey.Key7);
            processor.DeleteDynamicValuesInRange("Stream", new StreamingDataRange(Start.AddHours(2), Start.AddHours(3)), PartitionKey.Key7);
            processor.WriteDynamicValue("Stream", new TimeIndexedValue<int> { Timestamp = Start.UtcDateTime, Value = 2 }, MessageAction.Create, PartitionKey.Key7);
        }

        var messages = sent.ToArray();
        Assert.Equal(new[] { MessageAction.Create, MessageAction.Delete, MessageAction.Delete, MessageAction.Create }, messages.Select(m => m.MessageAction));
        Assert.All(messages, m => Assert.Equal(PartitionKey.Key7, m.PartitionKey));
        AssertRange(messages[1], Start, Start.AddHours(1));
        AssertRange(messages[2], Start.AddHours(2), Start.AddHours(3));
    }

    [Fact]
    public void RangeDelete_FlushesPendingValueDeletesBeforeRange()
    {
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        using (var processor = CreateProcessor(sent))
        {
            processor.WriteDynamicValue("Stream", new TimeIndexedValue<int> { Timestamp = Start.UtcDateTime }, MessageAction.Delete);
            processor.DeleteDynamicValuesInRange("Stream", new StreamingDataRange(Start, Start));
        }

        var messages = sent.ToArray();
        Assert.Equal(2, messages.Length);
        using var values = JsonDocument.Parse(messages[0].MessageBody);
        Assert.True(values.RootElement.GetProperty("streamingdata")[0].TryGetProperty("values", out _));
        AssertRange(messages[1], Start, Start);
    }

    [Fact]
    public void RangeDelete_SurvivesPersistentBufferRoundTrip()
    {
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        using (var processor = CreateProcessor(sent))
        {
            processor.DeleteDynamicValuesInRange("Stream", new StreamingDataRange(Start, Start.AddHours(1)), PartitionKey.Key16);
        }

        var original = Assert.Single(sent);
        DataItem stored = null;
        var queue = new Mock<IPersistentQueue>();
        queue.Setup(q => q.Enqueue(It.IsAny<DataItem>())).Callback<DataItem>(item => stored = item);
        queue.Setup(q => q.Dequeue()).Returns(() => stored);
        using var buffer = new PersistentOmfMessageQueue("http://localhost/omf", queue.Object, NullLogger.Instance);
        buffer.Enqueue(original);
        Assert.True(buffer.TryDequeue(out var restored));
        AssertRange(restored, Start, Start.AddHours(1));
        Assert.Equal(PartitionKey.Key16, restored.PartitionKey);
        Assert.Equal(original.MessageBody, restored.MessageBody);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangeDelete_EnforcesSizeLimitWithoutSplittingBounds(bool fits)
    {
        var message = new RangeDeleteMessage("Stream", new StreamingDataRange(Start, Start));
        var serializer = new OmfJsonSerializer();
        var size = serializer.Serialize(new Dictionary<string, RangeDeleteMessage[]> { ["streamingdata"] = [message] }).Length;
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        var logger = new TestLogger();
        using (var block = new SerializationBlock(null, null, fits ? size : size - 1, logger, -1, serializer, null, sent.Enqueue, CancellationToken.None, OmfVersion.Omf20))
        {
            block.Post(message);
        }

        Assert.Equal(fits ? 1 : 0, sent.Count);
        Assert.Equal(!fits, logger.AreErrorsWarningsInLog());
    }

    [Fact]
    public void RangeDelete_UsesConfiguredCompression()
    {
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        var compressor = new AdapterFramework.Data.Framework.Compression.GZipCompressor();
        using (var block = new SerializationBlock(null, null, 4096, NullLogger.Instance, -1,
            new OmfJsonSerializer(), compressor, sent.Enqueue, CancellationToken.None, OmfVersion.Omf20))
        {
            block.Post(new RangeDeleteMessage("Stream", new StreamingDataRange(Start, Start)));
        }

        var compressed = Assert.Single(sent);
        AssertRange(new SerializedOmfMessage(compressed.MessageType, compressor.Decompress(compressed.MessageBody),
            compressed.MessageAction, compressed.ItemCount, compressed.OmfVersion, compressed.PartitionKey), Start, Start);
    }

    private static DataMessageProcessor CreateProcessor(ConcurrentQueue<ISerializedOmfMessage> sent, OmfVersion version = OmfVersion.Omf20)
    {
        var endpoints = new Mock<IOmfDataEndpointManager>();
        endpoints.Setup(e => e.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback<ISerializedOmfMessage>(sent.Enqueue);
        var logs = new Mock<ILogManager>();
        logs.Setup(l => l.GetOrCreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);
        var manifest = new Mock<IApplicationManifest>();
        manifest.SetupGet(m => m.OmfVersion).Returns(version);
        return new DataMessageProcessor(logs.Object, new OmfJsonSerializer(), null, endpoints.Object,
            new Mock<IEgressComponentIdService>().Object, new Mock<IConfigurationProvider>().Object, applicationManifest: manifest.Object);
    }

    private static void AssertRange(ISerializedOmfMessage message, DateTimeOffset start, DateTimeOffset end)
    {
        Assert.Equal(MessageType.Instance, message.MessageType);
        Assert.Equal(MessageAction.Delete, message.MessageAction);
        Assert.Equal(OmfVersion.Omf20, message.OmfVersion);
        Assert.Equal(0, message.ItemCount);
        Assert.True(message.ResourceCounts.IsEmpty);
        using var document = JsonDocument.Parse(message.MessageBody);
        var item = Assert.Single(document.RootElement.GetProperty("streamingdata").EnumerateArray());
        Assert.Equal("Stream", item.GetProperty("id").GetString());
        Assert.False(item.TryGetProperty("values", out _));
        Assert.False(item.TryGetProperty("PartitionKey", out _));
        Assert.Equal(start, item.GetProperty("range").GetProperty("starttime").GetDateTimeOffset());
        Assert.Equal(end, item.GetProperty("range").GetProperty("endtime").GetDateTimeOffset());
    }
}
