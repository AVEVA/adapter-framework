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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Buffering.Tests;

public class BackedUpOmfMessageQueue_Lease_Tests : IDisposable
{
    private const int OneMb = 1;
    private readonly TimeSpan _messageExpirationTime = TimeSpan.FromSeconds(10);
    private readonly string _bufferDirectory = Path.Combine(Path.GetTempPath(), "AdapterFramework.LeaseTests", Guid.NewGuid().ToString("N"));
    private readonly TestLogger _testLogger = new();
    private readonly List<IDisposable> _persistentResources = new();
    private FileQueue _lastFileQueue;
    private bool _disposed;

    [Fact]
    public void BackedUpOmfMessageQueue_TryLease_Complete_RemovesOnlyLeasedMessage()
    {
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);
        queue.Enqueue(CreateMessage(1));
        queue.Enqueue(CreateMessage(2));

        Assert.True(queue.TryLease(out var lease));
        AssertMarker(1, lease.Message);
        Assert.True(queue.Complete(lease));

        Assert.True(queue.TryLease(out lease));
        AssertMarker(2, lease.Message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_TryLease_EmptyQueue_ReturnsFalse()
    {
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);

        Assert.False(queue.TryLease(out var lease));
        Assert.Null(lease);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_Abandon_SameMessageLeasedAgainBeforeNewerMessages()
    {
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);
        queue.Enqueue(CreateMessage(1));

        Assert.True(queue.TryLease(out var firstLease));
        queue.Enqueue(CreateMessage(2));
        queue.Abandon(firstLease);

        Assert.True(queue.TryLease(out var secondLease));
        AssertMarker(1, secondLease.Message);
        Assert.NotEqual(firstLease.Token, secondLease.Token);

        Assert.False(queue.Complete(firstLease));
        Assert.True(queue.Complete(secondLease));
        Assert.False(queue.Complete(secondLease));

        Assert.True(queue.TryLease(out var thirdLease));
        AssertMarker(2, thirdLease.Message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_TryLease_LeaseOutstanding_Throws()
    {
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);
        queue.Enqueue(CreateMessage(1));
        queue.Enqueue(CreateMessage(2));

        Assert.True(queue.TryLease(out _));

        Assert.Throws<InvalidOperationException>(() => queue.TryLease(out _));
    }

    [Fact]
    public void BackedUpOmfMessageQueue_TryPeekAndTryDequeue_LeaseOutstanding_Throw()
    {
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);
        queue.Enqueue(CreateMessage(1));

        Assert.True(queue.TryLease(out _));

        Assert.Throws<InvalidOperationException>(() => queue.TryPeek(out _));
        Assert.Throws<InvalidOperationException>(() => queue.TryDequeue(out _));
    }

    [Fact]
    public void BackedUpOmfMessageQueue_TryPeekAndTryDequeue_AfterAbandon_ReturnAbandonedVolatileMessageFirst()
    {
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);
        queue.Enqueue(CreateMessage(1));
        queue.Enqueue(CreateMessage(2));

        Assert.True(queue.TryLease(out var lease));
        queue.Abandon(lease);

        Assert.True(queue.TryPeek(out var message));
        AssertMarker(1, message);
        Assert.True(queue.TryDequeue(out message));
        AssertMarker(1, message);

