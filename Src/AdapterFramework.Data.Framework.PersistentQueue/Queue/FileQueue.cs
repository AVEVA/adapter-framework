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
using System.Threading;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

/// <summary>
/// The FileQueue is an implementation of IPersistentQueue which uses the file system to persist items in the queue. Its primary goals are to be robust, fast and lightweight.
///
/// Principles of operation:
///     On Enqueue(), the FQ stores enqueued items ready to be flushed.
///     On FlushEnqueues(), the FQ attempts to write the items enqueued to the underlying disk in data files which may be no larger than the file size limit set in the constructor.
///         If the data file to be written to would become larger than the maximum file size, the FQ will attempt to create a new data file and write the data to the new data file
///             If creating the new data file causes the queue to use more data files than specified by the maximum number of queue files limit set in the constructor, the FQ will delete the oldest data file (DATA LOSS)
///         If an out of disk space exception is encountered, and the FQ is managing more than one data file, it will attempt to delete the oldest data file and continue operation (DATA LOSS)
///             If the FQ only has one queue file no queue files will be deleted. The write will be retried, upon which any exception encountered will be thrown to the client.
///         FlushEnqueues() is available in order to facilitate bulk enqueueing to improve performance.
///     The FQ writes byte arrays to disk using header record. The FQ uses the start record pattern to verify the correctness of data it is reading and to seek the next valid data item when errors occur reading from disk.
///     On Dequeue() the FQ returns the next item from disk.
///     On FlushDequeues() the FQ persists the fact that it has read items dequeued since the last dequeue. If FlushDequeues is not called and the queue is restarted, Dequeue will return items previously dequeued.
///         If FlushDequeues() is called and the queue is restarted, the queue will no longer return the items dequeued prior to FlushDequeues(). FlushDequeues() is used to signal you are finished with the data item
///         and no longer need it in the queue.
/// </summary>
public class FileQueue : IPersistentQueue
{
    private const int Kilobyte = 1024;
    private readonly IFileManager _fileManager;
    private readonly ISerializer _serializer;
    private readonly ILogger _logger;
    private readonly QueueState _queueState;
    private readonly CancellationTokenSource _internalCancelTokenSource;
    private readonly List<DataItem> _enqueuedButNotFlushed;
    private readonly string _endpointId;

    private readonly object _statePersistenceOperationLock;
    private readonly object _enqueueOperationLock;
    private readonly object _dequeueOperationLock;
    private readonly IEdgeEventProvider _eventProvider;

    // Written items with a tracking ID, by file number, in write order.
    private readonly object _trackingLock = new();
    private readonly Dictionary<int, LinkedList<(long Position, Guid Id)>> _trackedItems = new();

    // Maximum bytes of a single queue file - the queue will store more bytes than this as it uses multiple files.
    private readonly long _maxFileSizeBytes;
   
    private int _maxQueueFiles;
    private Stream _writeStream;
    private Stream _readStream;
    private bool _disposed;

    public FileQueue(string directory, string fileNamePrefix, string endpointId = "", int maxFileSizeMb = 20, int maxQueueFiles = 0, ILogger logger = null, IEdgeEventProvider eventProvider = null)
        : this(new FileManager(new FileSystemInteractor(), directory, fileNamePrefix, endpointId, logger), new Serializer(), maxFileSizeMb, maxQueueFiles, logger, eventProvider)
    {
        _endpointId = endpointId;
    }

    internal FileQueue(IFileManager fileManager, ISerializer serializer, int maxFileSizeMb, int maxQueueFiles, ILogger logger = null, IEdgeEventProvider eventProvider = null)
    {
        _fileManager = fileManager ?? throw new ArgumentException($"Null {nameof(IFileManager)}.");
        _serializer = serializer ?? throw new ArgumentException($"Null {nameof(ISerializer)}.");
        if (maxFileSizeMb <= 0) throw new ArgumentException($"{nameof(maxFileSizeMb)} must be greater than 0.");
        if (maxQueueFiles < 0) throw new ArgumentException($"{nameof(maxQueueFiles)} must be greater than or equal to 0. A value of 0 means unlimited.");

        _logger = logger;
        _eventProvider = eventProvider;

        _maxFileSizeBytes = (long)maxFileSizeMb * Kilobyte * Kilobyte;
        _maxQueueFiles = maxQueueFiles;

        _internalCancelTokenSource = new CancellationTokenSource();
        _statePersistenceOperationLock = new object();
        _enqueueOperationLock = new object();
        _dequeueOperationLock = new object();
        _enqueuedButNotFlushed = new List<DataItem>();

        using (var stateStream = _fileManager.CreateStateStream())
        {
            _queueState = _serializer.DeserializeQueueState(stateStream);
            _fileManager.RepairDirectoryAndUpdateState(_queueState, _maxQueueFiles);
            _serializer.SerializeQueueState(stateStream, _queueState);
        }

        if (!_queueState.IsValid())
        {
            throw new InvalidOperationException("Queue state is invalid.");
        }

        _writeStream = _fileManager.CreateWriterStream(_queueState.WriterFileNumber);
        _writeStream.Position = _queueState.WriterPosition;

        _readStream = _fileManager.CreateReaderStream(_queueState.ReaderFileNumber);
        _readStream.Position = _queueState.ReaderPosition;
    }

