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
using System.Runtime.InteropServices;
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
/// Provides a dataFlow block that will group every data element received by <see cref="DataMessage"/>'s Id.
/// </summary>
[DebuggerDisplay("Buffered Messages = {MessageHandler.InputCount}")]
public sealed class DataGroupingBlock : BaseBlock<Message>
{
    #region Private Constants

    private const int MinBatchCount = 1;
    private const int MinFlushTime = 15;
    private const int DisposalDelay = 15 * 1000;
    private const string OutOfRangeExceptionTemplate = "Parameter must be at least {0}.";

    #endregion

    #region Private Fields

    private readonly Action<Message> _flush;
    private readonly Action<Message> _flushTypesStreams;
    private readonly Timer _flushTimer;
    private readonly int _maxFlushTime;
    private int _maxBatchCount;
    private int _currentCount;
    private List<StaticDataMessage> _staticMessages;
    private Dictionary<string, (Classification, List<object>)> _staticAndDynamicMessages;
    private Dictionary<PartitionKey, Dictionary<string, List<object>>> _dynamicMessagesWithPartitionKey;
    private MessageAction _messageAction;
    private int _lastFlush;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DataGroupingBlock"/> class.
    /// </summary>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="capacity"> The max number of messages that may be buffered by the block. Use <see cref="DataflowBlockOptions"/> unbounded capacity.</param>
    /// <param name="flush">The action to be performed when <paramref name="maxBatchCount"/> or <paramref name="maxFlushTime"/> has been reached.</param>
    /// <param name="flushTypesStreams">The action to signal <see cref="TypesStreamsGroupingBlock"/> to flush before <paramref name="flush"/> actions is performed.</param>
    /// <param name="maxBatchCount">The max number of messages to batch before flushing.</param>
    /// <param name="maxFlushTime">The max time in milliseconds before batched messages get flushed.</param>
    /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
    public DataGroupingBlock(
        ILogger logger,
        int capacity,
        Action<Message> flush,
        Action<Message> flushTypesStreams,
        int maxBatchCount,
        int maxFlushTime,
        CancellationToken token)
        : base(logger, capacity, false, false, token)
    {
        ThrowHelper.ThrowIfArgumentNull(flush, nameof(flush));
        ThrowHelper.ThrowIfArgumentNull(flushTypesStreams, nameof(flushTypesStreams));

        if (maxBatchCount < MinBatchCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchCount), string.Format(CultureInfo.InvariantCulture, OutOfRangeExceptionTemplate, MinBatchCount));
        }

        _maxBatchCount = maxBatchCount;

        if (maxFlushTime < MinFlushTime)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFlushTime), string.Format(CultureInfo.InvariantCulture, OutOfRangeExceptionTemplate, MinFlushTime));
        }

        _flush = flush;
        _flushTypesStreams = flushTypesStreams;
        _maxFlushTime = maxFlushTime;

        _staticAndDynamicMessages = Factory();
        _dynamicMessagesWithPartitionKey = new(_maxBatchCount);
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
            case StaticDataMessage staticData:
                SetMessageActionAndFlushOnChange(staticData.MessageAction);
                ProcessStaticData(staticData);
                break;
            case DataMessage data:                
                SetMessageActionAndFlushOnChange(data.MessageAction);
                ProcessData(data);
                break;
            case BulkDataMessage bulkedData:
                SetMessageActionAndFlushOnChange(bulkedData.MessageAction);
                ProcessBulkedData(bulkedData);
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

    private Dictionary<string, (Classification Classification, List<object> Instances)> Factory() => new(_maxBatchCount);

    private void HandleTimer(object state)
    {
        if (FlushDue())
        {
            MessageHandler.Post(new CommandMessage(false));
        }
    }

    private void ProcessCommand(CommandMessage command)
    {
        if (!command.ForceFlush && !FlushDue())
        {
            return;
        }

        Flush();
    }

    private void ProcessData(DataMessage data)
    {
        ProcessIncomingData(data);

        if (_currentCount >= _maxBatchCount)
        {
            Flush();
        }
    }

    private void ProcessStaticData(StaticDataMessage staticData)
    {
        if (staticData.ExtendedPropertiesDefinition == null && staticData.InstanceId == null)
        {
            ProcessIncomingData(staticData);
        }
        else
        {
            ProcessIncomingStaticData(staticData);
        }

        if (_currentCount >= _maxBatchCount)
        {
            Flush();
        }
    }

    private void ProcessBulkedData(BulkDataMessage bulkedData)
    {
        if (IsOverLimit(bulkedData.Instances.Count))
        {
            ProcessIncomingDataBulkInChunks(bulkedData);
        }
        else
        {
            AddBulkedValues(bulkedData);
            if (_currentCount >= _maxBatchCount)
            {
                Flush();
            }
        }
    }

    private void ProcessState(StateMessage state)
    {
        Logger?.LogDebug("Data batch size change. Previous = {MaxBatchCount}. New size = {state.BatchCount}.", _maxBatchCount, state.BatchCount);

        _maxBatchCount = state.BatchCount;

        if (_maxBatchCount <= _currentCount)
        {
            Flush();
        }
    }

    private void SetMessageActionAndFlushOnChange(MessageAction messageAction)
    {
        if (messageAction != _messageAction)
        {
            Flush();
            _messageAction = messageAction;
        }
    }

    private void Flush()
    {
        if (_currentCount == 0)
        {
            return;
        }

        _flushTypesStreams(new CommandMessage(true));

        var pendingDynamic = _staticAndDynamicMessages;
        var pendingStatic = _staticMessages;
        var pendingDynamicWithPartitionKey = _dynamicMessagesWithPartitionKey;

        _flush(new GroupedDataMessage(_currentCount, pendingDynamic, pendingStatic, pendingDynamicWithPartitionKey, _messageAction));

        _currentCount = 0;

        _staticMessages = null;
        _staticAndDynamicMessages = Factory();
        _dynamicMessagesWithPartitionKey = new(_maxBatchCount);

        _lastFlush = Environment.TickCount;
    }

    private bool FlushDue()
    {
        return Environment.TickCount - _lastFlush >= _maxFlushTime && _currentCount > 0;
    }

    private void ProcessIncomingData(DataMessage data)
    {
        if (data.PartitionKey != null)
        {
            var valuesWithPartitionKey = GetValuesCollectionFromDynamicMessagesWithPartitionKey(data.PartitionKey.Value, data.Id);

            valuesWithPartitionKey.Add(data.Instance);
            _currentCount++;
            return;
        }

        if (!_staticAndDynamicMessages.TryGetValue(data.Id, out (Classification Classification, List<object> Instances) values))
        {
            values = (data.Classification, new List<object>());
            _staticAndDynamicMessages.Add(data.Id, values);
        }

        values.Instances.Add(data.Instance);

        _currentCount++;
    }

    private void ProcessIncomingStaticData(StaticDataMessage staticData)
    {
        _staticMessages ??= new List<StaticDataMessage>();

        _staticMessages.Add(staticData);
        _currentCount++;
    }

    private void ProcessIncomingDataBulkInChunks(BulkDataMessage bulkedData)
    {
        if (bulkedData.PartitionKey != null)
        {
            var valuesWithPartitionKey = GetValuesCollectionFromDynamicMessagesWithPartitionKey(bulkedData.PartitionKey.Value, bulkedData.Id);

            foreach (var dataValue in bulkedData.Instances)
            {
                valuesWithPartitionKey.Add(dataValue);
                _currentCount++;

                if (_currentCount >= _maxBatchCount)
                {
                    Flush();

                    valuesWithPartitionKey = [];
                    _dynamicMessagesWithPartitionKey.Add(bulkedData.PartitionKey.Value, new Dictionary<string, List<object>> { { bulkedData.Id, valuesWithPartitionKey } });

                }
            }

            return;
        }

        var values = GetValuesCollection(bulkedData);

        foreach (var dataValue in bulkedData.Instances)
        {
            values.Instances.Add(dataValue);
            _currentCount++;

            if (_currentCount >= _maxBatchCount)
            {
                Flush();

                values = (bulkedData.Classification, new List<object>());

                _staticAndDynamicMessages.Add(bulkedData.Id, values);
            }
        }
    }

    private bool IsOverLimit(int valueUpdatesCount) => _currentCount + valueUpdatesCount > _maxBatchCount;

    private void AddBulkedValues(BulkDataMessage bulkedData)
    {
        if (bulkedData.PartitionKey != null)
        {
            var instancesWithPartitionKey = GetValuesCollectionFromDynamicMessagesWithPartitionKey(bulkedData.PartitionKey.Value, bulkedData.Id);

            instancesWithPartitionKey.AddRange(bulkedData.Instances);
        }
        else
        {
            var (_, instances) = GetValuesCollection(bulkedData);

            instances.AddRange(bulkedData.Instances);
        }

        _currentCount += bulkedData.Instances.Count;
    }

    private (Classification Classification, List<object> Instances) GetValuesCollection(BulkDataMessage bulkedData)
    {
        if (!_staticAndDynamicMessages.TryGetValue(bulkedData.Id, out (Classification Classification, List<object> Instances) values))
        {
            values = (bulkedData.Classification, new List<object>());

            _staticAndDynamicMessages.Add(bulkedData.Id, values);
        }

        return values;
    }

    private List<object> GetValuesCollectionFromDynamicMessagesWithPartitionKey(PartitionKey partitionKey, string id)
    {
        ref var dictionary = ref CollectionsMarshal.GetValueRefOrAddDefault(_dynamicMessagesWithPartitionKey, partitionKey, out var dictionaryExists);
        if (!dictionaryExists)
        {
            dictionary = new Dictionary<string, List<object>> { { id, [] } };
        }

        ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, id, out var valuesExist);
        if (!valuesExist)
        {
            values = [];
        }

        return values;
    }

    #endregion
}
