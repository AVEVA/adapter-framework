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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

public class FileQueue_IntegrationTests : IDisposable
{
    #region Private constants and fields

    private const string DefaultPartition = "defaultPartition";
    private const int DefaultQueueFileSizeMb = 1;
    private FileQueue _fileQueue;
    private bool _disposed;

    #endregion

    #region Test Initialize and Cleanup

    public FileQueue_IntegrationTests()
    {
        TestHelper.DeleteDirectoryWithRetry(DefaultPartition);
        _fileQueue = GenerateFileQueue();
    }

    public static FileQueue GenerateFileQueue(string partition = DefaultPartition, int fileSizeMb = DefaultQueueFileSizeMb, int maxQueuefiles = 0, TestLogger testLogger = null)
    {
        return new FileQueue(new FileManager(new FileSystemInteractor(), partition, logger: testLogger), new Serializer(), fileSizeMb, maxQueuefiles, testLogger);
    }

    #endregion

    #region Basic Operation Tests

    [Fact]
    public void FileQueue_Enqueue_Dequeue_Success()
    {
        var enqueuedData = new DataItem(DataItemVersion.V1, new byte[] { 1, 5, 9, 3 });

        _fileQueue.Enqueue(enqueuedData);
        _fileQueue.FlushEnqueues();

        var dequeuedData = _fileQueue.Dequeue();

        TestHelper.AssertEqual(enqueuedData, dequeuedData);
    }

    [Fact]
    public void FileQueue_Enqueue_ItemSizeSameAsFileSize_Success()
    {
        var enqueuedData = TestHelper.GenerateRandomDataItems(1, DefaultQueueFileSizeMb * 1024 * 1024).First();

        _fileQueue.Enqueue(enqueuedData);
        _fileQueue.FlushEnqueues();

        var dequeuedData = _fileQueue.Dequeue();

        TestHelper.AssertEqual(enqueuedData, dequeuedData);
    }

    [Fact]
    public void FileQueue_Enqueue_Dequeue_MultipleItems_Success()
    {
        var enqueuedList = new List<DataItem>();

        var numItems = 15;
        var rnd = new Random();
        for (int i = 0; i < numItems; i++)
        {
            var item = new byte[4];
            rnd.NextBytes(item);

            enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
        }

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(enqueuedList, dequeuedList);
    }

    [Fact]
    public void FileQueue_EnqueueWithoutFlushing_DataNotDequeued()
    {
        var enqueuedData = new DataItem(DataItemVersion.V1, new byte[] { 1, 5, 9, 3 });

        _fileQueue.Enqueue(enqueuedData);

        var dequeuedData = _fileQueue.Dequeue();

        Assert.Null(dequeuedData);
    }