    /// <inheritdoc/>
    public event EventHandler<TrackedItemsLostEventArgs> TrackedItemsLost;

    public void UpdateMaxQueueFiles(int maxQueueFiles)
    {
        lock (_enqueueOperationLock)
        {
            lock (_dequeueOperationLock)
            {
                _maxQueueFiles = maxQueueFiles;

                // Flush current write stream before repairing
                _writeStream.Flush();

                // Persist current state so RepairDirectoryAndUpdateState works with accurate positions
                _queueState.SetWriterPosition(_writeStream.Position);
                _queueState.SetReaderPosition(_readStream.Position);

                using (var stateStream = _fileManager.CreateStateStream())
                {
                    _serializer.SerializeQueueState(stateStream, _queueState);
                }

                // Close streams before repairing so that files are not held open during deletion
                _writeStream.Dispose();
                _readStream.Dispose();

                // Delete excess files and update queue state
                _fileManager.RepairDirectoryAndUpdateState(_queueState, _maxQueueFiles);

                // Persist the repaired state
                using (var stateStream = _fileManager.CreateStateStream())
                {
                    _serializer.SerializeQueueState(stateStream, _queueState);
                }

                // Reopen streams to match the (potentially changed) state
                _writeStream = _fileManager.CreateWriterStream(_queueState.WriterFileNumber);
                _writeStream.Position = _queueState.WriterPosition;

                _readStream = _fileManager.CreateReaderStream(_queueState.ReaderFileNumber);
                _readStream.Position = _queueState.ReaderPosition;

                DropTrackedFiles(fileNumber => fileNumber < _queueState.ReaderFileNumber || fileNumber > _queueState.WriterFileNumber, TrackedItemLossReason.Evicted);
            }
        }
    }

    public DataItem Dequeue()
    {
        // Possible performance enhancement: read multiple data items at once and cache them
        // Performance is good so that complexity is avoided for now
        lock (_dequeueOperationLock)
        {
            while (!_internalCancelTokenSource.Token.IsCancellationRequested)
            {
                while (ReaderAtEndOfFile())
                {
                    if (_queueState.WriterFileNumber == _queueState.ReaderFileNumber)
                    {
                        return null;
                    }

                    MoveReaderToNewDataFile(TrackedItemLossReason.Unreadable);
                }

                try
                {
                    return ResolveDequeuedItem(_serializer.DeserializeDataItem(_readStream));
                }
                catch (DataRecordException ex)
                {
                    // Attempt to recover by finding the next valid data item
                    _logger?.LogError(ex, "Error deserializing data item. Finding the next valid data item.");

                    var initWriterPosition = _queueState.WriterPosition;
                    if (_serializer.TryDeserializeNextValidDataItem(_readStream, out var deserialized))
                    {
                        _logger?.LogInformation("Next valid data item found.");
                        return ResolveDequeuedItem(deserialized);
                    }

                    // If we didn't find anything in the current file, see if we should move on to the next file
                    if (_queueState.WriterFileNumber == _queueState.ReaderFileNumber)
                    {
                        if (initWriterPosition != _queueState.WriterPosition)
                        {
                            // The writer wrote more data to the current file. Try dequeuing again.
                            continue;
                        }

                        // The writer did not write additional data while we attempted recovery. There is no valid data item to find.
                        return null;
                    }

                    MoveReaderToNewDataFile(TrackedItemLossReason.Unreadable);
                }
            }

            return null;
        }
    }

