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
using System.IO;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Buffering;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;

namespace AdapterFramework.Data.Framework.Buffering;

public abstract class PersistentOmfMessageQueueBase<TMessage> : IPersistentMessageQueue<TMessage>
    where TMessage : ISerializedOmfMessage
{
    #region Private Constants

    #endregion

    #region Private Fields

    private readonly IPersistentQueue _persistentQueue;
    private readonly ILogger _logger;
    private readonly string _targetIdentifier;
    private bool _disposed;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Creates instance of <see cref="PersistentOmfMessageQueue"/>.
    /// </summary>
    /// <param name="targetIdentifier">Identifier of the target the <see cref="PersistentOmfMessageQueue"/> is created for.</param>
    /// <param name="persistentQueue"><see cref="IPersistentQueue"/> instance.</param>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    public PersistentOmfMessageQueueBase(string targetIdentifier, IPersistentQueue persistentQueue, ILogger logger)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(targetIdentifier, nameof(targetIdentifier));
        ThrowHelper.ThrowIfArgumentNull(persistentQueue, nameof(persistentQueue));

        _targetIdentifier = targetIdentifier;
        _persistentQueue = persistentQueue;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public void Enqueue(TMessage message)
    {
        ThrowHelper.ThrowIfArgumentNull(message, nameof(message));

        var dataItem = CreateDataItem(message);

        _persistentQueue.Enqueue(dataItem);

        try
        {
            _persistentQueue.FlushEnqueues(); // If performance an issue, use a timer to periodically flush
        }
        catch (IOException ex)
        {
            // This should only happen for an out of disk space exception when using FileQueue as the IPersistentQueue
            _logger?.LogError(ex, "Error flushing {MessageType} to disk in buffer for {TargetIdentifier}.", message.MessageType, _targetIdentifier);
        }
    }

    /// <inheritdoc/>
    public bool TryDequeue(out TMessage message)
    {
        message = default;

        var dataItem = _persistentQueue.Dequeue();
        if (dataItem != null)
        {
            message = CreateSerializedOmfMessage(dataItem);
            _persistentQueue.FlushDequeues();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryPeek(out TMessage message)
    {
        message = default;

        var dataItem = _persistentQueue.Peek();
        if (dataItem != null)
        {
            message = CreateSerializedOmfMessage(dataItem);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _persistentQueue.DeleteBuffers();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Methods

    protected abstract DataItem CreateDataItem(TMessage message);

    protected abstract TMessage CreateSerializedOmfMessage(DataItem dataItem);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _persistentQueue.Dispose();
            }

            _disposed = true;
        }
    }

    #endregion
}
