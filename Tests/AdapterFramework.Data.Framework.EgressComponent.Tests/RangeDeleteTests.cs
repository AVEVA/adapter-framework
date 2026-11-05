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
using AdapterFramework.Data.Framework.Abstractions.DataFilters;
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
using Microsoft.Extensions.Logging;
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
    public void RangeDelete_PreservesLocalEnqueueOrderAndSeparateRanges()
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

    [Theory]
    [InlineData(9, 10, false)]
    [InlineData(13, 14, false)]
    [InlineData(11, 12, true)]
    [InlineData(12, 13, true)]
    [InlineData(12, 12, true)]
    public void RangeDelete_PreservesFilteringOnlyForUnaffectedReadings(int startHour, int endHour, bool overlaps)
    {
        var sink = new Mock<IMessageProcessor>();
        var adapter = new AdapterMessageProcessor(sink.Object, OmfVersion.Omf20);
        adapter.ProcessDataFiltersConfigurationChanges([new DataFiltersConfiguration { Id = "Filter", AbsoluteDeadband = 1 }]);
        var selection = new TestDataSelectionItem("Stream") { DataFilterId = "Filter" };
        var first = new TimeIndexedValue<int> { Timestamp = Start.AddHours(12).UtcDateTime, Value = 100 };
        adapter.WriteDynamicValue(selection, first, MessageAction.Create);
        var cache = selection.DataFilterCache;
        var range = new StreamingDataRange(Start.AddHours(startHour), Start.AddHours(endHour));

        adapter.DeleteDynamicValuesInRange(selection, range);
        if (overlaps)
        {
            Assert.Null(selection.DataFilterCache);
        }
        else
        {
            Assert.Same(cache, selection.DataFilterCache);
        }

        var next = new TimeIndexedValue<int> { Timestamp = first.Timestamp.AddMinutes(1), Value = 100 };
        adapter.WriteDynamicValue(selection, next, MessageAction.Create);
        sink.Verify(p => p.WriteDynamicValue("Stream", It.IsAny<TimeIndexedValue<int>>(), MessageAction.Create, null), Times.Exactly(overlaps ? 2 : 1));
        sink.Verify(p => p.DeleteDynamicValuesInRange("Stream", range, null), Times.Once);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(11)]
    public void RangeDelete_InvalidatesLastPreviousAndCurrentReadings(int deletedHour)
    {
        var sink = new Mock<IMessageProcessor>();
        var adapter = new AdapterMessageProcessor(sink.Object, OmfVersion.Omf20);
        adapter.ProcessDataFiltersConfigurationChanges([new DataFiltersConfiguration { Id = "Filter", AbsoluteDeadband = 1 }]);
        var selection = new TestDataSelectionItem("Stream") { DataFilterId = "Filter" };
        // The transmitted reading is at 12:00, the suppressed previous reading is at 13:00,
        // and the out-of-order current reading is at 11:00. Each can independently overlap.
        foreach (var hour in new[] { 12, 13, 11 })
        {
            adapter.WriteDynamicValue(selection, new TimeIndexedValue<int> { Timestamp = Start.AddHours(hour).UtcDateTime, Value = 100 }, MessageAction.Create);
        }

        var timestamp = Start.AddHours(deletedHour);
        adapter.DeleteDynamicValuesInRange(selection, new StreamingDataRange(timestamp, timestamp));
        Assert.Null(selection.DataFilterCache);
        adapter.WriteDynamicValue(selection, new TimeIndexedValue<int> { Timestamp = Start.AddHours(14).UtcDateTime, Value = 100 }, MessageAction.Create);
        sink.Verify(p => p.WriteDynamicValue("Stream", It.IsAny<TimeIndexedValue<int>>(), MessageAction.Create, null), Times.Exactly(3));
    }

    [Fact]
    public void RangeDelete_UnknownCacheIsClearedOnlyAfterSuccessfulAdmission()
    {
        var sink = new Mock<IMessageProcessor>();
        var adapter = new AdapterMessageProcessor(sink.Object, OmfVersion.Omf20);
        var cache = new Mock<IDataFilterCache>().Object;
        var selection = new TestDataSelectionItem("Stream") { DataFilterCache = cache };
        var range = new StreamingDataRange(Start, Start);
        sink.Setup(p => p.DeleteDynamicValuesInRange("Stream", range, null)).Throws(new InvalidOperationException("Rejected"));
        Assert.Throws<InvalidOperationException>(() => adapter.DeleteDynamicValuesInRange(selection, range));
        Assert.Same(cache, selection.DataFilterCache);

        sink.Setup(p => p.DeleteDynamicValuesInRange("Stream", range, null));
        adapter.DeleteDynamicValuesInRange(selection, range);
        Assert.Null(selection.DataFilterCache);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc, true)]
    [InlineData(DateTimeKind.Local, true)]
    [InlineData(DateTimeKind.Unspecified, false)]
    public void RangeDelete_PreservesOnlyKnownTimestampOffsets(DateTimeKind kind, bool preserve)
    {
        var adapter = new AdapterMessageProcessor(new Mock<IMessageProcessor>().Object, OmfVersion.Omf20);
        adapter.ProcessDataFiltersConfigurationChanges([new DataFiltersConfiguration { Id = "Filter", AbsoluteDeadband = 1 }]);
        var selection = new TestDataSelectionItem("Stream") { DataFilterId = "Filter" };
        var utc = Start.AddHours(12).UtcDateTime;
        var timestamp = kind == DateTimeKind.Local ? utc.ToLocalTime() : DateTime.SpecifyKind(utc, kind);
        adapter.WriteDynamicValue(selection, new TimeIndexedValue<int> { Timestamp = timestamp, Value = 100 }, MessageAction.Create);
        var cache = selection.DataFilterCache;
        adapter.DeleteDynamicValuesInRange(selection, new StreamingDataRange(Start.AddHours(9), Start.AddHours(10)));
        Assert.Same(preserve ? cache : null, selection.DataFilterCache);
    }

    [Fact]
    public void RangeDelete_AfterDisposalThrows()
    {
        using var processor = CreateProcessor(new ConcurrentQueue<ISerializedOmfMessage>());
        processor.Dispose();
        Assert.Throws<ObjectDisposedException>(() => processor.DeleteDynamicValuesInRange("Stream", new StreamingDataRange(Start, Start)));
    }

    [Fact]
    public void RangeDelete_AfterWorkerFaultThrows()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        using var processor = CreateProcessor(new ConcurrentQueue<ISerializedOmfMessage>(), logger: logger.Object);
        var values = new Mock<IReadOnlyList<object>>();
        values.SetupGet(v => v.Count).Throws(new InvalidOperationException("Injected value-list failure"));
        processor.WriteDynamicValues("Stream", values.Object, MessageAction.Create);
        Assert.True(SpinWait.SpinUntil(() => logger.Invocations.ToArray().Any(i =>
            i.Method.Name == nameof(ILogger.Log) && ((EventId)i.Arguments[1]).Id == 5008), TimeSpan.FromSeconds(10)), "The instance worker did not fault.");
        Assert.Throws<InvalidOperationException>(() => processor.DeleteDynamicValuesInRange("Stream", new StreamingDataRange(Start, Start)));
    }

    [Theory]
    [InlineData(OmfVersion.Omf20, 1, 5013, "RangeDeleteTooLarge")]
    [InlineData(OmfVersion.Omf12, 4096, 5014, "UnsupportedRangeDelete")]
    public void RangeDelete_RejectionsHaveDistinctLogEvents(OmfVersion version, int maximumBytes, int eventId, string eventName)
    {
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(LogLevel.Error)).Returns(true);
        using (var block = new SerializationBlock(null, null, maximumBytes, logger.Object, -1,
            new OmfJsonSerializer(), null, _ => Assert.Fail("Rejected delete was forwarded"), CancellationToken.None, version))
        {
            Assert.True(block.Post(new RangeDeleteMessage("Stream", new StreamingDataRange(Start, Start))));
        }

        logger.Verify(l => l.Log(LogLevel.Error, It.Is<EventId>(e => e.Id == eventId && e.Name == eventName),
            It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
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
    public void RangeDelete_RejectsOversizedPayloadBeforeEnqueueing()
    {
        const int maximumBytes = 512 * 1024;
        var range = new StreamingDataRange(Start, Start);
        var serializer = new OmfJsonSerializer();
        var wrapperSize = serializer.Serialize(new Dictionary<string, RangeDeleteMessage[]>
        {
            ["streamingdata"] = [new RangeDeleteMessage("S", range)],
        }).Length - 1;
        var idAtLimit = new string('S', maximumBytes - wrapperSize);
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        using (var processor = CreateProcessor(sent))
        {
            var error = Assert.Throws<ArgumentException>(() => processor.DeleteDynamicValuesInRange(idAtLimit + "S", range));
            Assert.Equal("id", error.ParamName);
            // Escaping makes the serialized size larger than the character count.
            Assert.Throws<ArgumentException>(() => processor.DeleteDynamicValuesInRange(new string('"', maximumBytes / 2), range));
            processor.DeleteDynamicValuesInRange(idAtLimit, range);
        }

        Assert.Equal(maximumBytes, Assert.Single(sent).MessageBody.Length);
    }

    [Fact]
    public void RangeDelete_SizeValidationUsesConfiguredCompression()
    {
        var compressor = new AdapterFramework.Data.Framework.Compression.GZipCompressor();
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        var id = new string('S', 512 * 1024);
        using (var processor = CreateProcessor(sent, compressor: compressor))
        {
            processor.DeleteDynamicValuesInRange(id, new StreamingDataRange(Start, Start));
        }

        var message = Assert.Single(sent);
        Assert.True(message.MessageBody.Length < 512 * 1024);
        using var payload = JsonDocument.Parse(compressor.Decompress(message.MessageBody));
        Assert.Equal(id, payload.RootElement.GetProperty("streamingdata")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(OmfVersion.Omf12)]
    [InlineData(OmfVersion.Omf13)]
    public void RangeDelete_UnsupportedMessageDoesNotFaultSerializationBlock(OmfVersion version)
    {
        var sent = new ConcurrentQueue<ISerializedOmfMessage>();
        var logger = new TestLogger();
        using (var block = new SerializationBlock(null, null, 4096, logger, -1,
            new OmfJsonSerializer(), null, sent.Enqueue, CancellationToken.None, version))
        {
            block.Post(new RangeDeleteMessage("Stream", new StreamingDataRange(Start, Start)));
            block.Post(new OmfMessage<DataType>(1, [new StaticDataType { Id = "Type" }], MessageAction.Create));
        }

        Assert.Equal(MessageAction.Create, Assert.Single(sent).MessageAction);
        Assert.True(logger.AreErrorsWarningsInLog());
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

    private static DataMessageProcessor CreateProcessor(ConcurrentQueue<ISerializedOmfMessage> sent, OmfVersion version = OmfVersion.Omf20, ICompressor compressor = null, ILogger logger = null)
    {
        var endpoints = new Mock<IOmfDataEndpointManager>();
        endpoints.Setup(e => e.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback<ISerializedOmfMessage>(sent.Enqueue);
        var logs = new Mock<ILogManager>();
        logs.Setup(l => l.GetOrCreateLogger(It.IsAny<string>())).Returns(logger ?? NullLogger.Instance);
        var manifest = new Mock<IApplicationManifest>();
        manifest.SetupGet(m => m.OmfVersion).Returns(version);
        return new DataMessageProcessor(logs.Object, new OmfJsonSerializer(), compressor, endpoints.Object,
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
