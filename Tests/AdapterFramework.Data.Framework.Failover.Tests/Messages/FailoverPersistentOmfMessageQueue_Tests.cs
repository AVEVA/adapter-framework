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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Failover.Messages;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using static AdapterFramework.Data.Framework.Messages.SerializedOmfMessage;
using MessageType = AdapterFramework.Data.DataModel.MessageType;
using OmfVersion = AdapterFramework.Data.DataModel.OmfVersion;

namespace AdapterFramework.Data.Framework.Failover.Tests.Messages;

public class FailoverPersistentOmfMessageQueue_Tests
{
    private const string TestTargetIdentifier = "http://localhost:5590/omf";

    /// <summary>
    /// Verifies failover bodies are written as V6 records that store ticks exactly once and never store a scope ID,
    /// and preserve all metadata across persistence and conversion to a writer message.
    /// </summary>
    [Theory]
    [InlineData(false, 0L, false)]
    [InlineData(false, long.MaxValue, true)]
    [InlineData(true, 0L, true)]
    [InlineData(true, long.MinValue, false)]
    public void FailoverPersistentOmfMessageQueue_RoundTrip_PreservesLayoutAndWriterConversion(bool withId, long ticks, bool emptyBody)
    {
        var body = emptyBody ? Array.Empty<byte>() : new byte[] { 0x11, 0x22, 0x33 };
        var message = new FailoverSerializedOmfMessage(MessageType.Instance, body, MessageAction.Update, 0x01020304, OmfVersion.Omf20, PartitionKey.Key16)
        {
            SerializedMessageId = withId ? Guid.NewGuid() : null,
            ProcessTimeTicks = ticks,
        };
        DataItem enqueued = null;
        var persistentQueue = new Mock<IPersistentQueue>();
        persistentQueue.Setup(q => q.Enqueue(It.IsAny<DataItem>())).Callback((DataItem item) => enqueued = item);
        persistentQueue.Setup(q => q.Peek()).Returns(() => new DataItem(enqueued.Version, enqueued.Data));
        persistentQueue.Setup(q => q.Dequeue()).Returns(() => new DataItem(enqueued.Version, enqueued.Data));
        using var queue = new FailoverPersistentOmfMessageQueue(TestTargetIdentifier, persistentQueue.Object, null);

        queue.Enqueue(message);

        Assert.Equal(DataItemVersion.V6, enqueued.Version);
        Assert.Null(enqueued.TrackingId);
        Assert.Equal(body.Length + 16, enqueued.Data.Length);
        Assert.Equal(enqueued.Data.Length, message.GetMessageSizeInBytes());
        Assert.Equal((byte)message.MessageType, enqueued.Data[0]);
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, enqueued.Data[1..5]);
        Assert.Equal(body, enqueued.Data[5..(5 + body.Length)]);
        Assert.Equal(ticks, BinaryPrimitives.ReadInt64LittleEndian(enqueued.Data.AsSpan(5 + body.Length)));
        Assert.Equal(new byte[] { (byte)PartitionKey.Key16, (byte)MessageAction.Update, (byte)OmfVersion.Omf20 }, enqueued.Data[(13 + body.Length)..(16 + body.Length)]);

        Assert.True(queue.TryPeek(out var peeked));
        Assert.True(queue.TryDequeue(out var restored));
        Assert.Null(peeked.SerializedMessageId);
        Assert.Equal(ticks, peeked.ProcessTimeTicks);
        Assert.Null(restored.SerializedMessageId);
        Assert.Equal(ticks, restored.ProcessTimeTicks);
        Assert.Equal(body, restored.MessageBody);
        Assert.Equal(message.MessageType, restored.MessageType);
        Assert.Equal(message.ItemCount, restored.ItemCount);
        Assert.Equal(message.MessageAction, restored.MessageAction);
        Assert.Equal(message.OmfVersion, restored.OmfVersion);
        Assert.Equal(message.PartitionKey, restored.PartitionKey);

