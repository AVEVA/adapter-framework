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
using System.IO;
using System.Linq;
using Moq;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

public class FileQueue_Tests
{
    [Fact]
    public void FileQueue_InvalidStateAfterDirectoryRepair_ThrowsException()
    {
        var invalidQs = new QueueState();
        invalidQs.SetReaderFileNumber(-1);

        Assert.False(invalidQs.IsValid());

        var mfm = new Mock<IFileManager>();
        mfm.Setup(fm => fm.RepairDirectoryAndUpdateState(It.IsAny<QueueState>(), It.IsAny<int>()))
            .Callback<QueueState, int>((qs, i) => SetQueueStateEqualTo(qs, invalidQs));
        var mser = new Mock<ISerializer>();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(new QueueState());

        Assert.Throws<InvalidOperationException>(() => new FileQueue(mfm.Object, mser.Object, 1, 0));
    }

    [Fact]
    public void FileQueue_Enqueue_ZeroLengthArray_NotEnqueuedAndFlushed()
    {
        using var writerStream = new MemoryStream();
        using var readerStream = new MemoryStream();
        var mfm = new Mock<IFileManager>();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(writerStream);
        mfm.Setup(fm => fm.CreateReaderStream(It.IsAny<int>())).Returns(readerStream);

        var serializedItems = new List<byte[]>();
        var mser = new Mock<ISerializer>();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(new QueueState());
        mser.Setup(ser => ser.SerializeDataItem(writerStream, It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) => serializedItems.Add(d.Data));

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);

        var enqueuedMessages = TestHelper.GenerateRandomDataItems(5, 0);
        foreach (var msg in enqueuedMessages) fq.Enqueue(msg);

