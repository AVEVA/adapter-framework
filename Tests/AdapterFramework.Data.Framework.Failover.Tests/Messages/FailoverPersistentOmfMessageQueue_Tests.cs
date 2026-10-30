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

    [Theory]
    [InlineData(AdapterFramework.Data.DataModel.OmfVersion.Omf12, null, DataItemVersion.V2)]
    [InlineData(AdapterFramework.Data.DataModel.OmfVersion.Omf20, PartitionKey.Key7, DataItemVersion.V3)]
    [InlineData(AdapterFramework.Data.DataModel.OmfVersion.Omf20, null, DataItemVersion.V3)]
    public void RangeDelete_PreservesFailoverBufferMetadata(AdapterFramework.Data.DataModel.OmfVersion version, PartitionKey? partitionKey, DataItemVersion recordVersion)
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
        Assert.Equal(recordVersion, stored.Version);
        Assert.Equal(message.GetMessageSizeInBytes(), stored.Data.Length);
        if (recordVersion == DataItemVersion.V3)
        {
            Assert.Equal(new[] { (byte)(partitionKey ?? 0), (byte)original.MessageAction, (byte)version }, stored.Data[^3..]);
        }

        Assert.True(buffer.TryDequeue(out var restored));
        Assert.Equal(original.MessageBody, restored.MessageBody);
        Assert.Equal(original.MessageAction, restored.MessageAction);
        Assert.Equal(version, restored.OmfVersion);
        Assert.Equal(partitionKey, restored.PartitionKey);
        Assert.Equal(message.ProcessTimeTicks, restored.ProcessTimeTicks);
        Assert.Equal(0, restored.ItemCount);
    }

    [Fact]
    public void FailoverPersistentOmfMessageQueue_TruncatedRecordsReturnEmptyMessage()
    {
        using var buffer = new FailoverMessageQueueWrapper(new Mock<IPersistentQueue>().Object);
        foreach (var version in new[] { DataItemVersion.V2, DataItemVersion.V3 })
        {
            // Type, value count, process ticks, action, and optional partition/version metadata.
            var minimumLength = 1 + sizeof(int) + sizeof(long) + 1 + (version == DataItemVersion.V3 ? 2 : 0);
            for (var length = 0; length < minimumLength; length++)
            {
                var restored = buffer.CreateSerializedOmfMessageExternal(new DataItem(version, new byte[length]));
                Assert.Empty(restored.MessageBody);
                Assert.Equal(0, restored.ItemCount);
                Assert.Equal(0, restored.ProcessTimeTicks);
            }
        }
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