        // A failover message is also a writer message; the writer format doesn't store ticks.
        using var writerQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, persistentQueue.Object, null);
        writerQueue.Enqueue(restored);
        Assert.Equal(DataItemVersion.V3, enqueued.Version);
        Assert.True(writerQueue.TryDequeue(out var writerMessage));
        Assert.Null(writerMessage.SerializedMessageId);
        Assert.Equal(body, writerMessage.MessageBody);
        Assert.Equal(message.ItemCount, writerMessage.ItemCount);
        Assert.Equal(message.MessageAction, writerMessage.MessageAction);
        Assert.Equal(message.OmfVersion, writerMessage.OmfVersion);
        Assert.Equal(message.PartitionKey, writerMessage.PartitionKey);
    }

    /// <summary>
    /// Verifies raw V6 bytes, and V3 bytes in the identical layout written by earlier builds, load with their timestamp,
    /// partition, and OMF version, without inventing a scoped ID.
    /// </summary>
    [Theory]
    [InlineData(DataItemVersion.V3)]
    [InlineData(DataItemVersion.V6)]
    public void FailoverPersistentOmfMessageQueue_V3OrV6Record_PreservesMetadata(DataItemVersion version)
    {
        var payload = new byte[] { (byte)MessageType.Instance, 4, 3, 2, 1, 0x11, 0x22, 8, 7, 6, 5, 4, 3, 2, 1,
            (byte)PartitionKey.Key7, (byte)MessageAction.Update, (byte)OmfVersion.Omf20 };
        var persistentQueue = new Mock<IPersistentQueue>();
        using var queue = new FailoverMessageQueueWrapper(persistentQueue.Object);

        var restored = queue.CreateSerializedOmfMessageExternal(new DataItem(version, payload));

        Assert.Equal(MessageType.Instance, restored.MessageType);
        Assert.Equal(0x01020304, restored.ItemCount);
        Assert.Equal(new byte[] { 0x11, 0x22 }, restored.MessageBody);
        Assert.Equal(0x0102030405060708L, restored.ProcessTimeTicks);
        Assert.Equal(PartitionKey.Key7, restored.PartitionKey);
        Assert.Equal(MessageAction.Update, restored.MessageAction);
        Assert.Equal(OmfVersion.Omf20, restored.OmfVersion);
        Assert.Null(restored.SerializedMessageId);
        Assert.Equal(payload, queue.CreateDataItemExternal(restored).Data);
    }

    /// <summary>
    /// Verifies truncated records and unsupported record versions cannot be mistaken for failover bodies.
    /// </summary>
    [Theory]
    [InlineData(DataItemVersion.V6, 0)]
    [InlineData(DataItemVersion.V6, 15)]
    [InlineData(DataItemVersion.V3, 15)]
    [InlineData(DataItemVersion.V2, 13)]
    [InlineData(DataItemVersion.V4, 40)]
    [InlineData(DataItemVersion.V5, 48)]
    [InlineData(DataItemVersion.V1, 40)]
    public void FailoverPersistentOmfMessageQueue_InvalidRecord_ReturnsNull(DataItemVersion version, int length)
    {
        var persistentQueue = new Mock<IPersistentQueue>();
        using var queue = new FailoverMessageQueueWrapper(persistentQueue.Object);

        var restored = queue.CreateSerializedOmfMessageExternal(new DataItem(version, new byte[length]));

        Assert.Null(restored);
    }

    /// <summary>
    /// Verifies range deletes keep their body, action, protocol, partition, and process time through the V6 failover buffer.
    /// </summary>
    [Theory]
    [InlineData(AdapterFramework.Data.DataModel.OmfVersion.Omf12, null)]
    [InlineData(AdapterFramework.Data.DataModel.OmfVersion.Omf20, PartitionKey.Key7)]
    [InlineData(AdapterFramework.Data.DataModel.OmfVersion.Omf20, null)]
    public void RangeDelete_PreservesFailoverBufferMetadata(AdapterFramework.Data.DataModel.OmfVersion version, PartitionKey? partitionKey)
    {
        var original = new AdapterFramework.Data.Framework.Messages.SerializedOmfMessage(
            MessageType.Instance, "{\"streamingdata\":[]}"u8.ToArray(), MessageAction.Delete, 0, version, partitionKey);
        var message = new FailoverSerializedOmfMessage(original) { ProcessTimeTicks = 123456789 };
        DataItem stored = null;
        var queue = new Mock<IPersistentQueue>();
        queue.Setup(q => q.Enqueue(It.IsAny<DataItem>())).Callback<DataItem>(item => stored = item);
        queue.Setup(q => q.Dequeue()).Returns(() => stored);
        using var buffer = new FailoverPersistentOmfMessageQueue(TestTargetIdentifier, queue.Object, null);
        buffer.Enqueue(message);
        Assert.Equal(DataItemVersion.V6, stored.Version);
        Assert.Equal(message.GetMessageSizeInBytes(), stored.Data.Length);
        Assert.Equal(new[] { (byte)(partitionKey ?? 0), (byte)original.MessageAction, (byte)version }, stored.Data[^3..]);

        Assert.True(buffer.TryDequeue(out var restored));
        Assert.Equal(original.MessageBody, restored.MessageBody);
        Assert.Equal(original.MessageAction, restored.MessageAction);
        Assert.Equal(version, restored.OmfVersion);
        Assert.Equal(partitionKey, restored.PartitionKey);
        Assert.Equal(message.ProcessTimeTicks, restored.ProcessTimeTicks);
        Assert.Equal(0, restored.ItemCount);
    }

    [Fact]
    public void FailoverPersistentOmfMessageQueue_TryPeek_Test()
    {
        var dequeueCalled = false;
        var flushCalled = false;
        var expectedMessageType = MessageType.Container;
        var expectedMessageAction = MessageAction.Create;

        var expectedMessageBody = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var serializedMessage = new FailoverSerializedOmfMessage(expectedMessageType, expectedMessageBody, expectedMessageAction, int.MaxValue)
        {
            ProcessTimeTicks = long.MaxValue,
        };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        using var failoverQueueWrapper = new FailoverMessageQueueWrapper(mockPersistentQueue.Object);

        var dataItem = failoverQueueWrapper.CreateDataItemExternal(serializedMessage);

        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Peek()).Returns(dataItem);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new FailoverPersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.True(persistentOmfMessageQueue.TryPeek(out var peekedMessage));

        Assert.False(dequeueCalled);
        Assert.False(flushCalled);
        Assert.Equal(expectedMessageType, peekedMessage.MessageType);
        Assert.Equal(expectedMessageBody, peekedMessage.MessageBody);
        Assert.Equal(expectedMessageAction, peekedMessage.MessageAction);
        Assert.Equal(long.MaxValue, peekedMessage.ProcessTimeTicks);
        Assert.Equal(int.MaxValue, peekedMessage.ItemCount);
    }

    [Theory]
    [InlineData(DataItemVersion.V2, 1)]
    [InlineData(DataItemVersion.V3, 15)]
    [InlineData(DataItemVersion.V1, 20)]
    [InlineData((DataItemVersion)99, 20)]
    public void FailoverPersistentOmfMessageQueue_InvalidRecordsLogVersionAndLength(DataItemVersion version, int length)
    {
        var queue = new Mock<IPersistentQueue>();
        queue.Setup(q => q.Dequeue()).Returns(new DataItem(version, new byte[length]));
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);
        using var buffer = new FailoverPersistentOmfMessageQueue(TestTargetIdentifier, queue.Object, logger.Object);
        Assert.True(buffer.TryDequeue(out var message));
        Assert.Null(message);

        var log = Assert.Single(logger.Invocations, i => i.Method.Name == nameof(ILogger.Log));
        Assert.Equal(LogLevel.Warning, log.Arguments[0]);
        Assert.Equal(5100, ((EventId)log.Arguments[1]).Id);
        var properties = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object>>>(log.Arguments[2]).ToDictionary(p => p.Key, p => p.Value);
        Assert.Equal(version, properties["RecordVersion"]);
        Assert.Equal(length, properties["ByteLength"]);
        Assert.Equal(3, properties.Count); // Version, length, and the message template; no payload.
    }

    [Fact]
    public void FailoverPersistentOmfMessageQueue_TryDequeue_Test()
    {
        var dequeueCalled = false;
        var flushCalled = false;
        var expectedMessageType = MessageType.Container;
        var expectedMessageAction = MessageAction.Create;

        var expectedMessageBody = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var serializedMessage = new FailoverSerializedOmfMessage(expectedMessageType, expectedMessageBody, expectedMessageAction, int.MaxValue)
        {
            ProcessTimeTicks = long.MaxValue,
        };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        using var failoverQueueWrapper = new FailoverMessageQueueWrapper(mockPersistentQueue.Object);

        var dataItem = failoverQueueWrapper.CreateDataItemExternal(serializedMessage);

        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true).Returns(dataItem);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new FailoverPersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.True(persistentOmfMessageQueue.TryDequeue(out var dequeuedMessage));

        Assert.NotNull(dequeuedMessage);
        Assert.True(dequeueCalled);
        Assert.True(flushCalled);
        Assert.Equal(expectedMessageBody, dequeuedMessage.MessageBody);
        Assert.Equal(expectedMessageType, dequeuedMessage.MessageType);
        Assert.Equal(expectedMessageAction, dequeuedMessage.MessageAction);
        Assert.Equal(long.MaxValue, dequeuedMessage.ProcessTimeTicks);
        Assert.Equal(int.MaxValue, dequeuedMessage.ItemCount);
    }

    /// <summary>
    /// Verifies that V6 failover records retain the protocol, optional key, action, count,
    /// body, and process time, including empty bodies and boundary routing keys.
    /// </summary>
    /// <param name="partitionKey">The routing key to persist.</param>
    /// <param name="emptyBody">Whether the message body is empty.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData(PartitionKey.Key1, false)]
    [InlineData(PartitionKey.Key16, true)]
    public void CreateDataItem_WhenOmf20MessageIsPersisted_RoundTripsMetadata(PartitionKey? partitionKey, bool emptyBody)
    {
        var body = emptyBody ? Array.Empty<byte>() : new byte[] { 1, 2, 3, 4 };
        var message = new FailoverSerializedOmfMessage(MessageType.Schema, body, MessageAction.Delete,
            int.MaxValue, OmfVersion.Omf20, partitionKey) { ProcessTimeTicks = long.MaxValue };
        using var wrapper = new FailoverMessageQueueWrapper(new Mock<IPersistentQueue>().Object);

        var record = wrapper.CreateDataItemExternal(message);
        Assert.Equal(DataItemVersion.V6, record.Version);
        Assert.Equal(body.Length + 16, record.Data.Length);
        Assert.Equal(record.Data.Length, message.GetMessageSizeInBytes());
        Assert.Equal((byte)(partitionKey ?? 0), record.Data[^3]);
        Assert.Equal((byte)MessageAction.Delete, record.Data[^2]);
        Assert.Equal((byte)OmfVersion.Omf20, record.Data[^1]);

        var restored = wrapper.CreateSerializedOmfMessageExternal(record);
        Assert.Equal(message.MessageType, restored.MessageType);
        Assert.Equal(message.MessageAction, restored.MessageAction);
        Assert.Equal(message.ItemCount, restored.ItemCount);
        Assert.Equal(message.ProcessTimeTicks, restored.ProcessTimeTicks);
        Assert.Equal(body, restored.MessageBody);
        Assert.Equal(OmfVersion.Omf20, restored.OmfVersion);
        Assert.Equal(partitionKey, restored.PartitionKey);
    }

    /// <summary>
    /// Verifies that a legacy V2 record still restores its body and timestamp with OMF 1.2
    /// and no partition key, independently of the current writer.
    /// </summary>
    [Fact]
    public void CreateSerializedOmfMessage_WhenLegacyV2RecordIsRead_UsesOriginalDefaults()
    {
        var body = new byte[] { 10, 20, 30 };
        var bytes = new byte[body.Length + 14];
        bytes[0] = (byte)MessageType.Container;
        BitConverter.GetBytes(42).CopyTo(bytes, 1);
        body.CopyTo(bytes, 5);
        BitConverter.GetBytes(123456789L).CopyTo(bytes, 5 + body.Length);
        bytes[^1] = (byte)MessageAction.Update;
        using var wrapper = new FailoverMessageQueueWrapper(new Mock<IPersistentQueue>().Object);

        var restored = wrapper.CreateSerializedOmfMessageExternal(new DataItem(DataItemVersion.V2, bytes));
        Assert.Equal(MessageType.Container, restored.MessageType);
        Assert.Equal(MessageAction.Update, restored.MessageAction);
        Assert.Equal(42, restored.ItemCount);
        Assert.Equal(123456789L, restored.ProcessTimeTicks);
        Assert.Equal(body, restored.MessageBody);
        Assert.Equal(OmfVersion.Omf12, restored.OmfVersion);
        Assert.Null(restored.PartitionKey);
    }

    [Theory]
    [InlineData(MessageType.Type, MessageAction.Create, 100)]
    [InlineData(MessageType.Container, MessageAction.Create, 100)]
    [InlineData(MessageType.Container, MessageAction.Update, 100)]
    [InlineData(MessageType.Container, MessageAction.Delete, 100)]
    [InlineData(MessageType.Data, MessageAction.Create, 1)]
    [InlineData(MessageType.Data, MessageAction.Update, 10000)]
    public void FailoverPersistentOmfMessageQueue_Enqueue_TryDequeue_Test(MessageType messageType, MessageAction messageAction, int valueCount)
    {
        var messageBody = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var expectedProcessedTime = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(10));

        var serializedMessage = new FailoverSerializedOmfMessage(messageType, messageBody, messageAction, valueCount)
        {
            ProcessTimeTicks = expectedProcessedTime.Ticks,
        };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        using var failoverQueueWrapper = new FailoverMessageQueueWrapper(mockPersistentQueue.Object);

        var dataItem = failoverQueueWrapper.CreateDataItemExternal(serializedMessage);

        Assert.Equal(DataItemVersion.V6, dataItem.Version);
        Assert.NotNull(dataItem.Data);

        var retreivedMessage = failoverQueueWrapper.CreateSerializedOmfMessageExternal(dataItem);

        Assert.Equal(valueCount, retreivedMessage.ItemCount);
        Assert.Equal(messageType, retreivedMessage.MessageType);
        Assert.Equal(messageAction, retreivedMessage.MessageAction);
        Assert.Equal(expectedProcessedTime.Ticks, retreivedMessage.ProcessTimeTicks);
        Assert.Equal(messageBody, retreivedMessage.MessageBody);
        Assert.Equal(OmfVersion.Omf12, retreivedMessage.OmfVersion);
        Assert.Null(retreivedMessage.PartitionKey);
    }

    [Theory]
    [InlineData(MessageType.Schema, MessageAction.Create, null)]
    [InlineData(MessageType.Instance, MessageAction.Update, PartitionKey.Key1)]
    [InlineData(MessageType.Instance, MessageAction.Delete, PartitionKey.Key16)]
    public void FailoverPersistentOmfMessageQueue_Omf20_RoundTrip_PreservesVersionAndPartitionKey(MessageType messageType, MessageAction messageAction, PartitionKey? partitionKey)
    {
        var messageBody = new byte[] { 0x7b, 0x7d };
        var processTimeTicks = DateTime.UtcNow.Ticks;
        var serializedMessage = new FailoverSerializedOmfMessage(messageType, messageBody, messageAction, 7, OmfVersion.Omf20, partitionKey)
        {
            ProcessTimeTicks = processTimeTicks,
        };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        using var failoverQueueWrapper = new FailoverMessageQueueWrapper(mockPersistentQueue.Object);

        var dataItem = failoverQueueWrapper.CreateDataItemExternal(serializedMessage);
        var retrievedMessage = failoverQueueWrapper.CreateSerializedOmfMessageExternal(dataItem);

        Assert.Equal(DataItemVersion.V6, dataItem.Version);
        Assert.Equal(serializedMessage.GetMessageSizeInBytes(), dataItem.Data.Length);
        Assert.Equal(messageType, retrievedMessage.MessageType);
        Assert.Equal(messageAction, retrievedMessage.MessageAction);
        Assert.Equal(7, retrievedMessage.ItemCount);
        Assert.Equal(messageBody, retrievedMessage.MessageBody);
        Assert.Equal(processTimeTicks, retrievedMessage.ProcessTimeTicks);
        Assert.Equal(OmfVersion.Omf20, retrievedMessage.OmfVersion);
        Assert.Equal(partitionKey, retrievedMessage.PartitionKey);
    }

    [Fact]
    public void FailoverPersistentOmfMessageQueue_LegacyV2Record_LoadsAsOmf12WithoutPartitionKey()
    {
        var messageBody = new byte[] { 0x01, 0x02, 0x03 };
        var processTimeTicks = DateTime.UtcNow.Ticks;
        var data = new byte[MessageTypeEnumSize + ValueCountSize + messageBody.Length + sizeof(long) + MessageActionEnumSize];
        data[0] = (byte)MessageType.Container;
        BitConverter.GetBytes(5).CopyTo(data, MessageTypeEnumSize);
        messageBody.CopyTo(data, MessageTypeEnumSize + ValueCountSize);
        BitConverter.GetBytes(processTimeTicks).CopyTo(data, MessageTypeEnumSize + ValueCountSize + messageBody.Length);
        data[^1] = (byte)MessageAction.Update;

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        using var failoverQueueWrapper = new FailoverMessageQueueWrapper(mockPersistentQueue.Object);

        var retrievedMessage = failoverQueueWrapper.CreateSerializedOmfMessageExternal(new DataItem(DataItemVersion.V2, data));

        Assert.Equal(MessageType.Container, retrievedMessage.MessageType);
        Assert.Equal(MessageAction.Update, retrievedMessage.MessageAction);
        Assert.Equal(5, retrievedMessage.ItemCount);
        Assert.Equal(messageBody, retrievedMessage.MessageBody);
        Assert.Equal(processTimeTicks, retrievedMessage.ProcessTimeTicks);
        Assert.Equal(OmfVersion.Omf12, retrievedMessage.OmfVersion);
        Assert.Null(retrievedMessage.PartitionKey);
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal class FailoverMessageQueueWrapper : FailoverPersistentOmfMessageQueue
#pragma warning restore SA1402 // File may only contain a single type
{
    public FailoverMessageQueueWrapper(IPersistentQueue persistentQueue)
        : base("Test", persistentQueue, null)
    {
    }

    public DataItem CreateDataItemExternal(IFailoverSerializedOmfMessage message)
    {
        return CreateDataItem(message);
    }

    public IFailoverSerializedOmfMessage CreateSerializedOmfMessageExternal(DataItem dataItem)
    {
        return CreateSerializedOmfMessage(dataItem);
    }
}
