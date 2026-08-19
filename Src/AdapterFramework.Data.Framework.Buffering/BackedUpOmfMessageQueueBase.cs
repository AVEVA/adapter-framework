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

public abstract class BackedUpOmfMessageQueueBase<TMessage> : IPersistentMessageQueue<TMessage>
    where TMessage : ISerializedOmfMessage
{
    #region Internal Constants

    internal const string MaxBufferSizeReachedMessage = "Maximum volatile buffer size has been reached. The oldest data will be discarded.";

    #endregion

    #region Private Constants

    private const int ConsolidateQueuesDelay = 2500;
    private const int Kilobyte = 1024;

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
            while (_currentVolatileQueueSize > _maxVolatileQueueSize
                   || (_persistentQueue != null && _volatileQueue.TryPeek(out var timestampedMessage) && HasMessageExpired(timestampedMessage.Timestamp)))
            {
                if (_volatileQueue.TryDequeue(out timestampedMessage))
                {
                    _currentVolatileQueueSize -= timestampedMessage.SerializedOmfMessage.GetMessageSizeInBytes();
                    _persistentQueue?.Enqueue(timestampedMessage.SerializedOmfMessage);
                    if (_persistentQueue == null)
                    {
                        _logger.LogDebug(MaxBufferSizeReachedMessage);
                    }
                }
            }
        }
    }

    private bool HasMessageExpired(DateTime messageTimestamp)
    {
        return DateTime.UtcNow - messageTimestamp > _messageExpirationTime;
    }

    #endregion
}
