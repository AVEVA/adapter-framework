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
public sealed class InstanceGroupingBlock : BaseBlock<Message>
{
    #region Private Constants

    private const int MinBatchCount = 1;
    private const int MinFlushTime = 15;
    private const int DisposalDelay = 15 * 1000;
    private const string OutOfRangeExceptionTemplate = "Parameter must be at least {0}.";
    private const int DefaultListCapacity = 5;

    #endregion

    #region Private Fields

    private readonly Action<Message> _flush;
    private readonly Action<Message> _flushTypesStreams;
    private readonly Timer _flushTimer;
    private readonly int _maxFlushTime;
    private readonly Dictionary<string, List<object>> _dynamicMessages;
    private readonly Dictionary<PartitionKey, Dictionary<string, List<object>>> _dynamicMessagesWithPartitionKey;
    private List<Link> _relationships;    
    private int _maxBatchCount;
    private int _totalStreamingDataCount;    
    private int _totalInstanceCount;
    private List<StaticStreamData> _staticMessages;    
    private List<EventMessage> _eventMessages;    
    private MessageAction _messageAction;
    private int _lastFlush;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceGroupingBlock"/> class.
    /// </summary>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="capacity"> The max number of messages that may be buffered by the block. Use <see cref="DataflowBlockOptions"/> unbounded capacity.</param>
    /// <param name="flush">The action to be performed when <paramref name="maxBatchCount"/> or <paramref name="maxFlushTime"/> has been reached.</param>
    /// <param name="flushTypesStreams">The action to signal <see cref="TypesStreamsGroupingBlock"/> to flush before <paramref name="flush"/> actions is performed.</param>
    /// <param name="maxBatchCount">The max number of messages to batch before flushing.</param>
    /// <param name="maxFlushTime">The max time in milliseconds before batched messages get flushed.</param>
    /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
    public InstanceGroupingBlock(
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

        _dynamicMessages = new Dictionary<string, List<object>>(maxBatchCount / 4);
        _dynamicMessagesWithPartitionKey = new(maxBatchCount / 4);
        _lastFlush = Environment.TickCount;
        _flushTimer = new Timer(HandleTimer, null, maxFlushTime, maxFlushTime);
    }

    #endregion

    private static readonly Action<ILogger, int, int, Exception> _batchSizeChanged =
    LoggerMessage.Define<int, int>(
        LogLevel.Debug,
        new EventId(1, nameof(LogBatchSizeChanged)),
        "Data batch size change. Previous = {PreviousBatchCount}. New size = {NewBatchCount}.");

    public static void LogBatchSizeChanged(ILogger logger, int previousBatchCount, int newBatchCount)
    {
        _batchSizeChanged(logger, previousBatchCount, newBatchCount, null);
    }

    #region Protected Methods

    /// <inheritdoc/>
    protected override void Handle(Message message)
    {
        switch (message)
        {
            case CommandMessage command:
                ProcessCommand(command);
                break;
            case EventMessage eventMessage:
                SetMessageActionAndFlushOnChange(eventMessage.MessageAction);
                ProcessData(eventMessage);
                break;
            case RelationshipMessage relationship:
                SetMessageActionAndFlushOnChange(relationship.MessageAction);
                ProcessRelationship(relationship);
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

        Flush(_messageAction);

        _flushTimer.Dispose();

        base.Dispose(true);
    }

    #endregion

    #region Private Methods

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

        Flush(_messageAction);
    }

    private void ProcessRelationship(RelationshipMessage relationship)
    {   
        _relationships ??= new List<Link>(DefaultListCapacity);
        _relationships.Add(relationship.Relationship);
        _totalInstanceCount++;

        if (_totalInstanceCount >= _maxBatchCount)
        {
            Flush(_messageAction);
        }
    }

    private void ProcessData(DataMessage data)
    {
        ProcessIncomingData(data);

        if (_totalInstanceCount >= _maxBatchCount)
        {
            Flush(_messageAction);
        }
    }

    private void ProcessData(EventMessage eventMessage)
    {
        _eventMessages ??= new List<EventMessage>(DefaultListCapacity);

        if (eventMessage.Relationships != null) 
        {
            _relationships ??= new List<Link>(DefaultListCapacity);
            _relationships.AddRange(eventMessage.Relationships);
            eventMessage.Relationships = null;
        }

        _eventMessages.Add(eventMessage);

        _totalInstanceCount++;

        if (_totalInstanceCount >= _maxBatchCount)
        {
            Flush(_messageAction);
        }
    }

