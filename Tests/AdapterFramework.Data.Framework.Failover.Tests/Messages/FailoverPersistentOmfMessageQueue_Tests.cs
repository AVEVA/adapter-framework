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
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Failover.Messages;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;
using static AdapterFramework.Data.Framework.Messages.SerializedOmfMessage;
using MessageType = AdapterFramework.Data.DataModel.MessageType;
using OmfVersion = AdapterFramework.Data.DataModel.OmfVersion;

namespace AdapterFramework.Data.Framework.Failover.Tests.Messages;

public class FailoverPersistentOmfMessageQueue_Tests
{
    private const string TestTargetIdentifier = "http://localhost:5590/omf";

    /// <summary>
    /// Verifies failover bodies always store ticks exactly once, append IDs only in V4,
    /// and preserve all metadata across persistence and conversion to a writer message.
    /// </summary>
    [Theory]
    [InlineData(false, 0L, false)]
    [InlineData(false, long.MaxValue, true)]
    [InlineData(true, 0L, true)]
    [InlineData(true, long.MinValue, false)]
    public void FailoverPersistentOmfMessageQueue_RoundTrip_PreservesLayoutAndWriterConversion(bool scoped, long ticks, bool emptyBody)
    {
        Guid? id = scoped ? Guid.NewGuid() : null;
        var body = emptyBody ? Array.Empty<byte>() : new byte[] { 0x11, 0x22, 0x33 };
        var message = new FailoverSerializedOmfMessage(MessageType.Instance, body, MessageAction.Update, 0x01020304, OmfVersion.Omf20, PartitionKey.Key16)
        {
            SerializedMessageId = id,
            ProcessTimeTicks = ticks,
        };
        DataItem enqueued = null;
        var persistentQueue = new Mock<IPersistentQueue>();
        persistentQueue.Setup(q => q.Enqueue(It.IsAny<DataItem>())).Callback((DataItem item) => enqueued = item);
        persistentQueue.Setup(q => q.Peek()).Returns(() => new DataItem(enqueued.Version, enqueued.Data));
        persistentQueue.Setup(q => q.Dequeue()).Returns(() => new DataItem(enqueued.Version, enqueued.Data));
        using var queue = new FailoverPersistentOmfMessageQueue(TestTargetIdentifier, persistentQueue.Object, null);

        queue.Enqueue(message);

        var idLength = scoped ? 16 : 0;
        Assert.Equal(scoped ? DataItemVersion.V4 : DataItemVersion.V3, enqueued.Version);
        Assert.Equal(id, enqueued.TrackingId);
        Assert.Equal(body.Length + 16 + idLength, enqueued.Data.Length);
        Assert.Equal(enqueued.Data.Length, message.GetMessageSizeInBytes());
        Assert.Equal((byte)message.MessageType, enqueued.Data[0]);
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, enqueued.Data[1..5]);
        Assert.Equal(body, enqueued.Data[5..(5 + body.Length)]);
        Assert.Equal(ticks, BinaryPrimitives.ReadInt64LittleEndian(enqueued.Data.AsSpan(5 + body.Length)));
        Assert.Equal(new byte[] { (byte)PartitionKey.Key16, (byte)MessageAction.Update, (byte)OmfVersion.Omf20 }, enqueued.Data[(13 + body.Length)..(16 + body.Length)]);
        if (scoped)
        {
            Assert.Equal(id, new Guid(enqueued.Data.AsSpan(enqueued.Data.Length - 16)));
        }

        Assert.True(queue.TryPeek(out var peeked));
        Assert.True(queue.TryDequeue(out var restored));
        Assert.Equal(id, peeked.SerializedMessageId);
        Assert.Equal(ticks, peeked.ProcessTimeTicks);
        Assert.Equal(id, restored.SerializedMessageId);
        Assert.Equal(ticks, restored.ProcessTimeTicks);
        Assert.Equal(body, restored.MessageBody);
        Assert.Equal(message.MessageType, restored.MessageType);
        Assert.Equal(message.ItemCount, restored.ItemCount);
        Assert.Equal(message.MessageAction, restored.MessageAction);
        Assert.Equal(message.OmfVersion, restored.OmfVersion);
        Assert.Equal(message.PartitionKey, restored.PartitionKey);

        // A failover message is also a writer message; the writer format has a different ticks position.
        using var writerQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, persistentQueue.Object, null);
        writerQueue.Enqueue(restored);
        Assert.Equal(ticks != 0 ? DataItemVersion.V5 : scoped ? DataItemVersion.V4 : DataItemVersion.V3, enqueued.Version);
        Assert.True(writerQueue.TryDequeue(out var writerMessage));
        Assert.Equal(id, writerMessage.SerializedMessageId);
        Assert.Equal(ticks, writerMessage.ProcessTimeTicks);
        Assert.Equal(body, writerMessage.MessageBody);
        Assert.Equal(message.ItemCount, writerMessage.ItemCount);
        Assert.Equal(message.MessageAction, writerMessage.MessageAction);
        Assert.Equal(message.OmfVersion, writerMessage.OmfVersion);
        Assert.Equal(message.PartitionKey, writerMessage.PartitionKey);
    }

    /// <summary>
    /// Verifies raw legacy V3 bytes load with their timestamp, partition, and OMF version, without inventing a scoped ID.
    /// </summary>
    [Fact]
    public void FailoverPersistentOmfMessageQueue_LegacyV3Record_PreservesMetadata()
    {
        var payload = new byte[] { (byte)MessageType.Instance, 4, 3, 2, 1, 0x11, 0x22, 8, 7, 6, 5, 4, 3, 2, 1,
            (byte)PartitionKey.Key7, (byte)MessageAction.Update, (byte)OmfVersion.Omf20 };
        var persistentQueue = new Mock<IPersistentQueue>();
        using var queue = new FailoverMessageQueueWrapper(persistentQueue.Object);

        var restored = queue.CreateSerializedOmfMessageExternal(new DataItem(DataItemVersion.V3, payload));

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
    /// Verifies incomplete scoped trailers and unsupported writer V5 records cannot be mistaken for legacy failover bodies.
    /// </summary>
    [Theory]
    [InlineData(DataItemVersion.V4, 15)]
    [InlineData(DataItemVersion.V4, 16)]
    [InlineData(DataItemVersion.V4, 31)]
    [InlineData(DataItemVersion.V5, 40)]
    public void FailoverPersistentOmfMessageQueue_InvalidRecord_ReturnsEmptyMessage(DataItemVersion version, int length)
    {
        var persistentQueue = new Mock<IPersistentQueue>();
        using var queue = new FailoverMessageQueueWrapper(persistentQueue.Object);

        var restored = queue.CreateSerializedOmfMessageExternal(new DataItem(version, new byte[length]));

        Assert.Empty(restored.MessageBody);
        Assert.Null(restored.SerializedMessageId);
        Assert.Equal(0, restored.ProcessTimeTicks);
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

        Assert.Equal(DataItemVersion.V3, dataItem.Version);
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

        Assert.Equal(DataItemVersion.V3, dataItem.Version);
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