    public DataItem Peek()
    {
        // Possible performance enhancement: read multiple data items at once and cache them
        // Performance is good so that complexity is avoided for now
        lock (_dequeueOperationLock)
        {
            while (!_internalCancelTokenSource.Token.IsCancellationRequested)
            {
                while (ReaderAtEndOfFile())
                {
                    if (_queueState.WriterFileNumber == _queueState.ReaderFileNumber)
                    {
                        return null;
                    }

                    MoveReaderToNewDataFile(TrackedItemLossReason.Unreadable);
                }

                try
                {
                    var readerPosition = _readStream.Position;
                    var returnItem = _serializer.DeserializeDataItem(_readStream);
                    _readStream.Position = readerPosition;
                    return returnItem;
                }
                catch (DataRecordException ex)
                {
                    // Attempt to recover by finding the next valid data item
                    _logger?.LogError(ex, "Error deserializing data item. Finding the next valid data item.");

                    var initWriterPosition = _queueState.WriterPosition;
                    var readerPosition = _readStream.Position;
                    if (_serializer.TryDeserializeNextValidDataItem(_readStream, out var deserialized))
                    {
                        _logger?.LogInformation("Next valid data item found.");
                        _readStream.Position = readerPosition;
                        return deserialized;
                    }

                    // If we didn't find anything in the current file, see if we should move on to the next file
                    if (_queueState.WriterFileNumber == _queueState.ReaderFileNumber)
                    {
                        if (initWriterPosition != _queueState.WriterPosition)
                        {
                            // The writer wrote more data to the current file. Try dequeuing again.
                            continue;
                        }

                        // The writer did not write additional data while we attempted recovery. There is no valid data item to find.
                        return null;
                    }

                    MoveReaderToNewDataFile(TrackedItemLossReason.Unreadable);
                }
            }

            return null;
        }
    }

    public void Enqueue(DataItem dataItem)
    {
        if (dataItem?.Data == null || dataItem.Data.Length == 0)
        {
            return;
        }

        if (dataItem.Version == DataItemVersion.Invalid)
        {
            throw new ArgumentException("Data item with invalid version received.");
        }

        if (dataItem.Data.Length > _maxFileSizeBytes)
        {
            throw new ArgumentOutOfRangeException($"The data is too large to be enqueued. Use a filequeue with a file size of at least {dataItem.Data.Length / (Kilobyte * Kilobyte)} MB to enqueue this data item.");
        }

        lock (_enqueueOperationLock)
        {
            _enqueuedButNotFlushed.Add(dataItem);
        }
    }

    public void FlushEnqueues()
    {
        lock (_enqueueOperationLock)
        {
            // Possible performance improvement: make copy and clear _enqueuedButNotFlushed while locked then use the copy to
            // flush to disk while accepting more enequeues
            foreach (var item in _enqueuedButNotFlushed)
            {
                if (TooLargeForCurrentWriterFile(item))
                {
                    MoveWriterToNewDataFile();
                }

                // Track before writing so the reader never sees a record whose entry is missing.
                TrackItem(item, _writeStream.Position);

                try
                {
                    WriteItem(item);
                }
                catch
                {
                    UntrackItem(item);
                    throw;
                }
            }

            FlushWriteStreamAndUpdateWriterState();
            _enqueuedButNotFlushed.Clear();
        }
    }

    public void FlushDequeues()
    {
        lock (_dequeueOperationLock)
        {
            PersistReaderState();
            DeleteDrainedDataFiles();
        }
    }

    // Deletes all buffers used by this instance.
    public void DeleteBuffers()
    {
        lock (_enqueueOperationLock)
        {
            _writeStream.Dispose();
            lock (_dequeueOperationLock)
            {
                _readStream.Dispose();
                _fileManager.DeleteDirectory();
                DropTrackedFiles(_ => true, TrackedItemLossReason.Cleared);
            }
        }
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
            lock (_enqueueOperationLock)
            {
                lock (_dequeueOperationLock)
                {
                    _internalCancelTokenSource.Cancel();
                    _writeStream.Dispose();
                    _readStream.Dispose();
                    _internalCancelTokenSource.Dispose();
                }
            }
        }