        Assert.True(queue.TryLease(out lease));
        AssertMarker(2, lease.Message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_TryDequeue_AfterAbandon_ReturnsAbandonedPersistentMessageFirst()
    {
        using var queue = CreatePersistentBackedQueue(0);
        queue.Enqueue(CreateMessage(1));
        queue.Enqueue(CreateMessage(2));

        Assert.True(queue.TryLease(out var lease));
        queue.Abandon(lease);

        Assert.True(queue.TryDequeue(out var message));
        AssertMarker(1, message);

        Assert.True(queue.TryLease(out lease));
        AssertMarker(2, lease.Message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_VolatileSizeLimitReached_EvictsOldestUnleasedMessage()
    {
        var bodySize = 400_000;
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);
        queue.Enqueue(CreateMessage(1, bodySize));
        queue.Enqueue(CreateMessage(2, bodySize));

        Assert.True(queue.TryLease(out var lease));
        queue.Enqueue(CreateMessage(3, bodySize));
        queue.Enqueue(CreateMessage(4, bodySize));

        Assert.True(_testLogger.ContainsMessage(BackedUpOmfMessageQueue.MaxBufferSizeReachedMessage));
        Assert.True(queue.Complete(lease));

        Assert.True(queue.TryLease(out lease));
        AssertMarker(3, lease.Message);
        Assert.True(queue.Complete(lease));
        Assert.True(queue.TryLease(out lease));
        AssertMarker(4, lease.Message);
        Assert.True(queue.Complete(lease));
        Assert.False(queue.TryLease(out _));
    }

    [Fact]
    public void BackedUpOmfMessageQueue_MemoryOnly_LeasedMessageNotCountedTowardSizeLimit()
    {
        using var queue = new BackedUpOmfMessageQueue(OneMb, _messageExpirationTime, null, _testLogger);
        queue.Enqueue(CreateMessage(1, 600_000));

        Assert.True(queue.TryLease(out var lease));
        queue.Enqueue(CreateMessage(2, 300_000));
        queue.Enqueue(CreateMessage(3, 300_000));

        Assert.False(_testLogger.ContainsMessage(BackedUpOmfMessageQueue.MaxBufferSizeReachedMessage));
        Assert.True(queue.Complete(lease));

        Assert.True(queue.TryLease(out lease));
        AssertMarker(2, lease.Message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_LeasedVolatileMessageSpilledToDisk_CompleteRemovesLeasedMessage()
    {
        using var queue = CreatePersistentBackedQueue(OneMb);
        queue.Enqueue(CreateMessage(1));

        Assert.True(queue.TryLease(out var lease));
        queue.Enqueue(CreateMessage(2, (OneMb * 1024 * 1024) + 1));

        Assert.True(queue.Complete(lease));

        Assert.True(queue.TryLease(out lease));
        AssertMarker(2, lease.Message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_PersistentFileEvictedWhileLeased_CompleteDoesNotRemoveNextMessage()
    {
        var bodySize = 1024 * 1024 / 10;
        using var queue = CreatePersistentBackedQueue(0, maxQueueFiles: 1, maxFileSizeMb: 1);
        var fileQueue = _lastFileQueue;

        for (byte marker = 0; marker < 3; marker++)
        {
            queue.Enqueue(CreateMessage(marker, bodySize));
        }

        Assert.True(queue.TryLease(out var lease));
        AssertMarker(0, lease.Message);

        for (byte marker = 3; marker < 18; marker++)
        {
            queue.Enqueue(CreateMessage(marker, bodySize));
        }

        var markerAfterEviction = GetMarkerOfPersistedItem(fileQueue.Peek());
        Assert.NotEqual(0, markerAfterEviction);

        Assert.False(queue.Complete(lease));

        Assert.True(queue.TryLease(out lease));
        AssertMarker(markerAfterEviction, lease.Message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_ClearWhilePersistentLeaseOutstanding_CompleteReturnsFalse()
    {
        using var queue = CreatePersistentBackedQueue(0);
        queue.Enqueue(CreateMessage(1));

        Assert.True(queue.TryLease(out var lease));
        queue.Clear();

        Assert.False(queue.Complete(lease));
    }

    [Fact]
    public void BackedUpOmfMessageQueue_Dispose_LeasedVolatileMessage_PersistedBeforeNewerMessages()
    {
        var queue = CreatePersistentBackedQueue(OneMb);
        queue.Enqueue(CreateMessage(1));

        Assert.True(queue.TryLease(out _));
        queue.Enqueue(CreateMessage(2));
        queue.Dispose();

        using var restarted = CreatePersistentBackedQueue(OneMb);
        Assert.True(restarted.TryDequeue(out var message));
        AssertMarker(1, message);
        Assert.True(restarted.TryDequeue(out message));
        AssertMarker(2, message);
    }

    [Fact]
    public void BackedUpOmfMessageQueue_Restart_UncompletedPersistentLease_Redelivered()
    {
        var queue = CreatePersistentBackedQueue(0);
        queue.Enqueue(CreateMessage(1));
        queue.Enqueue(CreateMessage(2));

        Assert.True(queue.TryLease(out var lease));
        AssertMarker(1, lease.Message);
        queue.Dispose();

        using var restarted = CreatePersistentBackedQueue(0);
        Assert.True(restarted.TryLease(out lease));
        AssertMarker(1, lease.Message);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (var resource in _persistentResources)
            {
                resource.Dispose();
            }

            _lastFileQueue?.Dispose();

            if (Directory.Exists(_bufferDirectory))
            {
                Directory.Delete(_bufferDirectory, true);
            }
        }

        _disposed = true;
    }

    private static SerializedOmfMessage CreateMessage(byte marker, int bodySize = 16)
    {
        return new SerializedOmfMessage(MessageType.Data, Enumerable.Repeat(marker, bodySize).ToArray(), MessageAction.Default, 1);
    }

    private static void AssertMarker(byte expectedMarker, ISerializedOmfMessage message)
    {
        Assert.All(message.MessageBody, value => Assert.Equal(expectedMarker, value));
    }

    // Persisted layout: 1 byte message type, 4 bytes item count, then the body.
    private static byte GetMarkerOfPersistedItem(DataItem dataItem) => dataItem.Data[SerializedOmfMessage.MessageTypeEnumSize + SerializedOmfMessage.ValueCountSize];

    private BackedUpOmfMessageQueue CreatePersistentBackedQueue(int maxVolatileMemorySizeMb, int maxQueueFiles = 0, int maxFileSizeMb = BufferingConstants.MaxBufferFileSizeMb)
    {
        _lastFileQueue = new FileQueue(_bufferDirectory, BufferingConstants.DataBufferFilePrefix, "leaseTests", maxFileSizeMb, maxQueueFiles, _testLogger);
        _persistentResources.Add(_lastFileQueue);

        var persistentQueue = new PersistentOmfMessageQueue("leaseTests", _lastFileQueue, _testLogger);
        _persistentResources.Add(persistentQueue);

        return new BackedUpOmfMessageQueue(maxVolatileMemorySizeMb, _messageExpirationTime, persistentQueue, _testLogger);
    }
}
