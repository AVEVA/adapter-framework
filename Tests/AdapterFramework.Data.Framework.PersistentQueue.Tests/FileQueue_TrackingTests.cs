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
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

public sealed class FileQueue_TrackingTests : IDisposable
{
    private const int FileSizeMb = 1;
    private const int FileSizeBytes = FileSizeMb * 1024 * 1024;
    private const int SmallItemBytes = 100;

    private readonly string _directory = Path.Combine("trackingPartition", Path.GetRandomFileName());
    private readonly List<TrackedItemsLostEventArgs> _lost = new();
    private FileQueue _fileQueue;

    /// <summary>
    /// Tracked items that are dequeued, including across a file change and after a peek, are never reported as lost.
    /// </summary>
    [Fact]
    public void Dequeue_TrackedItems_NotReported()
    {
        var queue = CreateQueue();
        queue.Enqueue(Tracked(Guid.NewGuid(), FileSizeBytes));
        queue.Enqueue(Tracked(Guid.NewGuid(), SmallItemBytes)); // Second file.
        queue.Enqueue(Untracked(SmallItemBytes));
        queue.FlushEnqueues();

        Assert.NotNull(queue.Peek());
        Assert.NotNull(queue.Dequeue());
        Assert.NotNull(queue.Dequeue());
        Assert.NotNull(queue.Dequeue());
        Assert.Null(queue.Dequeue());
        Assert.Empty(_lost);
    }

    /// <summary>
    /// A tracked record the reader skips as corrupt is reported as unreadable, and the next tracked record is not.
    /// </summary>
    [Fact]
    public void Dequeue_CorruptTrackedRecord_ReportedUnreadable()
    {
        var corrupt = Guid.NewGuid();
        var queue = CreateQueue();
        queue.Enqueue(Tracked(corrupt, SmallItemBytes));
        queue.Enqueue(Tracked(Guid.NewGuid(), SmallItemBytes));
        queue.Enqueue(Untracked(FileSizeBytes)); // Moves the writer off the first file so it can be edited.
        queue.FlushEnqueues();

        using (var stream = File.Open(Path.Combine(_directory, "_data0"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            stream.Position = 12; // First data byte of the first record.
            stream.WriteByte(0xFF);
        }

        Assert.Equal(SmallItemBytes, queue.Dequeue().Data.Length);
        Assert.Equal(FileSizeBytes, queue.Dequeue().Data.Length);

        var lost = Assert.Single(_lost);
        Assert.Equal(TrackedItemLossReason.Unreadable, lost.Reason);
        Assert.Equal(new[] { corrupt }, lost.TrackingIds);
    }

    /// <summary>
    /// A tracked item in the file the queue drops to stay within the file limit is reported as evicted.
    /// </summary>
    [Fact]
    public void FlushEnqueues_FileLimitReached_TrackedItemsReportedEvicted()
    {
        var evicted = Guid.NewGuid();
        var queue = CreateQueue(maxQueueFiles: 2);
        queue.Enqueue(Tracked(evicted, FileSizeBytes));
        queue.Enqueue(Untracked(FileSizeBytes));
        queue.Enqueue(Untracked(FileSizeBytes));
        queue.FlushEnqueues();

        var lost = Assert.Single(_lost);
        Assert.Equal(TrackedItemLossReason.Evicted, lost.Reason);
        Assert.Equal(new[] { evicted }, lost.TrackingIds);
    }

    /// <summary>
    /// Tracked items in files deleted by lowering the file limit are reported as evicted.
    /// </summary>
    [Fact]
    public void UpdateMaxQueueFiles_FilesDeleted_TrackedItemsReportedEvicted()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var queue = CreateQueue();
        queue.Enqueue(Tracked(first, FileSizeBytes));
        queue.Enqueue(Tracked(second, FileSizeBytes));
        queue.Enqueue(Untracked(FileSizeBytes));
        queue.FlushEnqueues();

        queue.UpdateMaxQueueFiles(1);

        var lost = Assert.Single(_lost);
        Assert.Equal(TrackedItemLossReason.Evicted, lost.Reason);
        Assert.Equal(new[] { first, second }, lost.TrackingIds);
        Assert.NotNull(queue.Dequeue());
        Assert.Single(_lost);
    }

    /// <summary>
    /// Tracked items still in the queue when the buffers are deleted are reported as cleared.
    /// </summary>
    [Fact]
    public void DeleteBuffers_TrackedItemsReportedCleared()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var queue = CreateQueue();
        queue.Enqueue(Tracked(first, SmallItemBytes));
        queue.Enqueue(Untracked(SmallItemBytes));
        queue.Enqueue(Tracked(second, SmallItemBytes));
        queue.FlushEnqueues();

        queue.DeleteBuffers();

        var lost = Assert.Single(_lost);
        Assert.Equal(TrackedItemLossReason.Cleared, lost.Reason);
        Assert.Equal(new[] { first, second }, lost.TrackingIds);
    }

    /// <summary>
    /// Disposes the queue and deletes its directory.
    /// </summary>
    public void Dispose()
    {
        _fileQueue?.Dispose();
        TestHelper.DeleteDirectoryWithRetry(_directory);
    }

    /// <summary>
    /// Creates the queue under test and records every loss it reports.
    /// </summary>
    /// <param name="maxQueueFiles">The file limit, or 0 for none.</param>
    /// <returns>The queue under test.</returns>
    private FileQueue CreateQueue(int maxQueueFiles = 0)
    {
        _fileQueue = FileQueue_IntegrationTests.GenerateFileQueue(_directory, FileSizeMb, maxQueueFiles);
        _fileQueue.TrackedItemsLost += (_, e) => _lost.Add(e);
        return _fileQueue;
    }

    /// <summary>
    /// Creates a V4 item with a tracking ID.
    /// </summary>
    /// <param name="id">The tracking ID.</param>
    /// <param name="size">The data length.</param>
    /// <returns>The item.</returns>
    private static DataItem Tracked(Guid id, int size) => new(DataItemVersion.V4, new byte[size]) { TrackingId = id };

    /// <summary>
    /// Creates a V3 item without a tracking ID.
    /// </summary>
    /// <param name="size">The data length.</param>
    /// <returns>The item.</returns>
    private static DataItem Untracked(int size) => new(DataItemVersion.V3, new byte[size]);
}