        _disposed = true;
    }

    private void FlushWriteStreamAndUpdateWriterState()
    {
        _writeStream.Flush();
        PersistWriterState();
    }

    private void MoveWriterToNewDataFile()
    {
        lock (_enqueueOperationLock)
        {
            _writeStream.Flush();
            _writeStream.Dispose();

            _writeStream = _fileManager.CreateWriterStream(_queueState.WriterFileNumber + 1);
            _queueState.IncrementWriterFileNumber();

            ReportBufferUsage(out var filesInUse, out var percentUsed);

            _logger?.LogDebug("Created a new buffer file for endpoint Id {endpointId}. Files in use: {FilesInUse}, Percent of configured storage used: {PercentUsed}%", _endpointId, filesInUse, percentUsed);

            if (_maxQueueFiles > 0 && filesInUse >= _maxQueueFiles)
            {
                MoveReaderToNewDataFile(TrackedItemLossReason.Evicted);
                _fileManager.DeleteOldestFilePendingDeletion();
            }
        }
    }

    private void ReportBufferUsage(out int filesInUse, out double percentUsed)
    {
        filesInUse = _queueState.WriterFileNumber - _queueState.ReaderFileNumber;
        percentUsed = _maxQueueFiles > 0
            ? (double)filesInUse / _maxQueueFiles * 100.0
            : 0.0;

        if (_eventProvider?.EdgeEventChannel != null)
        {
            var bufferEvent = new BufferFileCreatedDeletedInfo(_endpointId, filesInUse, percentUsed);
            _eventProvider.EdgeEventChannel.Writer.TryWrite(bufferEvent);
        }
    }

    /// <summary>
    /// Moves the reader to the next data file and reports tracked items it left unread in the previous file.
    /// </summary>
    /// <param name="lossReason">The reason reported for tracked items left in the previous file.</param>
    private void MoveReaderToNewDataFile(TrackedItemLossReason lossReason)
    {
        lock (_dequeueOperationLock)
        {
            var previousFileNumber = _queueState.ReaderFileNumber;
            _queueState.IncrementReaderFileNumber();
            _queueState.SetReaderPosition(0);
            _readStream.Dispose();
            _readStream = _fileManager.CreateReaderStream(_queueState.ReaderFileNumber);
            _fileManager.AddPendingDeletion(_queueState.ReaderFileNumber - 1);
            ReportBufferUsage(out _, out _);
            DropTrackedFiles(fileNumber => fileNumber == previousFileNumber, lossReason);
        }
    }

    private void PersistWriterState()
    {
        _queueState.SetWriterPosition(_writeStream.Position);

        lock (_statePersistenceOperationLock)
        {
            using var stream = _fileManager.CreateStateStream();
            _serializer.SerializeWriterState(stream, _queueState);
        }
    }

    private void PersistReaderState()
    {
        _queueState.SetReaderPosition(_readStream.Position);

        lock (_statePersistenceOperationLock)
        {
            using var stream = _fileManager.CreateStateStream();
            _serializer.SerializeReaderState(stream, _queueState);
        }
    }

    private void TryDeleteOldestQueueFile()
    {
        lock (_dequeueOperationLock)
        {
            if (_fileManager.AnyPendingFileDeletions())
            {
                _fileManager.DeleteOldestFilePendingDeletion();
            }
            else
            {
                if (_queueState.WriterFileNumber == _queueState.ReaderFileNumber)
                {
                    // Only one queue file, can't delete it.
                    _logger?.LogError("No queue files available to delete.");
                    return;
                }

                _logger?.LogInformation("Moving reader to new data file in order to delete oldest data file.");
                MoveReaderToNewDataFile(TrackedItemLossReason.Evicted);
                _fileManager.DeleteFilesPendingDeletion();
            }

            ReportBufferUsage(out _, out _);
        }
    }

    private bool TooLargeForCurrentWriterFile(DataItem item) => item.Data.Length + _writeStream.Position > _maxFileSizeBytes;

    private void DeleteDrainedDataFiles() => _fileManager.DeleteFilesPendingDeletion();

    private bool ReaderAtEndOfFile() => _readStream.Position == _readStream.Length;

    /// <summary>
    /// Writes an item to the current writer file, deleting the oldest data file once if the disk is full.
    /// </summary>
    /// <param name="item">The item to write.</param>
    private void WriteItem(DataItem item)
    {
        try
        {
            _serializer.SerializeDataItem(_writeStream, item);
        }
        catch (IOException ex) when (ExceptionChecker.IsOutOfDiskSpaceException(ex))
        {
            _logger?.LogError("Out of disk. Attempting to make room by deleting the oldest data file.");
            TryDeleteOldestQueueFile();

            try
            {
                _serializer.SerializeDataItem(_writeStream, item);
                _logger?.LogInformation("Successfully persisted item by removing oldest data file.");
            }
            catch (IOException) when (ExceptionChecker.IsOutOfDiskSpaceException(ex))
            {
                FlushWriteStreamAndUpdateWriterState();
                _enqueuedButNotFlushed.RemoveRange(0, _enqueuedButNotFlushed.IndexOf(item));
                throw;
            }
        }
    }

    /// <summary>
    /// Records the file and position of an item that carries a tracking ID.
    /// </summary>
    /// <param name="item">The item about to be written.</param>
    /// <param name="position">The position of the item's record in the current writer file.</param>
    private void TrackItem(DataItem item, long position)
    {
        if (item.TrackingId is not { } trackingId)
        {
            return;
        }

        lock (_trackingLock)
        {
            if (!_trackedItems.TryGetValue(_queueState.WriterFileNumber, out var items))
            {
                items = new LinkedList<(long Position, Guid Id)>();
                _trackedItems[_queueState.WriterFileNumber] = items;
            }

            items.AddLast((position, trackingId));
        }
    }

    /// <summary>
    /// Removes the entry <see cref="TrackItem"/> recorded for an item whose write failed.
    /// </summary>
    /// <param name="item">The item that was not written.</param>
    private void UntrackItem(DataItem item)
    {
        if (item.TrackingId is not { } trackingId)
        {
            return;
        }

        lock (_trackingLock)
        {
            if (_trackedItems.TryGetValue(_queueState.WriterFileNumber, out var items) && items.Last?.Value.Id == trackingId)
            {
                items.RemoveLast();
                if (items.Count == 0)
                {
                    _trackedItems.Remove(_queueState.WriterFileNumber);
                }
            }
        }
    }

    /// <summary>
    /// Removes the tracked entry of a dequeued item and reports tracked items the reader skipped before it.
    /// </summary>
    /// <param name="item">The dequeued item, or <c>null</c> when nothing was read.</param>
    /// <returns>The <paramref name="item"/> unchanged.</returns>
    private DataItem ResolveDequeuedItem(DataItem item)
    {
        if (item == null)
        {
            return null;
        }

        List<Guid> skipped = null;
        lock (_trackingLock)
        {
            if (!_trackedItems.TryGetValue(_queueState.ReaderFileNumber, out var items))
            {
                return item;
            }

            // The reader stops at the end of the returned record, so its start follows from the record size.
            var start = _readStream.Position - Serializer.DataItemSerializedOverhead - item.Data.Length;
            while (items.First?.Value.Position < start)
            {
                (skipped ??= new List<Guid>()).Add(items.First.Value.Id);
                items.RemoveFirst();
            }

            if (items.First?.Value.Position == start)
            {
                items.RemoveFirst();
            }

            if (items.Count == 0)
            {
                _trackedItems.Remove(_queueState.ReaderFileNumber);
            }
        }

        RaiseTrackedItemsLost(skipped, TrackedItemLossReason.Unreadable);
        return item;
    }

    /// <summary>
    /// Removes the tracked entries of the matching files and reports them as lost.
    /// </summary>
    /// <param name="matches">Selects the file numbers to drop.</param>
    /// <param name="reason">The reason reported for the dropped entries.</param>
    private void DropTrackedFiles(Func<int, bool> matches, TrackedItemLossReason reason)
    {
        List<Guid> lost = null;
        lock (_trackingLock)
        {
            foreach (var fileNumber in new List<int>(_trackedItems.Keys))
            {
                if (!matches(fileNumber))
                {
                    continue;
                }

                lost ??= new List<Guid>();
                foreach (var (_, id) in _trackedItems[fileNumber])
                {
                    lost.Add(id);
                }

                _trackedItems.Remove(fileNumber);
            }
        }

        RaiseTrackedItemsLost(lost, reason);
    }

    /// <summary>
    /// Raises <see cref="TrackedItemsLost"/> when any IDs were lost.
    /// </summary>
    /// <param name="ids">The lost tracking IDs, or <c>null</c> when none were lost.</param>
    /// <param name="reason">Why the items were lost.</param>
    private void RaiseTrackedItemsLost(List<Guid> ids, TrackedItemLossReason reason)
    {
        if (ids is { Count: > 0 })
        {
            TrackedItemsLost?.Invoke(this, new TrackedItemsLostEventArgs(ids, reason));
        }
    }
}
