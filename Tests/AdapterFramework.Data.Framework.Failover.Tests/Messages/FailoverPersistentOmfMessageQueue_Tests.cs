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
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Failover.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;
using MessageType = AdapterFramework.Data.DataModel.MessageType;

namespace AdapterFramework.Data.Framework.Failover.Tests.Messages;

public class FailoverPersistentOmfMessageQueue_Tests
{
    private const string TestTargetIdentifier = "http://localhost:5590/omf";

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

    /// <summary>
    /// Verifies that V3 failover records retain the protocol, optional key, action, count,
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
        Assert.Equal(DataItemVersion.V3, record.Version);
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

        Assert.Equal(DataItemVersion.V2, dataItem.Version);
        Assert.NotNull(dataItem.Data);

        var retreivedMessage = failoverQueueWrapper.CreateSerializedOmfMessageExternal(dataItem);

        Assert.Equal(valueCount, retreivedMessage.ItemCount);
        Assert.Equal(messageType, retreivedMessage.MessageType);
        Assert.Equal(messageAction, retreivedMessage.MessageAction);
        Assert.Equal(expectedProcessedTime.Ticks, retreivedMessage.ProcessTimeTicks);
        Assert.Equal(messageBody, retreivedMessage.MessageBody);
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
