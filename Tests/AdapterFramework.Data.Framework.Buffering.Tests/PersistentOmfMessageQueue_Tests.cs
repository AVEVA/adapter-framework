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
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Buffering.Tests;

public class PersistentOmfMessageQueue_Tests
{
    private const string TestTargetIdentifier = "http://localhost:5590/omf";

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(TestTargetIdentifier, null)]
    public void PersistentOmfMessageQueue_Constructor_InvalidInput_Test(string targetIdentifier, IPersistentQueue persistentQueue)
    {
        Assert.ThrowsAny<Exception>(() => new PersistentOmfMessageQueue(targetIdentifier, persistentQueue, new Mock<ILogger>().Object));
    }

    [Fact]
    public void PersistentOmfMessageQueue_Constructor_ValidInput_Test()
    {
        var mockPersistentQueue = new Mock<IPersistentQueue>();

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);

        Assert.NotNull(persistentOmfMessageQueue);
    }

    [Theory]
    [InlineData(MessageType.Type, MessageAction.Default, OmfVersion.Omf12)]
    [InlineData(MessageType.Type, MessageAction.Create, OmfVersion.Omf12)]
    [InlineData(MessageType.Type, MessageAction.Update, OmfVersion.Omf13)]
    [InlineData(MessageType.Type, MessageAction.Delete, OmfVersion.Omf13)]
    [InlineData(MessageType.Container, MessageAction.Update, OmfVersion.Omf12)]
    [InlineData(MessageType.Container, MessageAction.Create, OmfVersion.Omf12)]
    [InlineData(MessageType.Container, MessageAction.Delete, OmfVersion.Omf12)]
    [InlineData(MessageType.Data, MessageAction.Delete, OmfVersion.Omf12)]
    [InlineData(MessageType.Instance, MessageAction.Update, OmfVersion.Omf20)]
    [InlineData(MessageType.Schema, MessageAction.Create, OmfVersion.Omf20)]
    [InlineData(MessageType.Data, MessageAction.Create, OmfVersion.Omf20)]
    [InlineData(MessageType.Data, MessageAction.Create, OmfVersion.Omf12, PartitionKey.Key1)]
    [InlineData(MessageType.Data, MessageAction.Create, OmfVersion.Omf20, PartitionKey.Key7)]
    [InlineData(MessageType.Instance, MessageAction.Create, OmfVersion.Omf20, PartitionKey.Key16)]
    public void PersistentOmfMessageQueue_Enqueue_Test(MessageType messageType, MessageAction messageAction, OmfVersion omfVersion, PartitionKey? partitionKey = null)
    {
        var messageEnqueued = false;
        var flushEnqueuesCalled = false;
        DataItem enqueuedDataItem = null;

        var serializedOmfMessage = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, messageAction, 1, omfVersion, partitionKey);
        var mockPersistentQueue = new Mock<IPersistentQueue>();

        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<DataItem>()))
            .Callback((DataItem dataItem) =>
            {
                enqueuedDataItem = dataItem;
                messageEnqueued = true;
            });
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushEnqueues())
            .Callback(() => flushEnqueuesCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        persistentOmfMessageQueue.Enqueue(serializedOmfMessage);

        Assert.True(messageEnqueued);
        Assert.True(flushEnqueuesCalled);
        Assert.NotNull(enqueuedDataItem);
        Assert.Equal(DataItemVersion.V3, enqueuedDataItem.Version);
        Assert.Equal(serializedOmfMessage.GetMessageSizeInBytes(), enqueuedDataItem.Data.Length);
        Assert.Equal(serializedOmfMessage.MessageBody[0], enqueuedDataItem.Data[5]);
        Assert.Equal(serializedOmfMessage.MessageType, (MessageType)enqueuedDataItem.Data[0]);
        Assert.Equal(serializedOmfMessage.PartitionKey, partitionKey == null ? null : (PartitionKey)enqueuedDataItem.Data[^3]);
        Assert.Equal(serializedOmfMessage.MessageAction, (MessageAction)enqueuedDataItem.Data[^2]);
        Assert.Equal(serializedOmfMessage.OmfVersion, (OmfVersion)enqueuedDataItem.Data[^1]);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void PersistentOmfMessageQueue_Enqueue_Throws_Test(MessageType messageType)
    {
        var testLogger = new TestLogger();
        var expectedMessage = $"Error flushing {messageType} to disk in buffer for http://localhost:5590/omf.";

        var serializedOmfMessage = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default);

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<DataItem>()));
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushEnqueues())
            .Throws(new IOException("UnitTest is out of space!"));

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, testLogger);
        persistentOmfMessageQueue.Enqueue(serializedOmfMessage);

        var logMessages = testLogger.GetLogMessages();

        Assert.Single(logMessages);
        Assert.Equal(expectedMessage, logMessages[0].LogMessage);
        Assert.Equal(LogLevel.Error, logMessages[0].LogLevel);
    }

    [Fact]
    public void PersistentOmfMessageQueue_TryPeek_Test()
    {
        var dequeueCalled = false;
        var flushCalled = false;

        var messageToReturn = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Peek()).Returns(new DataItem(DataItemVersion.V1, messageToReturn));
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.True(persistentOmfMessageQueue.TryPeek(out var peekedMessage));

        Assert.False(dequeueCalled);
        Assert.False(flushCalled);
        Assert.Equal(messageToReturn[5], peekedMessage.MessageBody[0]);
        Assert.Equal(MessageType.Container, peekedMessage.MessageType);
    }

    [Theory]
    [InlineData(MessageAction.Default)]
    [InlineData(MessageAction.Create)]
    [InlineData(MessageAction.Update)]
    [InlineData(MessageAction.Delete)]
    public void PersistentOmfMessageQueue_TryPeek_MessageAction_Test(MessageAction messageAction)
    {
        var dequeueCalled = false;
        var flushCalled = false;

        var messageToReturn = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, (byte)messageAction };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Peek()).Returns(new DataItem(DataItemVersion.V2, messageToReturn));
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.True(persistentOmfMessageQueue.TryPeek(out var peekedMessage));

        Assert.False(dequeueCalled);
        Assert.False(flushCalled);
        Assert.Equal(messageToReturn[5], peekedMessage.MessageBody[0]);
        Assert.Equal(MessageType.Container, peekedMessage.MessageType);
        Assert.Equal(messageAction, peekedMessage.MessageAction);
    }

    [Fact]
    public void PersistentOmfMessageQueue_TryPeek_EmptyQueue_Test()
    {
        var dequeueCalled = false;
        var flushCalled = false;
        DataItem dataItemToReturn = null;

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Peek()).Returns(dataItemToReturn);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.False(persistentOmfMessageQueue.TryPeek(out var peekedMessage));

        Assert.False(dequeueCalled);
        Assert.False(flushCalled);
        Assert.Null(peekedMessage);
    }

    [Fact]
    public void PersistentOmfMessageQueue_TryDequeue_Test()
    {
        var dequeueCalled = false;
        var flushCalled = false;

        var messageToReturn = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true).Returns(new DataItem(DataItemVersion.V1, messageToReturn));
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.True(persistentOmfMessageQueue.TryDequeue(out var dequeuedMessage));

        Assert.NotNull(dequeuedMessage);
        Assert.True(dequeueCalled);
        Assert.True(flushCalled);
        Assert.Equal(messageToReturn[5], dequeuedMessage.MessageBody[0]);
        Assert.Equal(MessageType.Container, dequeuedMessage.MessageType);
    }

    [Theory]
    [InlineData(MessageAction.Default)]
    [InlineData(MessageAction.Create, DataItemVersion.V2)]
    [InlineData(MessageAction.Create, DataItemVersion.V3, null)]
    [InlineData(MessageAction.Create, DataItemVersion.V3, PartitionKey.Key5)]
    [InlineData(MessageAction.Update)]
    [InlineData(MessageAction.Delete)]
    public void PersistentOmfMessageQueue_TryDequeue_MessageAction_Test(MessageAction messageAction, DataItemVersion dataItemVersion = DataItemVersion.V2, PartitionKey? partitionKey = null)
    {
        var dequeueCalled = false;
        var flushCalled = false;

        var expectedMessageType = MessageType.Container;
        var messageToReturn = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x20, (byte)messageAction };

        if (dataItemVersion == DataItemVersion.V3)
        {
            expectedMessageType = MessageType.DynamicData;
            messageToReturn = new byte[] { (byte)expectedMessageType, 0x00, 0x00, 0x00, 0x00, 0x20, (byte)(partitionKey ?? 0), (byte)messageAction, (byte)OmfVersion.Omf12 };
        }

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true).Returns(new DataItem(dataItemVersion, messageToReturn));
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.True(persistentOmfMessageQueue.TryDequeue(out var dequeuedMessage));

        Assert.NotNull(dequeuedMessage);
        Assert.True(dequeueCalled);
        Assert.True(flushCalled);
        Assert.Equal(messageToReturn[5], dequeuedMessage.MessageBody[0]);        
        Assert.Equal(expectedMessageType, dequeuedMessage.MessageType);
        Assert.Equal(partitionKey, dequeuedMessage.PartitionKey);
        Assert.Equal(messageAction, dequeuedMessage.MessageAction);        
    }

    [Fact]
    public void PersistentOmfMessageQueue_TryDequeue_EmptyQueue_Test()
    {
        var dequeueCalled = false;
        var flushCalled = false;
        DataItem dataItemToReturn = null;

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Callback(() => dequeueCalled = true).Returns(dataItemToReturn);
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.FlushDequeues()).Callback(() => flushCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        Assert.False(persistentOmfMessageQueue.TryDequeue(out var firstDequeuedMessage));

        Assert.True(dequeueCalled);
        Assert.False(flushCalled);
        Assert.Null(firstDequeuedMessage);
    }

    [Fact]
    public void PersistentOmfMessageQueue_Clear_Test()
    {
        var deleteBuffersCalled = false;

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.DeleteBuffers()).Callback(() => deleteBuffersCalled = true);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);

        persistentOmfMessageQueue.Clear();

        Assert.True(deleteBuffersCalled);
    }

    /// <summary>
    /// A body with a serialized message ID is written as a V4 record that appends the ID after the V3 trailer and carries it as the tracking ID.
    /// </summary>
    [Fact]
    public void PersistentOmfMessageQueue_Enqueue_ScopedBody_WritesV4Record()
    {
        var id = Guid.NewGuid();
        DataItem enqueued = null;
        var message = new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x20, 0x21 }, MessageAction.Create, 3, OmfVersion.Omf20, PartitionKey.Key7) { SerializedMessageId = id };
        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(q => q.Enqueue(It.IsAny<DataItem>())).Callback((DataItem item) => enqueued = item);

        using var queue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        queue.Enqueue(message);

        Assert.Equal(DataItemVersion.V4, enqueued.Version);
        Assert.Equal(id, enqueued.TrackingId);
        Assert.Equal(message.MessageBody.Length + 8 + SerializedOmfMessage.SerializedMessageIdSize, message.GetMessageSizeInBytes());
        Assert.Equal(message.GetMessageSizeInBytes(), enqueued.Data.Length);
        Assert.Equal(PartitionKey.Key7, (PartitionKey)enqueued.Data[^19]);
        Assert.Equal(MessageAction.Create, (MessageAction)enqueued.Data[^18]);
        Assert.Equal(OmfVersion.Omf20, (OmfVersion)enqueued.Data[^17]);
        Assert.Equal(id, new Guid(enqueued.Data.AsSpan(enqueued.Data.Length - 16)));
    }

    /// <summary>
    /// A body without a serialized message ID is still written as a V3 record with no tracking ID.
    /// </summary>
    [Fact]
    public void PersistentOmfMessageQueue_Enqueue_UnscopedBody_WritesV3Record()
    {
        DataItem enqueued = null;
        var message = new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x20 }, MessageAction.Create, 1, OmfVersion.Omf20);
        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(q => q.Enqueue(It.IsAny<DataItem>())).Callback((DataItem item) => enqueued = item);

        using var queue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        queue.Enqueue(message);

        Assert.Equal(DataItemVersion.V3, enqueued.Version);
        Assert.Null(enqueued.TrackingId);
    }

    /// <summary>
    /// Bodies written and read back keep their fields, scoped bodies keep their serialized message ID, and unscoped bodies load without one.
    /// </summary>
    /// <param name="scoped">Whether the body carries a serialized message ID.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersistentOmfMessageQueue_RoundTrip_KeepsSerializedMessageId(bool scoped)
    {
        Guid? id = scoped ? Guid.NewGuid() : null;
        DataItem enqueued = null;
        var message = new SerializedOmfMessage(MessageType.Schema, new byte[] { 0x20, 0x21, 0x22 }, MessageAction.Update, 5, OmfVersion.Omf20, PartitionKey.Key3) { SerializedMessageId = id };
        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(q => q.Enqueue(It.IsAny<DataItem>())).Callback((DataItem item) => enqueued = item);
        mockPersistentQueue.Setup(q => q.Dequeue()).Returns(() => new DataItem(enqueued.Version, enqueued.Data));

        using var queue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        queue.Enqueue(message);
        Assert.True(queue.TryDequeue(out var dequeued));

        Assert.Equal(id, dequeued.SerializedMessageId);
        Assert.Equal(message.MessageType, dequeued.MessageType);
        Assert.Equal(message.MessageBody, dequeued.MessageBody);
        Assert.Equal(message.ItemCount, dequeued.ItemCount);
        Assert.Equal(message.MessageAction, dequeued.MessageAction);
        Assert.Equal(message.OmfVersion, dequeued.OmfVersion);
        Assert.Equal(message.PartitionKey, dequeued.PartitionKey);
    }

    /// <summary>
    /// Tracked items the persistent queue loses are re-raised with the matching OMF reason code.
    /// </summary>
    /// <param name="lossReason">The loss reason the persistent queue reports.</param>
    /// <param name="expected">The reason code the OMF queue reports.</param>
    [Theory]
    [InlineData(TrackedItemLossReason.Evicted, OmfReasonCode.BufferFull)]
    [InlineData(TrackedItemLossReason.Unreadable, OmfReasonCode.CorruptRecord)]
    [InlineData(TrackedItemLossReason.Cleared, OmfReasonCode.BuffersReset)]
    public void PersistentOmfMessageQueue_TrackedItemsLost_RaisesSerializedBodiesDiscarded(TrackedItemLossReason lossReason, OmfReasonCode expected)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        SerializedBodiesEventArgs raised = null;
        var mockPersistentQueue = new Mock<IPersistentQueue>();

        using var queue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        queue.SerializedBodiesDiscarded += (_, e) => raised = e;
        mockPersistentQueue.Raise(q => q.TrackedItemsLost += null, new TrackedItemsLostEventArgs(ids, lossReason));

        Assert.NotNull(raised);
        Assert.Equal(expected, raised.Reason);
        Assert.Equal(ids, raised.SerializedMessageIds);
    }

    /// <summary>
    /// A failed disk flush of a scoped body raises <see cref="PersistentOmfMessageQueueBase{TMessage}.SerializedBodyWriteFailed"/> with <see cref="OmfReasonCode.DiskError"/>; an unscoped body raises nothing.
    /// </summary>
    [Fact]
    public void PersistentOmfMessageQueue_Enqueue_FlushFails_RaisesWriteFailedForScopedBody()
    {
        var id = Guid.NewGuid();
        var raised = new List<SerializedBodiesEventArgs>();
        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(q => q.FlushEnqueues()).Throws(new IOException("UnitTest is out of space!"));

        using var queue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        queue.SerializedBodyWriteFailed += (_, e) => raised.Add(e);
        queue.Enqueue(new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x20 }, MessageAction.Create, 1, OmfVersion.Omf20));
        queue.Enqueue(new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x20 }, MessageAction.Create, 1, OmfVersion.Omf20) { SerializedMessageId = id });

        var single = Assert.Single(raised);
        Assert.Equal(OmfReasonCode.DiskError, single.Reason);
        Assert.Equal(new[] { id }, single.SerializedMessageIds);
    }
}
