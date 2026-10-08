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
using static AdapterFramework.Data.Framework.Messages.SerializedOmfMessage;
using MessageType = AdapterFramework.Data.DataModel.MessageType;
using OmfVersion = AdapterFramework.Data.DataModel.OmfVersion;

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
