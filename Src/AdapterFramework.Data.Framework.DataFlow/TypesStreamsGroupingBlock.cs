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
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Provides a dataFlow block that will group every <see cref="DataType"/> or <see cref="DataStream"/> item received to one bulked <see cref="OmfMessage{T}"/>.
/// </summary>
[DebuggerDisplay("Buffered Messages = {MessageHandler.InputCount}")]
public sealed class TypesStreamsGroupingBlock : BaseBlock<Message>
{
    #region Private Constants

    private const int MinimumBatchCount = 1;
    private const int MinimumFlushTime = 15;
    private const int DisposalDelay = 15 * 1000;
    private const string OutOfRangeExceptionTemplate = "Parameter must be at least {0}.";

    #endregion

    #region Private Fields

    private readonly Action<Message> _flush;
    private readonly Timer _flushTimer;
    private readonly int _maxFlushTime;
    private readonly int _maxTypesBatchCount;
    private readonly List<DataStream> _currentStreamsBatch;
    private readonly List<DataType> _currentTypesBatch;
    private int _maxStreamsBatchCount;
    private MessageAction _messageAction;
    private int _currentStreamCount;
    private int _currentTypeCount;
    private int _lastFlush;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Grouping block that allows batching of <see cref="DataType"/> and <see cref="DataStream"/> values.
    /// </summary>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="capacity"> The max number of messages that may be buffered by the block. Use <see cref="DataflowBlockOptions"/> unbounded capacity.</param>
    /// <param name="flush">The action to be performed when <paramref name="maxTypesBatchCount"/>, <paramref name="maxStreamsBatchCount"/> or <paramref name="maxFlushTime"/> has been reached.</param>
    /// <param name="maxStreamsBatchCount">Maximum number of <see cref="DataStream"/> that can be batched.</param>
    /// <param name="maxTypesBatchCount">Maximum number of <see cref="DataType"/> that can be batched.</param>
    /// <param name="maxFlushTime">The max time in milliseconds before batched messages get flushed.</param>
    /// <param name="token">Cancellation token.</param>
    public TypesStreamsGroupingBlock(ILogger logger,
        int capacity,
        Action<Message> flush,
        int maxStreamsBatchCount,
        int maxTypesBatchCount,
        int maxFlushTime,
        CancellationToken token)
        : base(logger, capacity, false, false, token)
    {
        ThrowHelper.ThrowIfArgumentNull(flush, nameof(flush));

        if (maxStreamsBatchCount < MinimumBatchCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStreamsBatchCount), string.Format(CultureInfo.InvariantCulture, OutOfRangeExceptionTemplate, MinimumBatchCount));
        }

        if (maxTypesBatchCount < MinimumBatchCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTypesBatchCount), string.Format(CultureInfo.InvariantCulture, OutOfRangeExceptionTemplate, MinimumBatchCount));
        }

        if (maxFlushTime < MinimumFlushTime)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFlushTime), string.Format(CultureInfo.InvariantCulture, OutOfRangeExceptionTemplate, MinimumFlushTime));
        }

        _flush = flush;
        _maxStreamsBatchCount = maxStreamsBatchCount;
        _maxTypesBatchCount = maxTypesBatchCount;
        _maxFlushTime = maxFlushTime;

        _currentStreamsBatch = StreamsBatchFactory();
        _currentTypesBatch = TypesBatchFactory();
        _lastFlush = Environment.TickCount;
        _flushTimer = new Timer(HandleTimer, null, maxFlushTime, maxFlushTime);
    }

    #endregion

    #region Protected Methods

    /// <inheritdoc/>
    protected override void Handle(Message message)
    {
        switch (message)
        {
            case CommandMessage command:
                ProcessCommand(command);
                break;
            case OmfMessage<DataStream> dataStreams:
                SetMessageActionAndFlushOnChange(dataStreams);
                ProcessOmfMessage(dataStreams);
                break;
            case OmfMessage<DataType> dataTypes:
                SetMessageActionAndFlushOnChange(dataTypes);
                ProcessOmfMessage(dataTypes);
                break;
            case StateMessage state:
                ProcessState(state);
                break;
        }
    }

    /// <inheritdoc/>
    protected override Task HandleAsync(Message message)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (Disposed || !disposing)
        {
            return;
        }

        _flushTimer.Change(Timeout.Infinite, Timeout.Infinite);

        MessageHandler.Complete();
        SpinWait.SpinUntil(() => MessageHandler.Completion.IsCompleted, DisposalDelay);

        Flush();

        _flushTimer.Dispose();

        base.Dispose(true);
    }

    #endregion

    #region Private Methods

    private static List<DataStream> StreamsBatchFactory() => [];

    private static List<DataType> TypesBatchFactory() => [];

    private void ProcessOmfMessage<T>(OmfMessage<T> omfMessage)
    {
        if (omfMessage.Count == 1)
        {
            AddValueToCurrentBatch(omfMessage.Values[0]);
        }
        else if (omfMessage.Count > 1)
        {
            AddValuesToCurrentBatch(omfMessage.Values);
        }
    }

    private void AddValueToCurrentBatch<T>(T value)
    {
        if (value is DataType dataType)
        {
            _currentTypesBatch.Add(dataType);
            _currentTypeCount++;

            if (_currentTypeCount >= _maxTypesBatchCount)
            {
                FlushTypes();
            }
        }
        else if (value is DataStream dataStream)
        {
            _currentStreamsBatch.Add(dataStream);
            _currentStreamCount++;

            if (_currentStreamCount >= _maxStreamsBatchCount)
            {
                FlushStreams();
            }
        }
    }

    private void AddValuesToCurrentBatch<T>(IReadOnlyCollection<T> values)
    {
        if (values is IEnumerable<DataType> dataTypes)
        {
            if (_currentTypeCount + values.Count <= _maxTypesBatchCount)
            {
                _currentTypesBatch.AddRange(dataTypes);
                _currentTypeCount += values.Count;

                if (_currentTypeCount >= _maxTypesBatchCount)
                {
                    FlushTypes();
                }
            }
            else
            {
                foreach (var dataType in dataTypes)
                {
                    AddValueToCurrentBatch(dataType);
                }
            }
        }

        if (values is IEnumerable<DataStream> dataStreams)
        {
            if (_currentStreamCount + values.Count <= _maxStreamsBatchCount)
            {
                _currentStreamsBatch.AddRange(dataStreams);
                _currentStreamCount += values.Count;

                if (_currentStreamCount >= _maxStreamsBatchCount)
                {
                    FlushStreams();
                }
            }
            else
            {
                foreach (var dataStream in dataStreams)
                {
                    AddValueToCurrentBatch(dataStream);
                }
            }
        }
    }

    private void Flush()
    {
        FlushTypes();
        FlushStreams();
    }

    private void FlushTypes()
    {
        if (_currentTypeCount == 0)
        {
            return;
        }

        _flush(new OmfMessage<DataType>(_currentTypeCount, [.. _currentTypesBatch], _messageAction));

        _currentTypeCount = 0;
        _currentTypesBatch.Clear();
        _lastFlush = Environment.TickCount;
    }

    private void FlushStreams()
    {
        if (_currentStreamCount == 0)
        {
            return;
        }

        _flush(new OmfMessage<DataStream>(_currentStreamCount, [.. _currentStreamsBatch], _messageAction));

        _currentStreamCount = 0;
        _currentStreamsBatch.Clear();

        _lastFlush = Environment.TickCount;
    }

    private void ProcessCommand(CommandMessage command)
    {
        if (!command.ForceFlush && !FlushDue())
        {
            return;
        }

        Flush();
    }

    private void ProcessState(StateMessage state)
    {
        Logger?.LogDebug("Streams batch size change. Previous = {PreviousStreamsBatchCount}. New size = {NewStreamsBatchCount}.", _maxStreamsBatchCount, state.BatchCount);

        _maxStreamsBatchCount = state.BatchCount;

        if (_maxStreamsBatchCount < _currentStreamCount)
        {
            Flush();
        }
    }

    private void HandleTimer(object state)
    {
        if (FlushDue())
        {
            MessageHandler.Post(new CommandMessage(false));
        }
    }

    private bool FlushDue()
    {
        return Environment.TickCount - _lastFlush >= _maxFlushTime && (_currentStreamCount > 0 || _currentTypeCount > 0);
    }

    private void SetMessageActionAndFlushOnChange<T>(OmfMessage<T> message)
    {
        if (typeof(T) == typeof(DataType))
        {
            if (_currentStreamCount != 0)
            {
                FlushStreams();
                _messageAction = message.MessageAction;
            }
            else if (_messageAction != message.MessageAction)
            {
                FlushTypes();
                _messageAction = message.MessageAction;
            }
        }
        else if (typeof(T) == typeof(DataStream))
        {
            if (_currentTypeCount != 0)
            {
                FlushTypes();
                _messageAction = message.MessageAction;
            }
            else if (_messageAction != message.MessageAction)
            {
                FlushStreams();
                _messageAction = message.MessageAction;
            }
        }
    }

    #endregion
}
