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
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
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
    [InlineData(null, MessageAction.Create)]
    [InlineData(PartitionKey.Key16, MessageAction.Update)]
    [InlineData(null, MessageAction.Delete)]
    public void PersistentOmfMessageQueue_ResourceCounts_RoundTrip_Test(PartitionKey? partitionKey, MessageAction messageAction)
    {
        DataItem enqueuedDataItem = null;
        var body = new byte[] { 0x20, 0x21, 0x22 };
        var resourceCounts = new OmfResourceCounts(1_000, 7, 3);
        var serializedOmfMessage = new SerializedOmfMessage(MessageType.Instance, body, messageAction, 1_012, OmfVersion.Omf20, partitionKey)
        {
            ResourceCounts = resourceCounts,
        };

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<DataItem>()))
            .Callback((DataItem dataItem) => enqueuedDataItem = dataItem);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        persistentOmfMessageQueue.Enqueue(serializedOmfMessage);

        Assert.NotNull(enqueuedDataItem);
        Assert.Equal(DataItemVersion.V4, enqueuedDataItem.Version);

        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Returns(enqueuedDataItem);
        Assert.True(persistentOmfMessageQueue.TryDequeue(out var dequeuedMessage));

        Assert.Equal(MessageType.Instance, dequeuedMessage.MessageType);
        Assert.Equal(body, dequeuedMessage.MessageBody);
        Assert.Equal(1_012, dequeuedMessage.ItemCount);
        Assert.Equal(messageAction, dequeuedMessage.MessageAction);
        Assert.Equal(OmfVersion.Omf20, dequeuedMessage.OmfVersion);
        Assert.Equal(partitionKey, dequeuedMessage.PartitionKey);
        Assert.Equal(resourceCounts, dequeuedMessage.ResourceCounts);
    }

    [Theory]
    [InlineData(MessageType.Instance, OmfVersion.Omf20)]
    [InlineData(MessageType.Schema, OmfVersion.Omf20)]
    [InlineData(MessageType.DynamicData, OmfVersion.Omf12)]
    public void PersistentOmfMessageQueue_NoResourceCounts_WritesV3_Test(MessageType messageType, OmfVersion omfVersion)
    {
        DataItem enqueuedDataItem = null;
        var serializedOmfMessage = new SerializedOmfMessage(messageType, new byte[] { 0x20 }, MessageAction.Default, 1, omfVersion);

        var mockPersistentQueue = new Mock<IPersistentQueue>();
        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Enqueue(It.IsAny<DataItem>()))
            .Callback((DataItem dataItem) => enqueuedDataItem = dataItem);

        using var persistentOmfMessageQueue = new PersistentOmfMessageQueue(TestTargetIdentifier, mockPersistentQueue.Object, null);
        persistentOmfMessageQueue.Enqueue(serializedOmfMessage);

        Assert.Equal(DataItemVersion.V3, enqueuedDataItem.Version);

        mockPersistentQueue.Setup(persistentQueue => persistentQueue.Dequeue()).Returns(enqueuedDataItem);
        Assert.True(persistentOmfMessageQueue.TryDequeue(out var dequeuedMessage));
        Assert.True(dequeuedMessage.ResourceCounts.IsEmpty);
    }

    [Fact]
    public void PersistentOmfMessageQueue_MixedV3AndV4Records_ReadAfterUpgrade_Test()
    {
        const string FilePrefix = "data";
        var bufferDirectory = Path.Combine(Path.GetTempPath(), $"omf-buffer-{Guid.NewGuid():N}");

        try
        {
            // Before upgrade: V3 records exactly as the previous build wrote them.
            using (var fileQueue = new FileQueue(bufferDirectory, FilePrefix))
            {
                fileQueue.Enqueue(CreateV3DataItem(MessageType.Instance, 5, new byte[] { 0x10, 0x11 }, PartitionKey.Key5, MessageAction.Create, OmfVersion.Omf20));
                fileQueue.Enqueue(CreateV3DataItem(MessageType.DynamicData, 2, new byte[] { 0x20 }, null, MessageAction.Default, OmfVersion.Omf12));
                fileQueue.FlushEnqueues();
            }

            // After upgrade: reopen the same buffer with the new code and append a V4 record.
            var v4Counts = new OmfResourceCounts(1_000, 7, 3);
            using var reopenedFileQueue = new FileQueue(bufferDirectory, FilePrefix);
            using var queue = new PersistentOmfMessageQueue(TestTargetIdentifier, reopenedFileQueue, null);
            queue.Enqueue(new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x30, 0x31, 0x32 }, MessageAction.Update, 1_010, OmfVersion.Omf20, PartitionKey.Key16)
            {
                ResourceCounts = v4Counts,
            });

            Assert.True(queue.TryDequeue(out var first));
            Assert.Equal(MessageType.Instance, first.MessageType);
            Assert.Equal(new byte[] { 0x10, 0x11 }, first.MessageBody);
            Assert.Equal(5, first.ItemCount);
            Assert.Equal(PartitionKey.Key5, first.PartitionKey);
            Assert.Equal(MessageAction.Create, first.MessageAction);
            Assert.Equal(OmfVersion.Omf20, first.OmfVersion);
            Assert.True(first.ResourceCounts.IsEmpty);

            Assert.True(queue.TryDequeue(out var second));
            Assert.Equal(MessageType.DynamicData, second.MessageType);
            Assert.Equal(new byte[] { 0x20 }, second.MessageBody);
            Assert.Equal(2, second.ItemCount);
            Assert.Null(second.PartitionKey);
            Assert.Equal(OmfVersion.Omf12, second.OmfVersion);
            Assert.True(second.ResourceCounts.IsEmpty);

            Assert.True(queue.TryDequeue(out var third));
            Assert.Equal(new byte[] { 0x30, 0x31, 0x32 }, third.MessageBody);
            Assert.Equal(1_010, third.ItemCount);
            Assert.Equal(PartitionKey.Key16, third.PartitionKey);
            Assert.Equal(MessageAction.Update, third.MessageAction);
            Assert.Equal(v4Counts, third.ResourceCounts);

            Assert.False(queue.TryDequeue(out _));
        }
        finally
        {
            if (Directory.Exists(bufferDirectory))
            {
                Directory.Delete(bufferDirectory, recursive: true);
            }
        }
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

    // Byte layout used by builds before V4: [type][itemCount int32 LE][body][partitionKey][action][omfVersion].
    private static DataItem CreateV3DataItem(MessageType messageType, int itemCount, byte[] body, PartitionKey? partitionKey, MessageAction messageAction, OmfVersion omfVersion)
    {
        var data = new byte[1 + sizeof(int) + body.Length + 3];
        data[0] = (byte)messageType;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(1), itemCount);
        body.CopyTo(data, 1 + sizeof(int));
        data[^3] = (byte)(partitionKey ?? 0);
        data[^2] = (byte)messageAction;
        data[^1] = (byte)omfVersion;
        return new DataItem(DataItemVersion.V3, data);
    }
}