    [Fact]
    public void FileQueue_ConcurrentEnqueueDequeue_Success()
    {
        using var taskCancelSource = new CancellationTokenSource();
        var dequeueListLock = new object();

        try
        {
            var dequeuedList = new List<DataItem>();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    var dequeuedItem = _fileQueue.Dequeue();
                    if (dequeuedItem != null)
                    {
                        lock (dequeueListLock)
                        {
                            dequeuedList.Add(dequeuedItem);
                        }
                    }
                    else
                    {
                        await Task.Delay(100, taskCancelSource.Token);
                    }
                }
            }, taskCancelSource.Token);

            var enqueuedList = new List<DataItem>();

            var numItems = 5;
            var rnd = new Random();
            for (int i = 0; i < numItems; i++)
            {
                var item = new byte[4];
                rnd.NextBytes(item);

                enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
            }

            foreach (var item in enqueuedList)
            {
                _fileQueue.Enqueue(item);
            }

            _fileQueue.FlushEnqueues();

            Assert.True(SpinWait.SpinUntil(() =>
            {
                lock (dequeueListLock) { return dequeuedList.Count == enqueuedList.Count; }
            }, 500));

            TestHelper.AssertEqual(enqueuedList, dequeuedList);
        }
        finally
        {
            taskCancelSource.Cancel();
        }
    }

    [Fact]
    public void FileQueue_ConcurrentEnqueueDequeue_MultipleEnqueueFlushes_Success()
    {
        using var taskCancelSource = new CancellationTokenSource();
        var dequeueListLock = new object();

        try
        {
            var dequeuedList = new List<DataItem>();
            _ = Task.Run(async () =>
            {
                while (!taskCancelSource.IsCancellationRequested)
                {
                    var dequeuedItem = _fileQueue.Dequeue();
                    if (dequeuedItem != null)
                    {
                        lock (dequeueListLock)
                        {
                            dequeuedList.Add(dequeuedItem);
                        }
                    }
                    else
                    {
                        await Task.Delay(100, taskCancelSource.Token);
                    }
                }
            }, taskCancelSource.Token);

            var enqueuedList = new List<DataItem>();

            var numItems = 5;
            var rnd = new Random();
            for (int i = 0; i < numItems; i++)
            {
                var item = new byte[4];
                rnd.NextBytes(item);

                enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
            }

            foreach (var item in enqueuedList)
            {
                _fileQueue.Enqueue(item);
                _fileQueue.FlushEnqueues();
            }

            Assert.True(SpinWait.SpinUntil(() =>
            {
                lock (dequeueListLock)
                {
                    return dequeuedList.Count == enqueuedList.Count;
                }
            }, 1000));

            TestHelper.AssertEqual(enqueuedList, dequeuedList);
        }
        finally
        {
            taskCancelSource.Cancel();
        }
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public void FileQueue_EnqueueThenDisposeQueueThenDequeue_Success()
    {
        var enqueuedData = new DataItem(DataItemVersion.V1, new byte[] { 1, 5, 9, 3 });

        _fileQueue.Enqueue(enqueuedData);
        _fileQueue.FlushEnqueues();

        DisposeDefaultFileQueue();
        _fileQueue = GenerateFileQueue();

        var dequeuedData = _fileQueue.Dequeue();

        TestHelper.AssertEqual(enqueuedData, dequeuedData);
    }

    [Fact]
    public void FileQueue_DequeueNotFlushed_DataDequeuedAgainAfterRestart()
    {
        var enqueuedData = new DataItem(DataItemVersion.V1, new byte[] { 1, 5, 9, 3 });

        _fileQueue.Enqueue(enqueuedData);
        _fileQueue.FlushEnqueues();

        var dequeuedDataBeforeRestart = _fileQueue.Dequeue();

        TestHelper.AssertEqual(enqueuedData, dequeuedDataBeforeRestart);

        DisposeDefaultFileQueue();
        _fileQueue = GenerateFileQueue();

        var dequeuedDataAfterNoFlushRestart = _fileQueue.Dequeue();
        TestHelper.AssertEqual(enqueuedData, dequeuedDataAfterNoFlushRestart); // Since the previous dequeue was not flushed we should dequeue the item again

        _fileQueue.FlushDequeues();
        DisposeDefaultFileQueue();
        _fileQueue = GenerateFileQueue();

        var dequeuedDataAfterFlushAndRestart = _fileQueue.Dequeue();
        Assert.Null(dequeuedDataAfterFlushAndRestart); // Since the previous dequeue was flushed we should not dequeue the item again
    }

    /// <summary>
    /// Tests that when we dequeue enough items to move to a new file, but do not flush the dequeues, the dequeued items are still available on queue restart
    /// </summary>
    [Fact]
    public void FileQueue_DequeueGreaterThanFileSizeNotFlushed_DataDequeuedAgainAfterRestart()
    {
        // Generate more than 1mb in messages
        var msgSize = 192 * 1024;
        var numMessages = ((1 * 1024 * 1024) / msgSize) * 4;

        // Enqueue the data
        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, msgSize);
        foreach (var msg in enqueuedList)
        {
            _fileQueue.Enqueue(msg);
        }

        _fileQueue.FlushEnqueues();

        // Dequeue and ensure all of the data is received, but do not flush
        var dequeuedDataBeforeRestart = new List<DataItem>();
        while (true)
        {
            var msg = _fileQueue.Dequeue();
            if (msg == null) break;

            dequeuedDataBeforeRestart.Add(msg);
        }

        TestHelper.AssertEqual(enqueuedList, dequeuedDataBeforeRestart);

        // Restart the queue
        _fileQueue.Dispose();
        _fileQueue = GenerateFileQueue(DefaultPartition, 1);

        // Dequeue and ensure all of the data is received after restart
        var dequeuedDataAfterRestart = new List<DataItem>();
        while (true)
        {
            var msg = _fileQueue.Dequeue();
            if (msg == null) break;

            dequeuedDataAfterRestart.Add(msg);
        }

        TestHelper.AssertEqual(enqueuedList, dequeuedDataAfterRestart);

        // Flush and restart the queue again, check that there is nothing left to dequeue
        _fileQueue.FlushDequeues();
        _fileQueue.Dispose();
        _fileQueue = GenerateFileQueue(DefaultPartition, 1);

        var dequeuedDataAfterFlushAndRestart = _fileQueue.Dequeue();
        Assert.Null(dequeuedDataAfterFlushAndRestart); // Since the previous dequeue was flushed we should not dequeue the item again
    }

    #endregion

    #region Max Number of Queue Files Tests

    /// <summary>
    /// Expected behavior: when the FileQueue reaches the queue file limit, the oldest queue file is deleted to make room for the new queue file.
    /// </summary>
    [Fact]
    public void FileQueue_ReachesNumberOfQueueFilesLimit_MovesReaderToNewFileAndDeletesOldData()
    {
        var maxQueueFiles = 3;
        var queueFileSize = 1; // MB

        _fileQueue.Dispose();

        var testLogger = new TestLogger();
        _fileQueue = GenerateFileQueue(DefaultPartition, queueFileSize, maxQueueFiles, testLogger);

        // Prepare more messages than will fit in the given maximum queue size
        var queueFileSizeBytes = queueFileSize * 1024 * 1024;
        var msgSize = DefaultQueueFileSizeMb * queueFileSizeBytes / 10;
        var numMessages = 50;
        var totalMessageSizeBytes = numMessages * (msgSize + Serializer.DataItemSerializedOverhead);
        Assert.True(totalMessageSizeBytes > maxQueueFiles * queueFileSizeBytes);

        var enqueuedList = new List<DataItem>();
        var rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgSize];
            rnd.NextBytes(item);

            enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
        }

        // Enqueue the messages
        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        // Check for appropriate message logged
        var logMessages = testLogger.GetLogMessages();

        Assert.NotEmpty(logMessages);
        Assert.Equal(LogLevel.Information, logMessages[3].LogLevel);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, FileManager.MaxBufferSizeReachedMessage, "", DefaultPartition + "/_data0"),
                     logMessages[3].LogMessage);

        // Expected behavior: when the FileQueue reaches the queue file limit, the oldest queue file is deleted to make room for the new queue file.
        // Determine which items we expect were discarded so we can determine what we expect to dequeue
        var itemsInCurrentFile = 0;
        var bytesInCurrentFile = 0;
        var fileItemList = new List<int>();
        foreach (var item in enqueuedList)
        {
            var serializedSize = item.Data.Length + Serializer.DataItemSerializedOverhead;
            if (bytesInCurrentFile + serializedSize < queueFileSizeBytes)
            {
                itemsInCurrentFile++;
                bytesInCurrentFile += serializedSize;
            }
            else
            {
                fileItemList.Add(itemsInCurrentFile);
                itemsInCurrentFile = 1;
                bytesInCurrentFile = serializedSize;
            }
        }

        fileItemList.Add(itemsInCurrentFile);

        Assert.True(fileItemList.Count > maxQueueFiles);

        var numFilesThatShouldHaveBeenDiscarded = fileItemList.Count - maxQueueFiles;
        var numItemsToRemove = 0;
        for (int i = 0; i < numFilesThatShouldHaveBeenDiscarded; i++)
        {
            numItemsToRemove += fileItemList[i];
        }

        enqueuedList.RemoveRange(0, numItemsToRemove);

        // Now let's actually dequeue items and ensure they match what we expected to dequeue
        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        _fileQueue.FlushDequeues();

        TestHelper.AssertEqual(enqueuedList, dequeuedList);

        // Let's sanity check that the queue still works now that the dequeues have been flushed
        // Use a number and size of messages so that we won't hit the limit
        RunEnqueueDequeueTest(queueFileSize * 1024 * 1024 / 10, (maxQueueFiles - 1) * 10);
    }

    /// <summary>
    /// This is to ensure the edge case of a maxQueueFiles limit of 1 behaves as expected.
    /// </summary>
    [Fact]
    public void FileQueue_ReachesNumberOfQueueFilesLimit_LimitIsOne_MovesReaderToNewFileAndDeletesOldData()
    {
        var maxQueueFiles = 1;
        var queueFileSize = 1; // MB
        var queueFileSizeBytes = queueFileSize * 1024 * 1024;

        _fileQueue.Dispose();

        var testLogger = new TestLogger();
        _fileQueue = GenerateFileQueue(DefaultPartition, queueFileSize, maxQueueFiles, testLogger);

        // Prepare more messages than will fit in the given maximum queue size
        var msgSize = DefaultQueueFileSizeMb * queueFileSizeBytes / 10;
        var numMessages = 50;
        var totalMessageSizeBytes = numMessages * (msgSize + Serializer.DataItemSerializedOverhead);
        Assert.True(totalMessageSizeBytes > maxQueueFiles * queueFileSizeBytes);

        var enqueuedList = new List<DataItem>();
        var rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgSize];
            rnd.NextBytes(item);

            enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
        }

        // Enqueue the messages
        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        // Check for appropriate message logged
        var logMessages = testLogger.GetLogMessages();

        Assert.NotEmpty(logMessages);
        Assert.Equal(LogLevel.Information, logMessages[1].LogLevel);
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, FileManager.MaxBufferSizeReachedMessage, "", DefaultPartition + "/_data0"),
                     logMessages[1].LogMessage); 

        // Expected behavior: when the FileQueue reaches the queue file limit, the oldest queue file is deleted to make room for the new queue file.
        // Determine which items we expect were discarded so we can determine what we expect to dequeue
        var itemsInCurrentFile = 0;
        var bytesInCurrentFile = 0;
        var fileItemList = new List<int>();
        foreach (var item in enqueuedList)
        {
            var serializedSize = item.Data.Length + Serializer.DataItemSerializedOverhead;
            if (bytesInCurrentFile + serializedSize < queueFileSizeBytes)
            {
                itemsInCurrentFile++;
                bytesInCurrentFile += serializedSize;
            }
            else
            {
                fileItemList.Add(itemsInCurrentFile);
                itemsInCurrentFile = 1;
                bytesInCurrentFile = serializedSize;
            }
        }

        fileItemList.Add(itemsInCurrentFile);

        Assert.True(fileItemList.Count > maxQueueFiles);

        var numFilesThatShouldHaveBeenDiscarded = fileItemList.Count - maxQueueFiles;
        var numItemsToRemove = 0;
        for (int i = 0; i < numFilesThatShouldHaveBeenDiscarded; i++)
        {
            numItemsToRemove += fileItemList[i];
        }

        enqueuedList.RemoveRange(0, numItemsToRemove);

        // Now let's actually dequeue items and ensure they match what we expected to dequeue
        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        _fileQueue.FlushDequeues();

        TestHelper.AssertEqual(enqueuedList, dequeuedList);

        // Let's sanity check that the queue still works now that the dequeues have been flushed
        // Use a number and size of messages so that we won't hit the limit
        // Note: This is a special case. At time of writing, at this point in the test
        //  one queue file is partially full. Since only one queue file is allowed, once the partially full
        //  queue file is filled, the file will be deleted for a new queue file. So the test below will only
        //  pass if we do not fill up the current partially filled queue file, which is why we only use 3 messages of small size.
        RunEnqueueDequeueTest(queueFileSize * 1024 * 1024 / 10, 3);
    }

    /// <summary>
    /// Expected behavior: when the FileQueue is restarted and has a lower file limit than the number of data files currently existing,
    ///  the files which violate the limit will be removed and the queue will function correctly as if it always had the lower limit.
    /// </summary>
    [Fact]
    public void FileQueue_RestartedWithMoreQueueFilesThanLimit_BehavesAsIfWasAlwaysLowerLimit()
    {
        // Limits which will be imposed AFTER restart
        var maxQueueFiles = 3;
        var queueFileSize = 1; // MB

        // Prepare more messages than will fit in the future FQ maximum queue files limit
        // The default FQ being used before restart does not have a maximum queue files limit
        var queueFileSizeBytes = queueFileSize * 1024 * 1024;
        var msgSize = DefaultQueueFileSizeMb * queueFileSizeBytes / 10;
        var numMessages = 50;
        var totalMessageSizeBytes = numMessages * (msgSize + Serializer.DataItemSerializedOverhead);
        Assert.True(totalMessageSizeBytes > maxQueueFiles * queueFileSizeBytes);

        var enqueuedList = new List<DataItem>();
        var rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgSize];
            rnd.NextBytes(item);

            enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
        }

        // Enqueue the messages
        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        // Restart the queue with a file number limit lower than the number of files currently used for enqueued data
        _fileQueue.Dispose();
        _fileQueue = GenerateFileQueue(DefaultPartition, queueFileSize, maxQueueFiles);

        // Expected behavior: the FQ will behave as if it always had that lower maximum queue file limit. So the old files violating the limit will be deleted.
        // Determine which items we expect were discarded so we can determine what we expect to dequeue
        var itemsInCurrentFile = 0;
        var bytesInCurrentFile = 0;
        var fileItemList = new List<int>();
        foreach (var item in enqueuedList)
        {
            var serializedSize = item.Data.Length + Serializer.DataItemSerializedOverhead;
            if (bytesInCurrentFile + serializedSize < queueFileSizeBytes)
            {
                itemsInCurrentFile++;
                bytesInCurrentFile += serializedSize;
            }
            else
            {
                fileItemList.Add(itemsInCurrentFile);
                itemsInCurrentFile = 1;
                bytesInCurrentFile = serializedSize;
            }
        }

        fileItemList.Add(itemsInCurrentFile);

        Assert.True(fileItemList.Count > maxQueueFiles);

        var numFilesThatShouldHaveBeenDiscarded = fileItemList.Count - maxQueueFiles;
        var numItemsToRemove = 0;
        for (int i = 0; i < numFilesThatShouldHaveBeenDiscarded; i++)
        {
            numItemsToRemove += fileItemList[i];
        }

        enqueuedList.RemoveRange(0, numItemsToRemove);

        // Now let's actually dequeue items and ensure they match what we expected to dequeue
        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        _fileQueue.FlushDequeues();

        TestHelper.AssertEqual(enqueuedList, dequeuedList);

        // Let's sanity check that the queue still works now that the dequeues have been flushed
        // Use a number and size of messages so that we won't hit the limit
        RunEnqueueDequeueTest(queueFileSize * 1024 * 1024 / 10, (maxQueueFiles - 1) * 10);
    }

    #endregion

    #region Trying To Break It Tests

    [Fact]
    public void FileQueue_ConcurrentEnqueueDequeue_StressTest()
    {
        using var cancelTokenSource = new CancellationTokenSource();
        try
        {
            var dequeuedList = new List<DataItem>();
            int numDequeued = 0;
            var drainTask = new Task(() =>
            {
                while (!cancelTokenSource.IsCancellationRequested)
                {
                    var data = _fileQueue.Dequeue();
                    if (data != null)
                    {
                        dequeuedList.Add(data);
                        numDequeued++;
                    }
                }
            }, cancelTokenSource.Token);

            var enqueuedList = TestHelper.GenerateRandomDataItems(1000, 192 * 1024); // 1000 messages, 192 KB each
            drainTask.Start();

            int i = 0;
            foreach (var msg in enqueuedList)
            {
                i++;
                _fileQueue.Enqueue(msg);
                if (i % 50 == 0) // Flush every 50 messages to see if an issue happens during periodic flushing
                {
                    _fileQueue.FlushEnqueues();
                }
            }

            _fileQueue.FlushEnqueues();

            Assert.True(SpinWait.SpinUntil(() => numDequeued == enqueuedList.Count, 5000), "Not all items were dequeued.");
            TestHelper.AssertEqual(enqueuedList, dequeuedList);
        }
        finally
        {
            cancelTokenSource.Cancel();
        }
    }

    [Fact]
    public void FileQueue_EnqueueMoreThanFileSize_ItemsSuccessfullyDequeued()
    {
        var msgSize = DefaultQueueFileSizeMb * 1024 * 1024 / 10;
        var numMessages = 20;
        Assert.True(numMessages * msgSize > DefaultQueueFileSizeMb * 1024 * 1024);
        RunEnqueueDequeueTest(msgSize, numMessages);
    }

    [Fact]
    public void FileQueue_CorruptFirstDataFileByShortening_ExpectedItemsSuccessfullyDequeued()
    {
        // Generate and enqueue
        var numMessages = 15;
        var messageSize = 200;
        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        // Stop the queue
        _fileQueue.Dispose();

        // We'll shorten the first data file
        var dataFileLocation = Path.Combine(DefaultPartition, GetDataFileName(0));
        if (!File.Exists(dataFileLocation)) throw new Exception("Test has the wrong location for the data file.");

        // Shorten the file to "corrupt" it
        var testStream = WaitOpen(dataFileLocation);
        var numMessagesStillInQueue = 4; // Number of complete messages in the queue after we shorten it
        var newFileLength = ((messageSize + Serializer.DataItemSerializedOverhead) * numMessagesStillInQueue) + (messageSize / 2);
        testStream.SetLength(newFileLength);
        testStream.Dispose();

        // Reset our expectations of what to receive...
        var expectedToBeDequeued = new List<DataItem>();
        for (int i = 0; i < numMessagesStillInQueue; i++)
        {
            expectedToBeDequeued.Add(enqueuedList[i]);
        }

        // Restart the FileQueue
        _fileQueue = GenerateFileQueue();

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(expectedToBeDequeued, dequeuedList);
    }

    [Fact]
    public void FileQueue_CorruptFirstDataFileByShortening_EnqueueBeforeDequeue_ExpectedItemsSuccessfullyDequeued()
    {
        // Generate and enqueue
        var numMessages = 15;
        var messageSize = 200;
        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        // Stop the queue
        _fileQueue.Dispose();

        // We'll shorten the first data file
        var dataFileLocation = Path.Combine(DefaultPartition, GetDataFileName(0));
        if (!File.Exists(dataFileLocation)) throw new Exception("Test has the wrong location for the data file.");

        // Shorten the file to "corrupt" it
        var testStream = WaitOpen(dataFileLocation);
        var numMessagesStillInQueue = 4; // Number of complete messages in the queue after we shorten it
        var newFileLength = ((messageSize + Serializer.DataItemSerializedOverhead) * numMessagesStillInQueue) + (messageSize / 2);
        testStream.SetLength(newFileLength);
        testStream.Dispose();

        // Reset our expectations of what to receive...
        var expectedToBeDequeued = new List<DataItem>();
        for (int i = 0; i < numMessagesStillInQueue; i++)
        {
            expectedToBeDequeued.Add(enqueuedList[i]);
        }

        // Restart the FileQueue
        _fileQueue = GenerateFileQueue();

        // Add some more items to the queue
        var numNewMessages = 5;
        var newMessageSize = 500;
        var newItemsToAdd = TestHelper.GenerateRandomDataItems(numNewMessages, newMessageSize);

        foreach (var item in newItemsToAdd)
        {
            expectedToBeDequeued.Add(item);
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(expectedToBeDequeued, dequeuedList);
    }

    /// <summary>
    /// This shouldn't happen in the field, but what if it did...
    /// </summary>
    [Fact]
    public void FileQueue_DeleteStateFile_Recovers()
    {
        // Generate and enqueue enough for a second file
        var numMessages = 10;
        var messageSize = 192 * 1024;
        Assert.True(numMessages * messageSize > 1 * 1024 * 1024); // Assert we enqueue enough data for more than 1 file for a better test

        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        _fileQueue.Dispose();
        var stateFile = GetStateFilePath(DefaultPartition);
        Assert.True(File.Exists(stateFile));
        WaitDelete(stateFile);

        // Create the FQ and ensure we dequeue everything as normal
        _fileQueue = GenerateFileQueue(DefaultPartition, 1);

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(enqueuedList, dequeuedList);
    }

    [Fact]
    public void FileQueue_DeleteFirstDataFile_RestOfDataDequeued()
    {
        // Generate and enqueue enough for a second file
        var numMessages = 15;
        var messageSize = 192 * 1024;
        Assert.True(numMessages * messageSize > 1 * 1024 * 1024); // Assert we enqueue enough data for more than 1 file for a better test

        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();
        _fileQueue.Dispose();

        var dataFile = GetDataFilePath(DefaultPartition, 0);
        Assert.True(File.Exists(dataFile));
        WaitDelete(dataFile);

        // Calculate how many messages we should have lost - file size / message size
        var numMessagesInFirstDataFile = 1 * 1024 * 1024 / (messageSize + Serializer.DataItemSerializedOverhead);

        // Construct a list of messages we expect to dequeue - all of the messages except those in the first data file
        var expectedToBeDequeued = new List<DataItem>();
        int msgNum = 0;
        foreach (var msg in enqueuedList)
        {
            msgNum++;
            if (msgNum > numMessagesInFirstDataFile) expectedToBeDequeued.Add(msg);
        }

        Assert.True(expectedToBeDequeued.Count > 0);

        // Create the FQ and ensure we dequeue the remaining data
        _fileQueue = GenerateFileQueue(DefaultPartition, 1);

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(expectedToBeDequeued, dequeuedList);
    }

    [Fact]
    public void FileQueue_DeleteMiddleDataFileWhileQueueOffline_RestOfDataDequeued()
    {
        // This tests if the queue can handle a missing "middle" data file on restart
        // Generate and enqueue enough for a third file
        var numMessages = 25;
        var messageSize = 192 * 1024;
        Assert.True(numMessages * (messageSize + Serializer.DataItemSerializedOverhead) > 3 * 1024 * 1024); // Assert we enqueue enough data for more than 1 file for a better test

        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();
        _fileQueue.Dispose();

        var dataFile = GetDataFilePath(DefaultPartition, 1);
        Assert.True(File.Exists(dataFile));
        WaitDelete(dataFile);

        // Calculate how many messages we should have lost - file size / message size
        var numMessagesInDataFile = 1 * 1024 * 1024 / (messageSize + Serializer.DataItemSerializedOverhead);

        // Construct a list of messages we expect to dequeue - all of the messages except those in the second data file
        var expectedToBeDequeued = new List<DataItem>();
        int msgNum = 0;
        foreach (var msg in enqueuedList)
        {
            msgNum++;
            if (msgNum <= numMessagesInDataFile || msgNum > 2 * numMessagesInDataFile) expectedToBeDequeued.Add(msg);
        }

        Assert.True(expectedToBeDequeued.Count > 0);

        // Create the FQ and ensure we dequeue the remaining data
        _fileQueue = GenerateFileQueue(DefaultPartition, 1);

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(expectedToBeDequeued, dequeuedList);
    }

    [Fact]
    public void FileQueue_DeleteMiddleDataFileWhileQueueOnline_RestOfDataDequeued()
    {
        // This tests if the queue can handle a missing "middle" data file while remaining online
        // Generate and enqueue enough for a third file
        var numMessages = 25;
        var messageSize = 192 * 1024;
        Assert.True(numMessages * (messageSize + Serializer.DataItemSerializedOverhead) > 3 * 1024 * 1024); // Assert we enqueue enough data for more than 1 file for a better test

        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        var dataFile = GetDataFilePath(DefaultPartition, 1);
        Assert.True(File.Exists(dataFile));
        WaitDelete(dataFile);

        // Calculate how many messages we should have lost - file size / message size
        var numMessagesInDataFile = 1 * 1024 * 1024 / (messageSize + Serializer.DataItemSerializedOverhead);

        // Construct a list of messages we expect to dequeue - all of the messages except those in the second data file
        var expectedToBeDequeued = new List<DataItem>();
        int msgNum = 0;
        foreach (var msg in enqueuedList)
        {
            msgNum++;
            if (msgNum <= numMessagesInDataFile || msgNum > 2 * numMessagesInDataFile) expectedToBeDequeued.Add(msg);
        }

        Assert.True(expectedToBeDequeued.Count > 0);

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(expectedToBeDequeued, dequeuedList);
    }

    [Fact]
    public void FileQueue_DeleteFirstDataFile_EnqueueBeforeDequeue_ExpectedItemsSuccessfullyDequeued()
    {
        // Generate and enqueue enough for a second file
        var numMessages = 15;
        var messageSize = 192 * 1024;
        Assert.True(numMessages * messageSize > 1 * 1024 * 1024); // Assert we enqueue enough data for more than 1 file for a better test

        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();
        _fileQueue.Dispose();

        var dataFile = GetDataFilePath(DefaultPartition, 0);
        Assert.True(File.Exists(dataFile));
        WaitDelete(dataFile);

        // Calculate how many messages we should have lost
        var numMessagesInFirstDataFile = 1 * 1024 * 1024 / (messageSize + Serializer.DataItemSerializedOverhead);

        // Construct a list of messages we expect to dequeue - all of the messages except those in the first data file
        var expectedToBeDequeued = new List<DataItem>();
        int msgNum = 0;
        foreach (var msg in enqueuedList)
        {
            msgNum++;
            if (msgNum > numMessagesInFirstDataFile) expectedToBeDequeued.Add(msg);
        }

        Assert.True(expectedToBeDequeued.Count > 0);

        // Create the FQ
        _fileQueue = GenerateFileQueue(DefaultPartition, 1);

        // Enqueue some more data
        var newMessages = TestHelper.GenerateRandomDataItems(10, 192 * 512);
        foreach (var msg in newMessages)
        {
            _fileQueue.Enqueue(msg);
            expectedToBeDequeued.Add(msg);
        }

        _fileQueue.FlushEnqueues();

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(expectedToBeDequeued, dequeuedList);
    }

    [Fact]
    public void FileQueue_CorruptBeginningAndEndOfDataFile_RestOfDataDequeued()
    {
        // Generate and enqueue enough messages for a second file
        var numMessages = 30;
        var messageSize = 192 * 1024;
        Assert.True(numMessages * messageSize > 3 * 1024 * 1024); // Assert we enqueue enough data for more than 1 file for a better test

        var enqueuedList = TestHelper.GenerateRandomDataItems(numMessages, messageSize);

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();
        _fileQueue.Dispose();

        var dataFile = GetDataFilePath(DefaultPartition, 0);
        Assert.True(File.Exists(dataFile));

        // We want to corrupt the header or footer of a data item because the queue will detect that
        // Modification of a data item body would not be detected - corruption from partial data item being written is 
        //  addressed in "Corrupt*ByShortening" tests
        // Corrupt the beginning of the file
        using var stream = File.Open(dataFile, FileMode.Open, FileAccess.Write);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);

        // Corrupt the end of the file
        stream.Position = stream.Length - 4;
        for (int i = 0; i < 4; i++)
        {
            stream.WriteByte(0);
        }

        stream.Flush(true);
        stream.Dispose();

        // Construct a list of messages we expect to dequeue - all of the messages except the one in the beginning and end of first data file
        var numMessagesInFirstDataFile = 1 * 1024 * 1024 / (messageSize + Serializer.DataItemSerializedOverhead);
        var expectedToBeDequeued = new List<DataItem>();
        int msgNum = 0;
        foreach (var msg in enqueuedList)
        {
            msgNum++;
            if (msgNum == 1 || msgNum == numMessagesInFirstDataFile) continue;
            expectedToBeDequeued.Add(msg);
        }

        Assert.True(expectedToBeDequeued.Count > 0);
        Assert.Equal(enqueuedList.Count - 2, expectedToBeDequeued.Count);

        // Create the FQ and enqueue some more data
        _fileQueue = GenerateFileQueue(DefaultPartition, 1);

        var newMessages = TestHelper.GenerateRandomDataItems(5, 1024);
        foreach (var msg in newMessages)
        {
            expectedToBeDequeued.Add(msg);
            _fileQueue.Enqueue(msg);
        }

        _fileQueue.FlushEnqueues();

        // Ensure we dequeue all expected items
        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(expectedToBeDequeued, dequeuedList);
    }

    /// <summary>
    /// The expected behavior of the FileQueue is that if we have multiple data files and hit a disk space error, we delete the oldest  data file and enqueue the item.
    /// Generally people care more about their new data than their old data.
    /// </summary>
    [Fact]
    public async Task FileQueue_RunsOutOfDiskSpace_DiskSpaceLargerThanQueueFileSize_DeletesOldestDataKeepsNewestData()
    {
        var totalDiskSpaceMB = 3; // MB
        var totalDiskSpaceBytes = totalDiskSpaceMB * 1024 * 1024;
        var queueFileSize = 1; // MB

        var eventProviderMock = new Mock<IEdgeEventProvider>();
        var channel = Channel.CreateBounded<IEdgeEvent>(20);
        eventProviderMock.SetupGet(ep => ep.EdgeEventChannel).Returns(channel);

        _fileQueue.Dispose();
        var fakeFileSystem = new FakeFileSystem(totalDiskSpaceMB);
        _fileQueue = new FileQueue(new FileManager(new FakeFileSystemInteractor(fakeFileSystem), DefaultPartition), new Serializer(), queueFileSize, 0, null, eventProviderMock.Object);

        var queueFileSizeBytes = DefaultQueueFileSizeMb * 1024 * 1024;
        var msgSize = DefaultQueueFileSizeMb * queueFileSizeBytes / 10;
        var numMessages = 50;
        var totalMessageSizeBytes = numMessages * (msgSize + Serializer.DataItemSerializedOverhead);
        Assert.True(totalMessageSizeBytes > totalDiskSpaceBytes);

        var enqueuedList = new List<DataItem>();

        var rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgSize];
            rnd.NextBytes(item);

            enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
        }

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        // Expected behavior: when encountering an out of disk space exception, the queue deletes the oldest queue file and enqueues the new data
        // With a queue file size of 1 MB, and a disk space of 3 MB, we expect to have at most 3 queue files
        // Simulate the operation to see what items we expect to dequeue...
        var itemsInCurrentFile = 0;
        var bytesInCurrentFile = 0;
        var fileItemList = new List<int>();
        foreach (var item in enqueuedList)
        {
            var serializedSize = item.Data.Length + Serializer.DataItemSerializedOverhead;
            if (bytesInCurrentFile + serializedSize < queueFileSizeBytes)
            {
                itemsInCurrentFile++;
                bytesInCurrentFile += serializedSize;
            }
            else
            {
                fileItemList.Add(itemsInCurrentFile);
                itemsInCurrentFile = 1;
                bytesInCurrentFile = serializedSize;
            }
        }

        fileItemList.Add(itemsInCurrentFile);

        var expectedRemainingQueueFiles = totalDiskSpaceMB / DefaultQueueFileSizeMb;
        Assert.True(fileItemList.Count > expectedRemainingQueueFiles);

        var numFilesExpectedToBeDiscarded = fileItemList.Count - expectedRemainingQueueFiles;
        var numItemsToRemove = 0;
        for (int i = 0; i < numFilesExpectedToBeDiscarded; i++)
        {
            numItemsToRemove += fileItemList[i];
        }

        enqueuedList.RemoveRange(0, numItemsToRemove);

        // Now let's actually dequeue items and ensure they match what we expected to dequeue
        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        _fileQueue.FlushDequeues();

        TestHelper.AssertEqual(enqueuedList, dequeuedList);

        // Let's test that the queue still works now that the dequeues have been flushed
        RunEnqueueDequeueTest(queueFileSize * 1024 * 1024 / 10, 20);

        var events = channel.Reader.ReadAllAsync();
        var expectedEventCount = 17;
        var expectedMaxFileCount = 3;
        var expectedMinFileCount = 1;
        var eventCount = 0;

        while (channel.Reader.TryRead(out var item)) 
        {
            eventCount++;
            Assert.Equal(EdgeEventType.Buffering, item.EventType);
            Assert.False(((BufferFileCreatedDeletedInfo)item).NumberOfFiles < expectedMinFileCount && ((BufferFileCreatedDeletedInfo)item).NumberOfFiles > expectedMaxFileCount);
        }

        Assert.Equal(expectedEventCount, eventCount);
    }

    /// <summary>
    /// The expected behavior of FileQueue in the case that there is only one data file being used is that it attempts to retry the enqueue. If it doesn't work on retry,
    /// throw an exception. Do not discard items which were not flushed. Once the disk space has increased the queue should function normally again and successfully dequeue
    /// items which were enqueued prior to the disk exception.
    /// </summary>
    [Fact]
    public void FileQueue_RunsOutOfDiskSpace_OnlyOneDataFile_QueueThrowsException_WhenDiskSpaceExpandedFlushesEnqueueItemsSuccessfully_NoItemsDropped()
    {
        var totalDiskSpaceMB = 1; // MB
        var totalDiskSpaceBytes = totalDiskSpaceMB * 1024 * 1024;
        var expandedDiskSpaceMB = 5;
        var expandedDiskSpaceBytes = expandedDiskSpaceMB * 1024 * 1024;
        var queueFileSize = 2;

        _fileQueue.Dispose();
        var fakeFileSystem = new FakeFileSystem(totalDiskSpaceMB);
        var serializer = new Serializer();
        _fileQueue = new FileQueue(new FileManager(new FakeFileSystemInteractor(fakeFileSystem), DefaultPartition), serializer, queueFileSize, 0);

        var queueFileSizeBytes = queueFileSize * 1024 * 1024;
        var msgSize = queueFileSize * queueFileSizeBytes / 10;
        var numMessages = expandedDiskSpaceBytes / msgSize; // Later in test we expand the disk space and assume all messages fit on the expanded disk size

        var totalMessageSizeBytes = numMessages * (msgSize + Serializer.DataItemSerializedOverhead);
        Assert.True(totalMessageSizeBytes > totalDiskSpaceBytes);

        var enqueuedList = new List<DataItem>();

        var rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgSize];
            rnd.NextBytes(item);

            enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
        }

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        // Expected behavior: when encountering an out of disk space exception, and there is only a single data file,
        // the queue attempts to retry the enqueue then throws. We will expect to dequeue only the items in the first data file.

        // Determine the overhead for serialized items
        int serializationOverhead;
        using (var ms = new MemoryStream())
        {
            serializer.SerializeDataItem(ms, new DataItem(DataItemVersion.V1, Array.Empty<byte>()));
            serializationOverhead = (int)ms.Position;
        }

        var expectedToDequeue = new List<DataItem>();
        var enqueuedBytes = 0;
        foreach (var item in enqueuedList)
        {
            var serializedBytes = item.Data.Length + serializationOverhead;
            if (enqueuedBytes + serializedBytes > totalDiskSpaceBytes) break;
            expectedToDequeue.Add(item);
            enqueuedBytes += serializedBytes;
        }

        bool thrown = false;
        try
        {
            _fileQueue.FlushEnqueues();
        }
        catch (IOException)
        {
            thrown = true;
        }

        Assert.True(thrown);

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        _fileQueue.FlushDequeues();

        TestHelper.AssertEqual(expectedToDequeue, dequeuedList);

        // When more disk space is available, we should be able to flush the remaining items
        fakeFileSystem.SetDiskSize(expandedDiskSpaceMB);
        _fileQueue.FlushEnqueues();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        _fileQueue.FlushDequeues();

        // Now we should have dequeued all data items
        TestHelper.AssertEqual(enqueuedList, dequeuedList);

        RunEnqueueDequeueTest(queueFileSize * 1024 * 1024 / 10, 5);
    }

    #endregion

    #region Dispose pattern

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _fileQueue?.Dispose();
                DisposeDefaultFileQueue();
            }

            _disposed = true;
        }
    }

    #endregion

    #region Helpers
    #region Private Static Methods"
    private static FileStream WaitOpen(string path)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
                return stream;
            }
            catch
            {
                Thread.Sleep(100);
            }
        }

        return File.Open(path, FileMode.Open, FileAccess.ReadWrite);
    }

    private static void WaitDelete(string path)
    {
        for (int i = 0; i < 4; i++)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                Thread.Sleep(100);
            }
        }

        File.Delete(path);
    }

    private static string GetStateFilePath(string dir)
    {
        return Path.Combine(dir, "_state");
    }

    private static string GetDataFilePath(string dir, int fileNumber)
    {
        return Path.Combine(dir, GetDataFileName(fileNumber));
    }

    private static string GetDataFileName(int fileNumber)
    {
        return "_data" + fileNumber;
    }
    #endregion

    #region Private Methods"
    private void DisposeDefaultFileQueue()
    {
        _fileQueue?.Dispose();
        _fileQueue = null;
    }

    private void RunEnqueueDequeueTest(int msgSize, int numMessages)
    {
        var enqueuedList = new List<DataItem>();

        var rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgSize];
            rnd.NextBytes(item);

            enqueuedList.Add(new DataItem(DataItemVersion.V1, item));
        }

        foreach (var item in enqueuedList)
        {
            _fileQueue.Enqueue(item);
        }

        _fileQueue.FlushEnqueues();

        var dequeuedList = new List<DataItem>();
        while (true)
        {
            var dequeuedItem = _fileQueue.Dequeue();
            if (dequeuedItem == null) break;

            dequeuedList.Add(dequeuedItem);
        }

        TestHelper.AssertEqual(enqueuedList, dequeuedList);
    }
    #endregion

    #endregion
}
