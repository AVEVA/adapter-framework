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
using System.Buffers;
using System.Collections.Generic;
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
public sealed class SchemaGroupingBlock : BaseBlock<Message>
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
    private List<Link> _relationships;
    private MessageAction _messageAction;
    private int _currentStreamCount;
    private int _currentTypeCount;
    private long _lastFlush;

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
    public SchemaGroupingBlock(ILogger logger,
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
        _lastFlush = Environment.TickCount64;
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
                SetMessageActionAndFlushOnChange(dataStreams.MessageAction);
                ProcessOmfMessage(dataStreams);
                break;
            case RelationshipMessage relationship:
                SetMessageActionAndFlushOnChange(relationship.MessageAction);
                ProcessRelationshipMessage(relationship);
                break;
            case OmfMessage<DataType> dataTypes:
                SetMessageActionAndFlushOnChange(dataTypes.MessageAction);
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

    private void ProcessRelationshipMessage(RelationshipMessage relationshipMessage)
    {
        _relationships ??= [];
        _relationships.Add(relationshipMessage.Relationship);
    }

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
            if (dataType.Relationships != null)
            {
                _relationships ??= [];
                _relationships.AddRange(dataType.Relationships);
                // Operate on a shallow copy so the relationships are not cleared from the
                // original instance, which may be cached elsewhere (e.g. for resending).
                dataType = dataType.Clone();
                dataType.Relationships = null;
            }

            _currentTypesBatch.Add(dataType);
            _currentTypeCount++;

            if (_currentTypeCount >= _maxTypesBatchCount)
            {
                Flush();
            }
        }
        else if (value is DataStream dataStream)
        {
            _currentStreamsBatch.Add(dataStream);
            _currentStreamCount++;

            if (_currentStreamCount >= _maxStreamsBatchCount)
            {
                Flush();
            }
        }
    }

    private void AddValuesToCurrentBatch<T>(IReadOnlyCollection<T> values)
    {
        if (values is IEnumerable<DataType> dataTypes)
        {
            foreach (var dataType in dataTypes)
            {                
                AddValueToCurrentBatch(dataType);
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
                    Flush();
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
        var relationshipCount = _relationships?.Count ?? 0;
        if (_currentStreamCount == 0 && _currentTypeCount == 0 && relationshipCount == 0)
        {
            return;
        }

        DataStream[] streamsArray = Array.Empty<DataStream>();
        DataType[] typesArray = Array.Empty<DataType>();
        Link[] relationshipsArray = Array.Empty<Link>();
        var streamCount = _currentStreamCount;
        var typeCount = _currentTypeCount;

        if (_currentStreamCount != 0)
        {
            streamsArray = ArrayPool<DataStream>.Shared.Rent(_currentStreamCount);
            _currentStreamsBatch.CopyTo(streamsArray, 0);

            _currentStreamCount = 0;
            _currentStreamsBatch.Clear();
        }

        if (_currentTypeCount != 0)
        {
            typesArray = ArrayPool<DataType>.Shared.Rent(_currentTypeCount);
            _currentTypesBatch.CopyTo(typesArray, 0);

            _currentTypeCount = 0;
            _currentTypesBatch.Clear();
        }

        if (relationshipCount > 0)
        {
            relationshipsArray = ArrayPool<Link>.Shared.Rent(_relationships.Count);
            _relationships.CopyTo(relationshipsArray, 0);
            _relationships.Clear();
        }

#pragma warning disable CA2000 // Dispose objects before losing scope
        _flush(new SchemaMessage(typesArray, streamsArray, relationshipsArray, typeCount, streamCount, relationshipCount, _messageAction, true));
#pragma warning restore CA2000 // Dispose objects before losing scope

        _lastFlush = Environment.TickCount64;
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
        return Environment.TickCount64 - _lastFlush >= _maxFlushTime && (_currentStreamCount > 0 || _currentTypeCount > 0);
    }

    private void SetMessageActionAndFlushOnChange(MessageAction incomingMessageAction)
    {
        if (_messageAction != incomingMessageAction)
        {
            if (_currentTypeCount != 0 || _currentStreamCount != 0 || _relationships != null)
            {
                Flush();
            }

            _messageAction = incomingMessageAction;
        }
    }

    #endregion
}