    private void ProcessStaticData(StaticDataMessage staticData)
    {
        _staticMessages ??= new List<StaticStreamData>(DefaultListCapacity);
        if (staticData.Relationships != null)
        {
            _relationships ??= new List<Link>(DefaultListCapacity);
            _relationships.AddRange(staticData.Relationships);
        }

        _staticMessages.Add(new StaticStreamData
        {
            Id = staticData.Id,
            InstanceId = staticData.InstanceId,
            Name = staticData.Name,
            Description = staticData.Description,
            DataSource = staticData.DataSource,
            Properties = staticData.ExtendedPropertiesDefinition,
            PropertyOverrides = staticData.PropertyOverrides,
            Value = staticData.Instance,
            Tags = staticData.Tags,
            Metadata = staticData.Metadata,
        });

        _totalInstanceCount++;

        if (_totalInstanceCount >= _maxBatchCount)
        {
            Flush(_messageAction);
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
            if (_totalInstanceCount >= _maxBatchCount)
            {
                Flush(_messageAction);
            }
        }
    }

    private void ProcessState(StateMessage state)
    {
        LogBatchSizeChanged(Logger, _maxBatchCount, state.BatchCount);

        _maxBatchCount = state.BatchCount;

        if (_maxBatchCount <= _totalInstanceCount)
        {
            Flush(_messageAction);
        }
    }

    private void SetMessageActionAndFlushOnChange(MessageAction messageAction)
    {
        if (messageAction != _messageAction)
        {
            Flush(_messageAction);
            _messageAction = messageAction;
        }
    }

    private void Flush(MessageAction messageAction)
    {
        if (_totalInstanceCount == 0)
        {
            return;
        }

        _flushTypesStreams(new CommandMessage(true));

        var streamingDataArray = Array.Empty<StreamingDataInstance>();        
        var entitiesArray = Array.Empty<StaticStreamData>();
        var relationshipsArray = Array.Empty<Link>();
        var eventsArray = Array.Empty<Event>();

        var streamingDataCount = _dynamicMessages.Count;        
        var entitiesCount = _staticMessages?.Count ?? 0;
        var eventsCount = _eventMessages?.Count ?? 0;
        var linkCount = _relationships?.Count ?? 0;
                
        if (_dynamicMessagesWithPartitionKey.Count > 0)
        {
            foreach ((PartitionKey partitionKey, Dictionary<string, List<object>> dictionary) in _dynamicMessagesWithPartitionKey)
            {
                var streamingDataObjectCount = dictionary.Count;
                var streamingDataArrayWithPartitionKey = ArrayPool<StreamingDataInstance>.Shared.Rent(streamingDataObjectCount);

                var streamingDataTotalCount = 0;
                var index = 0;

                foreach ((string id, List<object> values) in dictionary)
                {
                    streamingDataArrayWithPartitionKey[index++] = new StreamingDataInstance
                    {
                        Id = id,
                        Values = values,
                    };

                    streamingDataTotalCount += values.Count;
                }

#pragma warning disable CA2000 // Dispose objects before losing scope
                var instanceMessageWithPartitionKey = new InstanceMessage(
                    streamingDataArrayWithPartitionKey,
                    entities: null,
                    events: null,
                    relationships: null,
                    streamingDataObjectCount,
                    streamingDataTotalCount,
                    entitiesCount: 0,
                    eventsCount: 0,
                    relationshipCount: 0,
                    messageAction,
                    true,
                    partitionKey);
#pragma warning restore CA2000 // Dispose objects before losing scope

                _flush(instanceMessageWithPartitionKey);
                _totalInstanceCount -= streamingDataTotalCount;
                _lastFlush = Environment.TickCount;
            }

            _dynamicMessagesWithPartitionKey.Clear();

            if (_totalInstanceCount == 0)
            {
                return;
            }
        }

        if (streamingDataCount > 0)
        {
            streamingDataArray = ArrayPool<StreamingDataInstance>.Shared.Rent(streamingDataCount);            
            var index = 0;
            foreach (var item in _dynamicMessages)
            {
                streamingDataArray[index++] = new StreamingDataInstance
                {
                    Id = item.Key,
                    Values = item.Value,
                };
            }
        }

        if (entitiesCount > 0)
        {
            entitiesArray = ArrayPool<StaticStreamData>.Shared.Rent(entitiesCount);
            _staticMessages.CopyTo(entitiesArray);
            _staticMessages.Clear();
            _staticMessages = null;
        }

        if (linkCount > 0)
        {
            relationshipsArray = ArrayPool<Link>.Shared.Rent(linkCount);
            _relationships.CopyTo(relationshipsArray);
            _relationships.Clear();
            _relationships = null;
        }

        if (eventsCount > 0)
        {
            eventsArray = ArrayPool<Event>.Shared.Rent(eventsCount);
            for (var i = 0; i < eventsCount; i++)
            {
                var eventInstance = new Event()
                {
                    Id = _eventMessages[i].InstanceId,
                    TypeId = _eventMessages[i].Id,
                    Name = _eventMessages[i].Name,
                    Description = _eventMessages[i].Description,
                    StartTime = _eventMessages[i].StartTime,
                    EndTime = _eventMessages[i].EndTime,
                    DataSource = _eventMessages[i].DataSource,
                    Tags = _eventMessages[i].Tags,
                    Properties = _eventMessages[i].ExtendedPropertiesDefinition,
                    PropertyOverrides = _eventMessages[i].PropertyOverrides,
                    Metadata = _eventMessages[i].Metadata,
                    Value = _eventMessages[i].Instance,
                };

                eventsArray[i] = eventInstance;
            }

            _eventMessages.Clear();
            _eventMessages = null;
        }

#pragma warning disable CA2000 // Dispose objects before losing scope
        var instanceMessage = new InstanceMessage(
            streamingDataArray,
            entitiesArray,
            eventsArray,
            relationshipsArray,
            streamingDataCount,
            _totalStreamingDataCount,
            entitiesCount,
            eventsCount,
            linkCount,
            messageAction,
            true);
#pragma warning restore CA2000 // Dispose objects before losing scope

        _flush(instanceMessage);
        _totalStreamingDataCount = 0;
        _totalInstanceCount = 0;
        _dynamicMessages.Clear();
        _lastFlush = Environment.TickCount;
    }

