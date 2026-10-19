// Copyright 2018-2026 AVEVA Group Limited
// Licensed under the Apache License, Version 2.0 (the "License");
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

/// <summary>Verifies generic loss notifications needed by unscoped acceptance tracking.</summary>
public sealed class AcceptanceQueueLoss_Tests : IDisposable
{
    private const int FileBytes = 1024 * 1024;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AcceptanceQueueLoss", Path.GetRandomFileName());
    private FileQueue _queue;

    /// <summary>Evicting a file with only untracked records still freezes and releases timestamp tracking.</summary>
    [Fact]
    public void FileQueue_UnscopedEviction_FreezesTracker()
    {
        var tracker = CreateQueueAndTracker(maxFiles: 2);
        tracker.Register(100);
        for (var index = 0; index < 3; index++)
        {
            _queue.Enqueue(Untracked(FileBytes));
        }

        _queue.FlushEnqueues();
        tracker.Accept(100);
        Assert.Equal(99, tracker.GetWatermark().Ticks);
    }

    /// <summary>Resizing the queue cannot silently discard unscoped records and let their timestamps advance.</summary>
    [Fact]
    public void FileQueue_UnscopedResizeLoss_FreezesTracker()
    {
        var tracker = CreateQueueAndTracker();
        tracker.Register(100);
        for (var index = 0; index < 3; index++)
        {
            _queue.Enqueue(Untracked(FileBytes));
        }

        _queue.FlushEnqueues();
        _queue.UpdateMaxQueueFiles(1);
        tracker.Accept(100);
        Assert.Equal(99, tracker.GetWatermark().Ticks);
    }

    /// <summary>Skipping a corrupt record without a serialized ID freezes coverage before recovery returns a later body.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FileQueue_UnscopedCorruption_FreezesTracker(bool peek)
    {
        var tracker = CreateQueueAndTracker();
        tracker.Register(100);
        _queue.Enqueue(Untracked(100));
        _queue.Enqueue(Untracked(100));
        _queue.Enqueue(Untracked(FileBytes));
        _queue.FlushEnqueues();
        var firstFile = Directory.GetFiles(_directory, "*_data0", SearchOption.AllDirectories);
        using (var stream = File.Open(Assert.Single(firstFile), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            stream.Position = 12;
            stream.WriteByte(0xff);
        }

        Assert.NotNull(peek ? _queue.Peek() : _queue.Dequeue());
        tracker.Accept(100);
        Assert.Equal(99, tracker.GetWatermark().Ticks);
    }

    /// <summary>Normal queue file transitions do not report drained unscoped records as lost.</summary>
    [Fact]
    public void FileQueue_DrainedFiles_DoesNotFreezeTracker()
    {
        var tracker = CreateQueueAndTracker();
        tracker.Register(100);
        _queue.Enqueue(Untracked(FileBytes));
        _queue.Enqueue(Untracked(100));
        _queue.FlushEnqueues();
        Assert.NotNull(_queue.Dequeue());
        Assert.NotNull(_queue.Dequeue());
        Assert.Null(_queue.Dequeue());
        tracker.Accept(100);
        Assert.Equal(100, tracker.GetWatermark().Ticks);
    }

    /// <summary>Disposes file streams and removes the test's isolated disk buffers.</summary>
    public void Dispose()
    {
        _queue?.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    /// <summary>Creates a real disk queue whose generic loss notifications freeze a tracker without queue reentry.</summary>
    private AcceptanceLowWaterTracker CreateQueueAndTracker(int maxFiles = 0)
    {
        var tracker = new AcceptanceLowWaterTracker();
        _queue = new FileQueue(_directory, "acceptance", maxFileSizeMb: 1, maxQueueFiles: maxFiles);
        _queue.ItemsLost += (_, _) => tracker.Freeze();
        return tracker;
    }

    /// <summary>Creates an opaque legacy record without any serialized tracking ID.</summary>
    private static DataItem Untracked(int bytes) => new(DataItemVersion.V3, new byte[bytes]);
}