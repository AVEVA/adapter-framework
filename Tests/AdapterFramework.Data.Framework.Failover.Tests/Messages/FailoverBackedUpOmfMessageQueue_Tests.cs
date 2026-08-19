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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Buffering;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Failover.Messages;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests.Messages;

public class FailoverBackedUpOmfMessageQueue_Tests
{
    private const int MaxQueueSize = 10;
    private readonly TimeSpan _messageExpirationTime = TimeSpan.FromSeconds(5);

    [Fact]
    public void FailoverBackedUpOmfMessageQueue_Constructor_Test()
    {
        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();

        using var backedUpMessageQueueWithoutPersistence = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, new Mock<ILogger>().Object);
        Assert.NotNull(backedUpMessageQueueWithoutPersistence);

        using var backedUpMessageQueueWithPersistence = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, new Mock<ILogger>().Object);
        Assert.NotNull(backedUpMessageQueueWithPersistence);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_Enqueue_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<IFailoverSerializedOmfMessage>()))
            .Callback(() => enqueuedToPersistentQueue = true);

        var messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.False(enqueuedToPersistentQueue);
        Assert.True(backedUpMessageQueue.TryPeek(out var message));
        Assert.Equal(messageToEnqueue.MessageType, message.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, message.MessageBody);
        Assert.Equal(messageToEnqueue.MessageAction, message.MessageAction);
        Assert.Equal(messageToEnqueue.ItemCount, message.ItemCount);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_TryPeek_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<IFailoverSerializedOmfMessage>()))
            .Callback(() => enqueuedToPersistentQueue = true);

        var messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.False(enqueuedToPersistentQueue);
        Assert.True(backedUpMessageQueue.TryPeek(out var firstPeekedMessage));
        Assert.Equal(messageToEnqueue.MessageType, firstPeekedMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, firstPeekedMessage.MessageBody);
        Assert.True(backedUpMessageQueue.TryPeek(out var secondPeekMessage));
        Assert.Equal(messageToEnqueue.MessageType, secondPeekMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, secondPeekMessage.MessageBody);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_TryPeek_PersistentQueue_Null_Test(MessageType messageType)
    {
        var messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.True(backedUpMessageQueue.TryPeek(out var firstPeekedMessage));
        Assert.Equal(messageToEnqueue.MessageType, firstPeekedMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, firstPeekedMessage.MessageBody);
        Assert.True(backedUpMessageQueue.TryPeek(out var secondPeekMessage));
        Assert.Equal(messageToEnqueue.MessageType, secondPeekMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, secondPeekMessage.MessageBody);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_TryDequeue_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<IFailoverSerializedOmfMessage>()))
            .Callback(() => enqueuedToPersistentQueue = true);

        var messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.False(enqueuedToPersistentQueue);
        Assert.True(backedUpMessageQueue.TryDequeue(out var message));
        Assert.Equal(messageToEnqueue.MessageType, message.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, message.MessageBody);
        Assert.False(backedUpMessageQueue.TryDequeue(out _));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_TryDequeue_PersistentQueue_Null_Test(MessageType messageType)
    {
        var messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.True(backedUpMessageQueue.TryDequeue(out var message));
        Assert.Equal(messageToEnqueue.MessageType, message.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, message.MessageBody);
        Assert.False(backedUpMessageQueue.TryDequeue(out _));
    }

    [Fact]
    public void FailoverBackedUpOmfMessageQueue_Clear_Test()
    {
        var clearCalled = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Clear())
            .Callback(() => clearCalled = true);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, new Mock<ILogger>().Object);

        backedUpMessageQueue.Clear();

        Assert.True(clearCalled);
    }

    [Fact]
    public void FailoverBackedUpOmfMessageQueue_Clear_PersistentQueue_Null_Test()
    {
        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, new Mock<ILogger>().Object);

        // this checks if Clear does not throw
        backedUpMessageQueue.Clear();
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_MessageExpired_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;
        var dequeuedFromPersistentQueue = false;
        IFailoverSerializedOmfMessage messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);
        IFailoverSerializedOmfMessage expiredMessage = null;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.TryDequeue(out messageToEnqueue)).Returns(true)
            .Callback(() => dequeuedFromPersistentQueue = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<IFailoverSerializedOmfMessage>()))
            .Callback((IFailoverSerializedOmfMessage message) =>
            {
                enqueuedToPersistentQueue = true;
                expiredMessage = message;
            });

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, TimeSpan.FromSeconds(2), mockPersistentQueue.Object, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.True(SpinWait.SpinUntil(() => enqueuedToPersistentQueue, 3000));
        Assert.True(backedUpMessageQueue.TryDequeue(out var receivedMessage));
        Assert.True(dequeuedFromPersistentQueue);
        Assert.Equal(messageToEnqueue.MessageType, receivedMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, receivedMessage.MessageBody);
        Assert.NotNull(expiredMessage);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public async Task FailoverBackedUpOmfMessageQueue_MessageExpired_PersistentQueue_Null_Test(MessageType messageType)
    {
        var messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(MaxQueueSize, TimeSpan.FromSeconds(2), null, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        await Task.Delay(3000);

        // make sure that the message wasn't discarded
        Assert.True(backedUpMessageQueue.TryPeek(out var receivedMessage));
        Assert.Equal(messageToEnqueue.MessageType, receivedMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, receivedMessage.MessageBody);
        Assert.True(backedUpMessageQueue.TryPeek(out _));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_SizeLimitReached_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;
        var dequeuedFromPersistentQueue = false;
        IFailoverSerializedOmfMessage messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);
        IFailoverSerializedOmfMessage expiredMessage = null;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.TryDequeue(out messageToEnqueue)).Returns(true)
            .Callback(() => dequeuedFromPersistentQueue = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<IFailoverSerializedOmfMessage>()))
            .Callback((IFailoverSerializedOmfMessage message) =>
            {
                enqueuedToPersistentQueue = true;
                expiredMessage = message;
            });

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(0, TimeSpan.FromSeconds(10), mockPersistentQueue.Object, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.True(enqueuedToPersistentQueue);
        Assert.True(backedUpMessageQueue.TryDequeue(out var receivedMessage));
        Assert.True(dequeuedFromPersistentQueue);
        Assert.Equal(messageToEnqueue.MessageType, receivedMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, receivedMessage.MessageBody);
        Assert.NotNull(expiredMessage);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void FailoverBackedUpOmfMessageQueue_SizeLimitReached_PersistentQueue_Null_Test(MessageType messageType)
    {
        var messageToEnqueue = new FailoverSerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(0, TimeSpan.FromSeconds(10), null, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        // make sure that the message was discarded
        Assert.False(backedUpMessageQueue.TryPeek(out _));
        Assert.False(backedUpMessageQueue.TryDequeue(out _));
    }

    [Fact]
    public void FailoverBackedUpOmfMessageQueue_Dispose_Test()
    {
        var persistentQueueDisposeCalled = false;
        var enqueuedToPersistentQueue = false;
        IFailoverSerializedOmfMessage flushedMessage = null;

        var messageToEnqueue = new FailoverSerializedOmfMessage(MessageType.Data, new byte[] { 0x20 }, MessageAction.Default);

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<IFailoverSerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dispose())
            .Callback(() => persistentQueueDisposeCalled = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<IFailoverSerializedOmfMessage>()))
            .Callback((IFailoverSerializedOmfMessage message) =>
            {
                enqueuedToPersistentQueue = true;
                flushedMessage = message;
            });

        var backedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(0, TimeSpan.FromSeconds(10), mockPersistentQueue.Object, new Mock<ILogger>().Object);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        backedUpMessageQueue.Dispose();

        Assert.True(persistentQueueDisposeCalled);
        Assert.True(enqueuedToPersistentQueue);
        Assert.Equal(messageToEnqueue.MessageType, flushedMessage.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, flushedMessage.MessageBody);

        // make sure second dispose call doesn't throw
        backedUpMessageQueue.Dispose();
    }
}