    private bool FlushDue()
    {
        return Environment.TickCount - _lastFlush >= _maxFlushTime && _totalInstanceCount > 0;
    }

    private void ProcessIncomingData(DataMessage data)
    {
        if (data.PartitionKey != null)
        {
            var dynamicValues = GetValuesCollectionFromDynamicMessagesWithPartitionKey(data.PartitionKey.Value, data.Id);

            dynamicValues.Add(data.Instance);            
            _totalInstanceCount++;
            return;
        }

        ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(_dynamicMessages, data.Id, out var exists);
        if (!exists)
        {
            values = new List<object>(DefaultListCapacity);
        }

        values.Add(data.Instance);
        _totalStreamingDataCount++;
        _totalInstanceCount++;
    }

    private void ProcessIncomingDataBulkInChunks(BulkDataMessage bulkedData)
    {
        if (bulkedData.PartitionKey != null)
        {
            var dynamicValues = GetValuesCollectionFromDynamicMessagesWithPartitionKey(bulkedData.PartitionKey.Value, bulkedData.Id);

            foreach (var dataValue in bulkedData.Instances)
            {
                dynamicValues.Add(dataValue);                
                _totalInstanceCount++;

                if (_totalInstanceCount >= _maxBatchCount)
                {
                    Flush(_messageAction);
                    dynamicValues = GetValuesCollectionFromDynamicMessagesWithPartitionKey(bulkedData.PartitionKey.Value, bulkedData.Id);
                }
            }

            return;
        }

        var values = GetOrCreateValuesCollection(bulkedData.Id);

        foreach (var dataValue in bulkedData.Instances)
        {
            values.Add(dataValue);
            _totalStreamingDataCount++;
            _totalInstanceCount++;

            if (_totalInstanceCount >= _maxBatchCount)
            {
                Flush(_messageAction);
                values = GetOrCreateValuesCollection(bulkedData.Id);
            }
        }
    }

    private bool IsOverLimit(int valueUpdatesCount) => _totalInstanceCount + valueUpdatesCount > _maxBatchCount;

    private void AddBulkedValues(BulkDataMessage bulkedData)
    {
        if (bulkedData.PartitionKey != null)
        {
            var dynamicValues = GetValuesCollectionFromDynamicMessagesWithPartitionKey(bulkedData.PartitionKey.Value, bulkedData.Id);
            dynamicValues.AddRange(bulkedData.Instances);            
        }
        else
        {
            var instances = GetOrCreateValuesCollection(bulkedData.Id);
            instances.AddRange(bulkedData.Instances);
            _totalStreamingDataCount += bulkedData.Instances.Count;
        }
        
        _totalInstanceCount += bulkedData.Instances.Count;
    }

    private List<object> GetOrCreateValuesCollection(string id)
    {
        ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(_dynamicMessages, id, out var exists);
        if (!exists)
        {
            values = new List<object>(DefaultListCapacity);
        }

        return values;
    }

    private List<object> GetValuesCollectionFromDynamicMessagesWithPartitionKey(PartitionKey partitionKey, string id)
    {
        ref var dictionary = ref CollectionsMarshal.GetValueRefOrAddDefault(_dynamicMessagesWithPartitionKey, partitionKey, out var dictionaryExists);
        if (!dictionaryExists)
        {
            dictionary = new Dictionary<string, List<object>> { { id, new List<object>(DefaultListCapacity) } };
        }

        ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary, id, out var valuesExist);
        if (!valuesExist)
        {
            values = new List<object>(DefaultListCapacity);
        }

        return values;
    }

    #endregion
}
