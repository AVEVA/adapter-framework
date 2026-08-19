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
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Buffering.Tests;

public class BackedUpOmfMessageQueue_Tests
{
    private const int MaxQueueSize = 10;
    private readonly TimeSpan _messageExpirationTime = TimeSpan.FromSeconds(5);

    [Fact]
    public void BackedUpOmfMessageQueue_Constructor_Test()
    {
        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();

        using var backedUpMessageQueueWithoutPersistence = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, null);
        Assert.NotNull(backedUpMessageQueueWithoutPersistence);

        using var backedUpMessageQueueWithPersistence = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, null);
        Assert.NotNull(backedUpMessageQueueWithPersistence);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void BackedUpOmfMessageQueue_Enqueue_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<ISerializedOmfMessage>()))
            .Callback(() => enqueuedToPersistentQueue = true);

        var messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, null);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.False(enqueuedToPersistentQueue);
        Assert.True(backedUpMessageQueue.TryPeek(out var message));
        Assert.Equal(messageToEnqueue.MessageType, message.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, message.MessageBody);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void BackedUpOmfMessageQueue_TryPeek_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<ISerializedOmfMessage>()))
            .Callback(() => enqueuedToPersistentQueue = true);

        var messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, null);

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
    public void BackedUpOmfMessageQueue_TryPeek_PersistentQueue_Null_Test(MessageType messageType)
    {
        var messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);
        var testLogger = new TestLogger();

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, testLogger);

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
    public void BackedUpOmfMessageQueue_TryDequeue_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<ISerializedOmfMessage>()))
            .Callback(() => enqueuedToPersistentQueue = true);

        var messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, null);

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
    public void BackedUpOmfMessageQueue_TryDequeue_PersistentQueue_Null_Test(MessageType messageType)
    {
        var messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, null);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        Assert.True(backedUpMessageQueue.TryDequeue(out var message));
        Assert.Equal(messageToEnqueue.MessageType, message.MessageType);
        Assert.Equal(messageToEnqueue.MessageBody, message.MessageBody);
        Assert.False(backedUpMessageQueue.TryDequeue(out _));
    }

    [Fact]
    public void BackedUpOmfMessageQueue_Clear_Test()
    {
        var clearCalled = false;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Clear())
            .Callback(() => clearCalled = true);

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, mockPersistentQueue.Object, null);

        backedUpMessageQueue.Clear();

        Assert.True(clearCalled);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_Clear_PersistentQueue_Null_Test()
    {
        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, _messageExpirationTime, null, null);

        // this checks if Clear does not throw
        backedUpMessageQueue.Clear();
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void BackedUpOmfMessageQueue_MessageExpired_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;
        var dequeuedFromPersistentQueue = false;
        ISerializedOmfMessage messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);
        ISerializedOmfMessage expiredMessage = null;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.TryDequeue(out messageToEnqueue)).Returns(true)
            .Callback(() => dequeuedFromPersistentQueue = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<ISerializedOmfMessage>()))
            .Callback((ISerializedOmfMessage message) =>
            {
                enqueuedToPersistentQueue = true;
                expiredMessage = message;
            });

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, TimeSpan.FromSeconds(2), mockPersistentQueue.Object, null);

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
    public async Task BackedUpOmfMessageQueue_MessageExpired_PersistentQueue_Null_Test(MessageType messageType)
    {
        var messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        var testLogger = new TestLogger();

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(MaxQueueSize, TimeSpan.FromSeconds(2), null, testLogger);

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
    public void BackedUpOmfMessageQueue_SizeLimitReached_Test(MessageType messageType)
    {
        var enqueuedToPersistentQueue = false;
        var dequeuedFromPersistentQueue = false;
        ISerializedOmfMessage messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);
        ISerializedOmfMessage expiredMessage = null;

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.TryDequeue(out messageToEnqueue)).Returns(true)
            .Callback(() => dequeuedFromPersistentQueue = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<ISerializedOmfMessage>()))
            .Callback((ISerializedOmfMessage message) =>
            {
                enqueuedToPersistentQueue = true;
                expiredMessage = message;
            });

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(0, TimeSpan.FromSeconds(10), mockPersistentQueue.Object, null);

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
    public void BackedUpOmfMessageQueue_SizeLimitReached_PersistentQueue_Null_Test(MessageType messageType)
    {
        var testLogger = new TestLogger();
        var messageToEnqueue = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        using var backedUpMessageQueue = new BackedUpOmfMessageQueue(0, TimeSpan.FromSeconds(10), null, testLogger);

        backedUpMessageQueue.Enqueue(messageToEnqueue);

        // make sure that the message was discarded
        Assert.False(backedUpMessageQueue.TryPeek(out _));
        Assert.False(backedUpMessageQueue.TryDequeue(out _));

        var logMessages = testLogger.GetLogMessages();

        Assert.Single(logMessages);
        Assert.Equal(BackedUpOmfMessageQueue.MaxBufferSizeReachedMessage, logMessages[0].LogMessage);
        Assert.Equal(LogLevel.Debug, logMessages[0].LogLevel);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_Dispose_Test()
    {
        var persistentQueueDisposeCalled = false;
        var enqueuedToPersistentQueue = false;
        ISerializedOmfMessage flushedMessage = null;

        var messageToEnqueue = new SerializedOmfMessage(MessageType.Data, new byte[] { 0x20 }, MessageAction.Default);

        var mockPersistentQueue = new Mock<IPersistentMessageQueue<ISerializedOmfMessage>>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dispose())
            .Callback(() => persistentQueueDisposeCalled = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<ISerializedOmfMessage>()))
            .Callback((ISerializedOmfMessage message) =>
            {
                enqueuedToPersistentQueue = true;
                flushedMessage = message;
            });

        var backedUpMessageQueue = new BackedUpOmfMessageQueue(0, TimeSpan.FromSeconds(10), mockPersistentQueue.Object, null);

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
