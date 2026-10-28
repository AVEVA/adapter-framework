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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Buffering;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Buffering.Messages;
using AdapterFramework.Data.Framework.Extensions;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.Buffering.Tests")]

namespace AdapterFramework.Data.Framework.Buffering;

public abstract class BackedUpOmfMessageQueueBase<TMessage> : IPersistentMessageQueue<TMessage>, ILeasableMessageQueue<TMessage>
    where TMessage : ISerializedOmfMessage
{
    #region Internal Constants

    internal const string MaxBufferSizeReachedMessage = "Maximum volatile buffer size has been reached. The oldest data will be discarded.";

    #endregion

    #region Private Constants

    private const int ConsolidateQueuesDelay = 2500;
    private const int Kilobyte = 1024;
    private const string LeaseOutstandingMessage = "A message lease is already outstanding.";
    private const string MessageLeasedMessage = "Messages cannot be peeked or dequeued while a message lease is outstanding.";

    #endregion

    #region Private Fields

    private readonly Queue<TimestampedOmfMessage<TMessage>> _volatileQueue;
    private readonly IPersistentMessageQueue<TMessage> _persistentQueue;
    private readonly object _lock = new();
    private readonly Task _consolidateQueuesTask;
    private readonly TimeSpan _messageExpirationTime;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ILogger _logger;
    private long _currentVolatileQueueSize;
    private long _maxVolatileQueueSize;
    private LeasedItem _leasedItem;
    private bool _leaseOutstanding;
    private long _leaseToken;
    private bool _disposed;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Creates a new instance of the <see cref="BackedUpOmfMessageQueue"/> class.
    /// </summary>
    /// <param name="maxVolatileMemorySize">Maximum size of internal volatile memory in MB.</param>
    /// <param name="messageExpirationTime">Timespan defining maximum time message can spend in volatile queue.</param>
    /// <param name="persistentQueue"><see cref="IPersistentMessageQueue{TMessage}"/> instance. Messages are pushed to it from volatile queue in event or
    /// expiring or when the volatile queue is over size.</param>
    /// <param name="logger">Logger instance.</param>
    public BackedUpOmfMessageQueueBase(int maxVolatileMemorySize, TimeSpan messageExpirationTime, IPersistentMessageQueue<TMessage> persistentQueue, ILogger logger)
    {
        _messageExpirationTime = messageExpirationTime;
        _persistentQueue = persistentQueue;
        _maxVolatileQueueSize = MegaBytesToBytes(maxVolatileMemorySize);
        _logger = logger;

        _cancellationTokenSource = new CancellationTokenSource();
        _volatileQueue = new Queue<TimestampedOmfMessage<TMessage>>();

        _consolidateQueuesTask = Task.Run(async () =>
        {
            await ConsolidateQueuesTimedTaskAsync();
        });
    }

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public void Enqueue(TMessage message)
    {
        ThrowHelper.ThrowIfArgumentNull(message, nameof(message));

        lock (_lock)
        {
            _volatileQueue.Enqueue(new TimestampedOmfMessage<TMessage>(message));
            _currentVolatileQueueSize += message.GetMessageSizeInBytes();

            if (_currentVolatileQueueSize > _maxVolatileQueueSize)
            {
                ConsolidateQueues();
            }
        }
    }

    /// <inheritdoc/>
    public bool TryDequeue(out TMessage message)
    {
        message = default;

        lock (_lock)
        {
            ThrowIfLeaseOutstanding();

            if (_leasedItem?.VolatileMessage != null)
            {
                // An abandoned volatile lease is the oldest message and lives only in _leasedItem.
                message = _leasedItem.Message;
                _currentVolatileQueueSize -= message.GetMessageSizeInBytes();
                _leasedItem = null;
                return true;
            }

            // An abandoned persistent lease is still the persistent head unless retention discarded it.
            _leasedItem = null;

            if (_persistentQueue == null || !_persistentQueue.TryDequeue(out message))
            {
                if (_volatileQueue.TryDequeue(out var timestampedMessage))
                {
                    message = timestampedMessage.SerializedOmfMessage;
                    _currentVolatileQueueSize -= message.GetMessageSizeInBytes();
                }
            }

            return message != null;
        }
    }

    /// <inheritdoc/>
    public bool TryPeek(out TMessage message)
    {
        message = default;

        lock (_lock)
        {
            ThrowIfLeaseOutstanding();

            if (_leasedItem?.VolatileMessage != null)
            {
                message = _leasedItem.Message;
                return true;
            }

            if (_persistentQueue == null || !_persistentQueue.TryPeek(out message))
            {
                if (_volatileQueue.TryPeek(out var timestampedMessage))
                {
                    message = timestampedMessage.SerializedOmfMessage;
                }
            }

            return message != null;
        }
    }

    /// <inheritdoc/>
    public bool TryLease(out MessageLease<TMessage> lease)
    {
        lease = null;

        lock (_lock)
        {
            if (_leaseOutstanding)
            {
                throw new InvalidOperationException(LeaseOutstandingMessage);
            }

            if (_leasedItem is { VolatileMessage: null } && _leasedItem.ReaderEpoch != _persistentQueue.ReaderEpoch)
            {
                // Retention discarded the previously leased message from persistent storage.
                _leasedItem = null;
            }

            if (_leasedItem == null && !TrySelectHead(out _leasedItem))
            {
                return false;
            }

            _leaseOutstanding = true;
            lease = new MessageLease<TMessage>(_leasedItem.Message, ++_leaseToken);
            return true;
        }
    }

    /// <inheritdoc/>
    public bool Complete(MessageLease<TMessage> lease)
    {
        lock (_lock)
        {
            if (!IsActiveLease(lease))
            {
                return false;
            }

            var leasedItem = _leasedItem;
            _leasedItem = null;
            _leaseOutstanding = false;

            if (leasedItem.VolatileMessage != null)
            {
                _currentVolatileQueueSize -= leasedItem.Message.GetMessageSizeInBytes();
                return true;
            }

            return _persistentQueue.TryDequeueIfEpoch(leasedItem.ReaderEpoch);
        }
    }

    /// <inheritdoc/>
    public void Abandon(MessageLease<TMessage> lease)
    {
        lock (_lock)
        {
            if (IsActiveLease(lease))
            {
                _leaseOutstanding = false;
            }
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        lock (_lock)
        {
            _persistentQueue?.Clear();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Methods

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cancellationTokenSource?.Cancel();
            _consolidateQueuesTask?.GetAwaiter().GetResult();

            lock (_lock)
            {
                _maxVolatileQueueSize = 0;
            }

            ConsolidateQueues();

            lock (_lock)
            {
                _persistentQueue?.Dispose();
            }

            _consolidateQueuesTask?.Dispose();
            _cancellationTokenSource?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private static long MegaBytesToBytes(int megabytes) => (long)megabytes * Kilobyte * Kilobyte;

    private async Task ConsolidateQueuesTimedTaskAsync()
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                ConsolidateQueues();

                await Task.Delay(ConsolidateQueuesDelay, _cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ConsolidateQueues()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            while (IsSpillRequired(now))
            {
                if (_persistentQueue != null && _leasedItem?.VolatileMessage is { } leasedMessage)
                {
                    // Spill the leased message before anything else so it stays first in persistent storage.
                    SpillLeasedMessage(leasedMessage);
                }
                else if (_volatileQueue.TryDequeue(out var timestampedMessage))
                {
                    _currentVolatileQueueSize -= timestampedMessage.SerializedOmfMessage.GetMessageSizeInBytes();
                    _persistentQueue?.Enqueue(timestampedMessage.SerializedOmfMessage);
                    if (_persistentQueue == null)
                    {
                        _logger.LogDebug(MaxBufferSizeReachedMessage);
                    }
                }
                else
                {
                    return;
                }
            }
        }
    }

    private bool IsSpillRequired(DateTime now)
    {
        // Without persistent storage a leased message cannot be dropped, so it must not push newer messages out.
        var volatileQueueSize = _persistentQueue == null && _leasedItem?.VolatileMessage != null
            ? _currentVolatileQueueSize - _leasedItem.Message.GetMessageSizeInBytes()
            : _currentVolatileQueueSize;

        if (volatileQueueSize > _maxVolatileQueueSize)
        {
            return true;
        }

        return _persistentQueue != null
               && ((_volatileQueue.TryPeek(out var head) && HasMessageExpired(head.Timestamp, now))
                   || (_leasedItem?.VolatileMessage is { } leasedMessage && HasMessageExpired(leasedMessage.Timestamp, now)));
    }

    private void SpillLeasedMessage(TimestampedOmfMessage<TMessage> leasedMessage)
    {
        _persistentQueue.Enqueue(leasedMessage.SerializedOmfMessage);
        _currentVolatileQueueSize -= leasedMessage.SerializedOmfMessage.GetMessageSizeInBytes();

        // Volatile messages are only leased while persistent storage is empty, so this is now its head.
        _leasedItem = new LeasedItem(leasedMessage.SerializedOmfMessage, null, _persistentQueue.ReaderEpoch);
    }

    private bool TrySelectHead(out LeasedItem leasedItem)
    {
        if (_persistentQueue != null)
        {
            // Read before peeking so an eviction racing the peek invalidates this lease rather than a later one.
            var readerEpoch = _persistentQueue.ReaderEpoch;
            if (_persistentQueue.TryPeek(out var persistedMessage))
            {
                leasedItem = new LeasedItem(persistedMessage, null, readerEpoch);
                return true;
            }
        }

        if (_volatileQueue.TryDequeue(out var timestampedMessage))
        {
            leasedItem = new LeasedItem(timestampedMessage.SerializedOmfMessage, timestampedMessage, 0);
            return true;
        }

        leasedItem = null;
        return false;
    }

    private bool IsActiveLease(MessageLease<TMessage> lease) => lease != null && _leaseOutstanding && lease.Token == _leaseToken;

    private void ThrowIfLeaseOutstanding()
    {
        if (_leaseOutstanding)
        {
            throw new InvalidOperationException(MessageLeasedMessage);
        }
    }

    private bool HasMessageExpired(DateTime messageTimestamp, DateTime now)
    {
        return now - messageTimestamp > _messageExpirationTime;
    }

    #endregion

    #region Nested Types

    private sealed class LeasedItem
    {
        public LeasedItem(TMessage message, TimestampedOmfMessage<TMessage> volatileMessage, long readerEpoch)
        {
            Message = message;
            VolatileMessage = volatileMessage;
            ReaderEpoch = readerEpoch;
        }

        public TMessage Message { get; }

        // Null when the leased message is the persistent queue head.
        public TimestampedOmfMessage<TMessage> VolatileMessage { get; }

        public long ReaderEpoch { get; }
    }

    #endregion
}