        fq.FlushEnqueues();
        Assert.Empty(serializedItems);
    }

    [Fact]
    public void FileQueue_Enqueue_ArrayLengthLongerThanFileSize_ThrowsException()
    {
        using var writerStream = new MemoryStream();
        using var readerStream = new MemoryStream();
        var mfm = new Mock<IFileManager>();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(writerStream);
        mfm.Setup(fm => fm.CreateReaderStream(It.IsAny<int>())).Returns(readerStream);

        var mser = new Mock<ISerializer>();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(new QueueState());

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0); // 1 MB file size

        var msg = new byte[(1024 * 1024) + 1];

        Assert.Throws<ArgumentOutOfRangeException>(() => fq.Enqueue(new DataItem(DataItemVersion.V1, msg)));
    }

    [Fact]
    public void FileQueue_EnqueueAndFlushEnqueues_SerializesEnqueuedItemsOnceAndFlushesStreamAndPersistsAdvancedWriterPosition()
    {
        bool flushed = false;
        var writerStream = new Mock<Stream>();
        writerStream.Setup(s => s.Flush()).Callback(() => flushed = true);
        long writeStreamPosition = 0;
        writerStream.SetupGet(s => s.Position).Returns(() => writeStreamPosition);

        var mfm = new Mock<IFileManager>();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(writerStream.Object);
        var stateStream = new Mock<Stream>();
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream.Object);
        mfm.Setup(fm => fm.CreateReaderStream(It.IsAny<int>())).Returns(() => new MemoryStream());

        var serializedItems = new List<DataItem>();
        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        mser.Setup(ser => ser.SerializeDataItem(writerStream.Object, It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) =>
            {
                writeStreamPosition += d.Data.Length; // Technically there would be some overhead but we just want to show that the position advances
                serializedItems.Add(d);
            });
        mser.Setup(ser => ser.SerializeWriterState(stateStream.Object, It.IsAny<QueueState>()))
            .Callback<Stream, QueueState>((s, qs) =>
            {
                persistedQs.SetWriterFileNumber(qs.WriterFileNumber);
                persistedQs.SetWriterPosition(qs.WriterPosition);
            });

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        var enqueuedMessages = TestHelper.GenerateRandomDataItems(5, 100);

        // Enqueue..
        foreach (var msg in enqueuedMessages) fq.Enqueue(msg);

        // Assert we serialized the items, flushed the stream, and persisted an updated queuestate writer position
        Assert.Empty(serializedItems);
        fq.FlushEnqueues();
        TestHelper.AssertEqual(enqueuedMessages, serializedItems);
        Assert.True(flushed);
        Assert.True(persistedQs.WriterPosition > 0);
        var writePositionAfterFirstFlush = persistedQs.WriterPosition;

        // Flush again and ensure we don't get additional items serialized, since the items were already serialized
        // Also check that persisted queuestate writer position is the same since no new items have been added
        fq.FlushEnqueues();
        TestHelper.AssertEqual(enqueuedMessages, serializedItems);
        Assert.Equal(writePositionAfterFirstFlush, persistedQs.WriterPosition);

        // Enqueue some more items and assert that they are correctly flushed
        flushed = false;
        var newMessages = TestHelper.GenerateRandomDataItems(16, 50);
        enqueuedMessages.AddRange(newMessages);
        foreach (var msg in newMessages) fq.Enqueue(msg);
        fq.FlushEnqueues();
        TestHelper.AssertEqual(enqueuedMessages, serializedItems);
        Assert.True(flushed);
        Assert.True(persistedQs.WriterPosition > writePositionAfterFirstFlush);
    }

    [Fact]
    public void FileQueue_EnqueueAndFlushEnqueues_OutOfDiskException_AnyPendingFileDeletions_DeletesOldestFileAndSerializes()
    {
        // Setup..
        var writerStream = new Mock<Stream>();

        var mfm = new Mock<IFileManager>();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(writerStream.Object);
        var stateStream = new Mock<Stream>();
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream.Object);
        mfm.Setup(fm => fm.CreateReaderStream(It.IsAny<int>())).Returns(() => new MemoryStream());
        mfm.Setup(fm => fm.AnyPendingFileDeletions()).Returns(true);
        var deleteOldestFileCalled = false;
        mfm.Setup(fm => fm.DeleteOldestFilePendingDeletion()).Callback(() => deleteOldestFileCalled = true);

        var serializedItems = new List<DataItem>();
        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        var numSerialized = 0;
        mser.Setup(ser => ser.SerializeDataItem(writerStream.Object, It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) =>
            {
                numSerialized++;
                if (numSerialized == 3) TestHelper.ThrowOutOfDiskException();
                serializedItems.Add(d);
            });

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        var enqueuedMessages = TestHelper.GenerateRandomDataItems(5, 100);

        // Enqueue and flush..
        foreach (var msg in enqueuedMessages) fq.Enqueue(msg);
        fq.FlushEnqueues();

        // Assert we serialized the items, flushed the stream, and persisted an updated queuestate writer position
        Assert.True(deleteOldestFileCalled);
        TestHelper.AssertEqual(enqueuedMessages, serializedItems);
    }

    [Fact]
    public void FileQueue_EnqueueAndFlushEnqueues_OutOfDiskException_NoPendingFileDeletions_RetriesEnqueueAndThrowsOnSecondFailure()
    {
        // Setup..
        var mfm = new Mock<IFileManager>();
        var writerStream = new Mock<Stream>();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(writerStream.Object);
        var stateStream = new Mock<Stream>();
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream.Object);
        mfm.Setup(fm => fm.CreateReaderStream(It.IsAny<int>())).Returns(() => new MemoryStream());
        mfm.Setup(fm => fm.AnyPendingFileDeletions()).Returns(false);
        var deleteOldestFileCalled = false;
        mfm.Setup(fm => fm.DeleteOldestFilePendingDeletion()).Callback(() => deleteOldestFileCalled = true);

        var serializedItems = new List<DataItem>();
        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        var numSerialized = 0;
        var numSerializedThatDiskCanHold = 3;
        mser.Setup(ser => ser.SerializeDataItem(writerStream.Object, It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) =>
            {
                if (numSerialized >= numSerializedThatDiskCanHold) TestHelper.ThrowOutOfDiskException();
                numSerialized++;
                serializedItems.Add(d);
            });

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        var enqueuedMessages = TestHelper.GenerateRandomDataItems(5, 100);

        // Enqueue and flush..
        foreach (var msg in enqueuedMessages) fq.Enqueue(msg);

        var inpOutExceptionThrown = false;
        try
        {
            fq.FlushEnqueues();
        }
        catch (IOException)
        {
            inpOutExceptionThrown = true;
        }

        // Since AnyPendingFileDeletions() returned false, DeleteOldestFilePendingDeletion() should not have been called and we should have the items prior to the disk IO exception flushed
        Assert.False(deleteOldestFileCalled);
        Assert.True(inpOutExceptionThrown);
        Assert.Equal(numSerializedThatDiskCanHold, serializedItems.Count);

        // Simulate expansion of disk space after which we should be able to flush all of the items
        numSerializedThatDiskCanHold = enqueuedMessages.Count;
        fq.FlushEnqueues();
        TestHelper.AssertEqual(enqueuedMessages, serializedItems);
    }

    [Fact]
    public void FileQueue_EnqueueAndFlushEnqueues_EnqueuedItemsGreaterThanFileSize_ItemsEnqueuedToNewFile()
    {
        // Setup..
        var mfm = new Mock<IFileManager>();
        var writerStream0 = new Mock<Stream>();
        writerStream0.SetupProperty(s => s.Position);
        var writerStream1 = new Mock<Stream>();
        writerStream1.SetupProperty(s => s.Position);
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(writerStream0.Object);
        mfm.Setup(fm => fm.CreateWriterStream(1)).Returns(writerStream1.Object);
        var stateStream = new Mock<Stream>();
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream.Object);
        mfm.Setup(fm => fm.CreateReaderStream(It.IsAny<int>())).Returns(() => new MemoryStream());

        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        var serializedItemsDataFile0 = new List<DataItem>();
        var serializedItemsDataFile1 = new List<DataItem>();
        mser.Setup(ser => ser.SerializeDataItem(writerStream0.Object, It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) =>
            {
                s.Position += d.Data.Length;
                serializedItemsDataFile0.Add(d);
            });
        mser.Setup(ser => ser.SerializeDataItem(writerStream1.Object, It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) =>
            {
                s.Position += d.Data.Length;
                serializedItemsDataFile1.Add(d);
            });

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        var enqueuedMessages = TestHelper.GenerateRandomDataItems(2, (1024 * 1024) - 200); // 2 byte arrays a little less than the 1 MB file limit so that each will get enqueued in its own file

        // Enqueue and flush..
        foreach (var msg in enqueuedMessages) fq.Enqueue(msg);
        fq.FlushEnqueues();

        Assert.Single(serializedItemsDataFile0);
        TestHelper.AssertEqual(enqueuedMessages[0], serializedItemsDataFile0[0]);
        Assert.Single(serializedItemsDataFile1);
        TestHelper.AssertEqual(enqueuedMessages[1], serializedItemsDataFile1[0]);
    }

    [Fact]
    public void FileQueue_EnqueueAndFlushEnqueues_EnqueuedItemsGreaterThanMaximumQueueSize_FilesDeletedToMaintainQueueSize()
    {
        var maximumNumberOfQueueFiles = 2;
        var numQueueFilesTooMany = 3;

        // Setup..
        var mfm = new Mock<IFileManager>();
        for (int i = 0; i < maximumNumberOfQueueFiles + numQueueFilesTooMany; i++)
        {
            var writerStream = new Mock<Stream>();
            writerStream.SetupAllProperties();
            mfm.Setup(fm => fm.CreateWriterStream(i)).Returns(writerStream.Object);
        }

        var stateStream = new Mock<Stream>();
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream.Object);
        mfm.Setup(fm => fm.CreateReaderStream(It.IsAny<int>())).Returns(() => new MemoryStream());
        var pendingDeletion = new List<int>();
        var deleted = new List<int>();
        mfm.Setup(fm => fm.AddPendingDeletion(It.IsAny<int>())).Callback<int>(n => pendingDeletion.Add(n));
        mfm.Setup(fm => fm.DeleteOldestFilePendingDeletion()).Callback(() =>
        {
            deleted.Add(pendingDeletion[0]);
            pendingDeletion.RemoveAt(0);
        });

        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        mser.Setup(ser => ser.SerializeDataItem(It.IsAny<Stream>(), It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) => s.Position += d.Data.Length);

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 2);
        var enqueuedMessages = TestHelper.GenerateRandomDataItems(maximumNumberOfQueueFiles + numQueueFilesTooMany, (1024 * 1024) - 200); // 3 more items than allowed queue files, where each item fills its own queue file

        // Enqueue and flush..
        foreach (var msg in enqueuedMessages) fq.Enqueue(msg);
        fq.FlushEnqueues();

        Assert.Equal(deleted.Count, numQueueFilesTooMany);
        for (int i = 0; i < numQueueFilesTooMany; i++)
        {
            Assert.Equal(i, deleted[i]); // Queue files should have been deleted in order from 0,1,2...
        }
    }

    [Fact]
    public void FileQueue_UpdateMaxQueueFiles_ReopensStreams_AndAllowsSubsequentEnqueue()
    {
        var mfm = new Mock<IFileManager>();
        using var writerStreamInitial = new MemoryStream();
        using var writerStreamAfterUpdate = new MemoryStream();
        using var readerStreamInitial = new MemoryStream();
        using var readerStreamAfterUpdate = new MemoryStream();
        mfm.SetupSequence(fm => fm.CreateWriterStream(0))
            .Returns(writerStreamInitial)
            .Returns(writerStreamAfterUpdate);
        mfm.SetupSequence(fm => fm.CreateReaderStream(0))
            .Returns(readerStreamInitial)
            .Returns(readerStreamAfterUpdate);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(() => new MemoryStream());

        var mser = new Mock<ISerializer>();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(new QueueState());
        mser.Setup(ser => ser.SerializeDataItem(It.IsAny<Stream>(), It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) => s.Position += d.Data.Length);

        using var fq = new FileQueue(mfm.Object, mser.Object, 20, 1);
        var firstItem = TestHelper.GenerateRandomDataItems(1, 100).First();
        fq.Enqueue(firstItem);
        fq.FlushEnqueues();

        fq.UpdateMaxQueueFiles(1);

        var secondItem = TestHelper.GenerateRandomDataItems(1, 100).First();
        var ex = Record.Exception(() =>
        {
            fq.Enqueue(secondItem);
            fq.FlushEnqueues();
        });

        Assert.Null(ex);
        mfm.Verify(fm => fm.CreateWriterStream(0), Times.Exactly(2));
        mfm.Verify(fm => fm.CreateReaderStream(0), Times.Exactly(2));
    }

    [Fact]
    public void FileQueue_UpdateMaxQueueFiles_UsesRequestedLimitDuringRepair()
    {
        var mfm = new Mock<IFileManager>();
        using var writerStreamInitial = new MemoryStream();
        using var writerStreamAfterUpdate = new MemoryStream();
        using var readerStreamInitial = new MemoryStream();
        using var readerStreamAfterUpdate = new MemoryStream();
        mfm.SetupSequence(fm => fm.CreateWriterStream(0))
            .Returns(writerStreamInitial)
            .Returns(writerStreamAfterUpdate);
        mfm.SetupSequence(fm => fm.CreateReaderStream(0))
            .Returns(readerStreamInitial)
            .Returns(readerStreamAfterUpdate);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(() => new MemoryStream());

        QueueState queueStateDuringRepair = null;
        int requestedLimit = -1;
        mfm.Setup(fm => fm.RepairDirectoryAndUpdateState(It.IsAny<QueueState>(), It.IsAny<int>()))
            .Callback<QueueState, int>((qs, limit) =>
            {
                queueStateDuringRepair = new QueueState(qs.ReaderFileNumber, qs.ReaderPosition, qs.WriterFileNumber, qs.WriterPosition);
                requestedLimit = limit;
            });

        var mser = new Mock<ISerializer>();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(new QueueState());
        mser.Setup(ser => ser.SerializeDataItem(It.IsAny<Stream>(), It.IsAny<DataItem>()))
            .Callback<Stream, DataItem>((s, d) => s.Position += d.Data.Length);

        using var fq = new FileQueue(mfm.Object, mser.Object, 2, 2);
        var firstItem = TestHelper.GenerateRandomDataItems(1, 100).First();
        fq.Enqueue(firstItem);
        fq.FlushEnqueues();

        var ex = Record.Exception(() => fq.UpdateMaxQueueFiles(1));

        Assert.Null(ex);
        Assert.NotNull(queueStateDuringRepair);
        Assert.Equal(1, requestedLimit);
        Assert.True(queueStateDuringRepair.WriterPosition > 0);
        Assert.Equal(0, queueStateDuringRepair.ReaderPosition);
    }

    [Fact]
    public void FileQueue_Peek_ReturnsDeserializedItem()
    {
        var mfm = new Mock<IFileManager>();
        using var writerStream = new MemoryStream();
        using var stateStream = new MemoryStream();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(() => writerStream);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream);
        var readerStream = new Mock<Stream>();
        readerStream.SetupAllProperties();
        readerStream.SetupGet(s => s.Length).Returns(300); // If position = length = 0, FQ assumes it has finished reader file
        mfm.Setup(fm => fm.CreateReaderStream(0)).Returns(readerStream.Object);

        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        var deserializedItem = TestHelper.GenerateRandomDataItems(1, 200).First();
        mser.Setup(ser => ser.DeserializeDataItem(readerStream.Object)).Returns(deserializedItem);
        mser.Setup(ser => ser.DeserializeDataItem(readerStream.Object)).Callback(() => { readerStream.SetupGet(s => s.Position).Returns(readerStream.Object.Position + 100); });

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);

        var readPosition = readerStream.Object.Position;
        var item = fq.Dequeue();
        Assert.NotEqual(readPosition, readerStream.Object.Position);
        readPosition = readerStream.Object.Position;

        fq.Peek();
        Assert.Equal(readPosition, readerStream.Object.Position);
        fq.Peek();
        Assert.Equal(readPosition, readerStream.Object.Position);
    }

    [Fact]
    public void FileQueue_Dequeue_ReturnsDeserializedItem()
    {
        var mfm = new Mock<IFileManager>();
        using var writerStream = new MemoryStream();
        using var stateStream = new MemoryStream();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(() => writerStream);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream);
        var readerStream = new Mock<Stream>();
        readerStream.SetupAllProperties();
        readerStream.SetupGet(s => s.Length).Returns(300); // If position = length = 0, FQ assumes it has finished reader file
        mfm.Setup(fm => fm.CreateReaderStream(0)).Returns(readerStream.Object);

        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        var deserializedItem = TestHelper.GenerateRandomDataItems(1, 200).First();
        mser.Setup(ser => ser.DeserializeDataItem(readerStream.Object)).Returns(deserializedItem);

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        Assert.Equal(deserializedItem, fq.Dequeue());
    }

    [Fact]
    public void FileQueue_Dequeue_DataRecordExceptionFromSerializer_TriesToFindNextValidDataitem()
    {
        var mfm = new Mock<IFileManager>();
        using var writerStream = new MemoryStream();
        using var stateStream = new MemoryStream();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(() => writerStream);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream);
        var readerStream = new Mock<Stream>();
        readerStream.SetupAllProperties();
        readerStream.SetupGet(s => s.Length).Returns(300); // If position = length = 0, FQ assumes it has finished reader file
        mfm.Setup(fm => fm.CreateReaderStream(0)).Returns(readerStream.Object);

        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        mser.Setup(ser => ser.DeserializeDataItem(readerStream.Object)).Callback<Stream>((s) => throw new DataRecordException("Invalid data record"));
        var deserializedItem = TestHelper.GenerateRandomDataItems(1, 200).First();
        mser.Setup(ser => ser.TryDeserializeNextValidDataItem(readerStream.Object, out deserializedItem)).Returns(true);

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        Assert.Equal(deserializedItem, fq.Dequeue());
    }

    [Fact]
    public void FileQueue_Dequeue_ReaderStreamPositionEqualsLength_WriterAndReaderOnFile0_AtEndOfFileReturnsNull()
    {
        var mfm = new Mock<IFileManager>();
        using var writerStream = new MemoryStream();
        using var stateStream = new MemoryStream();
        mfm.Setup(fm => fm.CreateWriterStream(0)).Returns(() => writerStream);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream);
        var readerStream = new Mock<Stream>();
        readerStream.SetupAllProperties();
        readerStream.SetupGet(s => s.Position).Returns(300); // If position == length, FQ assumes it is at the end of the reader file
        readerStream.SetupGet(s => s.Length).Returns(300); // If position == length, FQ assumes it is at the end of the reader file
        mfm.Setup(fm => fm.CreateReaderStream(0)).Returns(readerStream.Object);

        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState();
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        DataItem data = null;
        mser.Setup(ser => ser.DeserializeDataItem(It.IsAny<Stream>())).Returns(data);

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        Assert.Null(fq.Dequeue());
    }

    [Fact]
    public void FileQueue_Dequeue_ReaderStreamPositionEqualsLength_WriterFileAheadOfReaderOnFile_ReaderProceedsToNewFileAndDeserializesItem()
    {
        var mfm = new Mock<IFileManager>();
        using var writerStream = new MemoryStream();
        using var stateStream = new MemoryStream();
        mfm.Setup(fm => fm.CreateWriterStream(It.IsAny<int>())).Returns(() => writerStream);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream);
        var readerStream0 = new Mock<Stream>();
        readerStream0.SetupProperty(s => s.Position);
        readerStream0.SetupGet(s => s.Length).Returns(300); // If position == length, FQ assumes it is at the end of the reader file
        mfm.Setup(fm => fm.CreateReaderStream(0)).Returns(readerStream0.Object);
        var readerStream1 = new Mock<Stream>();
        readerStream1.SetupGet(s => s.Position).Returns(0);
        readerStream1.SetupGet(s => s.Length).Returns(300);
        mfm.Setup(fm => fm.CreateReaderStream(1)).Returns(readerStream1.Object);

        var mser = new Mock<ISerializer>();
        var persistedQs = new QueueState(0, 300, 1, 500);
        mser.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(persistedQs);
        var deserializedItem = TestHelper.GenerateRandomDataItems(1, 200).First();
        mser.Setup(ser => ser.DeserializeDataItem(readerStream1.Object)).Returns(deserializedItem);

        using var fq = new FileQueue(mfm.Object, mser.Object, 1, 0);
        Assert.Equal(deserializedItem, fq.Dequeue());
    }

    [Fact]
    public void FileQueue_FlushDequeues_ReaderStateUpdatedAndPersistedAndOldDataFilesDeleted()
    {
        // Set up so that on dequeue we proceed to a new data file and monitor method calls made by the FQ
        var mfm = new Mock<IFileManager>();
        using var writerStream = new MemoryStream();
        using var stateStream = new MemoryStream();
        mfm.Setup(fm => fm.CreateWriterStream(It.IsAny<int>())).Returns(() => writerStream);
        mfm.Setup(fm => fm.CreateStateStream()).Returns(stateStream);
        var readerStream0 = new Mock<Stream>();
        readerStream0.SetupProperty(s => s.Position);
        readerStream0.SetupGet(s => s.Length).Returns(300); // If position == length, FQ assumes it is at the end of the reader file
        mfm.Setup(fm => fm.CreateReaderStream(0)).Returns(readerStream0.Object);
        var readerStream1 = new Mock<Stream>();
        readerStream1.SetupGet(s => s.Position).Returns(0);
        readerStream1.SetupGet(s => s.Length).Returns(300);
        mfm.Setup(fm => fm.CreateReaderStream(1)).Returns(readerStream1.Object);
        var pendingDeletion = new List<int>();
        mfm.Setup(fm => fm.AddPendingDeletion(It.IsAny<int>())).Callback<int>(n => pendingDeletion.Add(n));
        var deleted = new List<int>();
        mfm.Setup(fm => fm.DeleteFilesPendingDeletion()).Callback(() =>
        {
            foreach (var n in pendingDeletion)
            {
                deleted.Add(n);
            }

            pendingDeletion.Clear();
        });

        var mockSerializer = new Mock<ISerializer>();
        var persistedQs = new QueueState(0, 300, 1, 500);
        mockSerializer.Setup(ser => ser.DeserializeQueueState(It.IsAny<Stream>())).Returns(
            new QueueState(persistedQs.ReaderFileNumber, persistedQs.ReaderPosition, persistedQs.WriterFileNumber, persistedQs.WriterPosition));
        var deserializedItem = TestHelper.GenerateRandomDataItems(1, 200).First();
        mockSerializer.Setup(ser => ser.DeserializeDataItem(readerStream1.Object)).Returns(deserializedItem);
        mockSerializer.Setup(ser => ser.SerializeQueueState(It.IsAny<Stream>(), It.IsAny<QueueState>())).Callback<Stream, QueueState>((s, qs) => SetQueueStateEqualTo(persistedQs, qs));
        mockSerializer.Setup(ser => ser.SerializeReaderState(It.IsAny<Stream>(), It.IsAny<QueueState>()))
            .Callback<Stream, QueueState>((s, qs) =>
            {
                persistedQs.SetReaderFileNumber(qs.ReaderFileNumber);
                persistedQs.SetReaderPosition(qs.ReaderPosition);
            });

        // Dequeue an item, moving the queue to the next data file
        using var fq = new FileQueue(mfm.Object, mockSerializer.Object, 1, 0);
        Assert.Equal(deserializedItem, fq.Dequeue());
        Assert.Single(pendingDeletion);
        Assert.Equal(0, pendingDeletion[0]);

        // Test that FlushDequeus updates the persisted reader state and deletes the old files
        fq.FlushDequeues();
        Assert.Equal(1, persistedQs.ReaderFileNumber);
        Assert.Single(deleted);
        Assert.Equal(0, deleted[0]);
    }

    private static void SetQueueStateEqualTo(QueueState toBeModified, QueueState toBeModifiedTo)
    {
        toBeModified.SetReaderFileNumber(toBeModifiedTo.ReaderFileNumber);
        toBeModified.SetReaderPosition(toBeModifiedTo.ReaderPosition);
        toBeModified.SetWriterFileNumber(toBeModifiedTo.WriterFileNumber);
        toBeModified.SetWriterPosition(toBeModifiedTo.WriterFileNumber);
    }
}
