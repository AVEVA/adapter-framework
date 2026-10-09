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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Data flow block that handles serialization and optional compression of passed <see cref="Message"/>.
/// </summary>
public class SerializationBlock : BaseBlock<Message>
{
    private const int EmptyMessageLength = 2;
    private readonly BatchingStrategyOptimizer _dataBatchingStrategyOptimizer;
    private readonly BatchingStrategyOptimizer _streamsBatchingStrategyOptimizer;
    private readonly Action<ISerializedOmfMessage> _flushAction;
    private readonly ISerializer _serializer;
    private readonly ICompressor _compressor;
    private readonly ILogger _logger;
    private readonly int _maxByteCount;
    private readonly OmfVersion _omfVersion;
    private readonly OmfAwaitableCoordinator _coordinator;
    private readonly Dictionary<ScopeToken, int> _materializationBarrierCounts = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializationBlock"/> class.
    /// </summary>
    /// <param name="dataBatchingStrategyOptimizer">Data batching strategy optimizer instance.</param>
    /// <param name="streamsBatchingStrategyOptimizer">Streams batching strategy optimizer instance.</param>
    /// <param name="maxByteCount">Maximum resulting serialized message size in bytes.</param>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="capacity"> The max number of messages that may be buffered by the block. Use <see cref="DataflowBlockOptions"/> unbounded capacity.</param>
    /// <param name="serializer"><see cref="ISerializer"/> service instance used to serialize <see cref="DataType"/>, <see cref="DataStream"/> or <see cref="GroupedDataMessage"/> messages.</param>
    /// <param name="compressor"><see cref="ICompressor"/> service instance used to compress serialized payload before creating <see cref="SerializedOmfMessage"/>.</param>
    /// <param name="flushAction">The action to be performed when <see cref="SerializedOmfMessage"/> message is created.</param>
    /// <param name="token">Cancellation token.</param>
    /// <param name="omfVersion">Version of OMF to be emitted.</param>
    /// <param name="awaitableCoordinator">The coordinator that tracks awaitable scopes, or <c>null</c> when scopes are not supported.</param>
    public SerializationBlock(
        BatchingStrategyOptimizer dataBatchingStrategyOptimizer,
        BatchingStrategyOptimizer streamsBatchingStrategyOptimizer,
        int maxByteCount,
        ILogger logger,
        int capacity,
        ISerializer serializer,
        ICompressor compressor,
        Action<ISerializedOmfMessage> flushAction,
        CancellationToken token,
        OmfVersion omfVersion = OmfVersion.Omf12,
        OmfAwaitableCoordinator awaitableCoordinator = null)
        : base(logger, capacity, false, false, token)
    {
        ThrowHelper.ThrowIfArgumentNull(serializer, nameof(serializer));

        _dataBatchingStrategyOptimizer = dataBatchingStrategyOptimizer;
        _streamsBatchingStrategyOptimizer = streamsBatchingStrategyOptimizer;
        _maxByteCount = maxByteCount;
        _serializer = serializer;
        _compressor = compressor;
        _flushAction = flushAction;
        _logger = logger;
        _omfVersion = omfVersion;
        _coordinator = awaitableCoordinator;
    }

    /// <inheritdoc/>
    protected override void Handle(Message message)
    {
        switch (message)
        {
            case InstanceMessage instanceMessage:
                using (instanceMessage)
                {
                    ProcessInstance(instanceMessage);
                }

                break;
            case SchemaMessage schemaMessage:
                using (schemaMessage)
                {
                    ProcessSchema(schemaMessage);
                }

                break;
            case OmfMessage<DataType> omfTypes:
                Process(omfTypes.Count, omfTypes.Values, MessageType.Type, omfTypes.MessageAction);
                break;
            case OmfMessage<DataStream> omfContainers:
                Process(omfContainers.Count, omfContainers.Values, MessageType.Container, omfContainers.MessageAction);
                break;
            case OmfMessage<StaticStreamData> staticStreamData:
                Process(staticStreamData.Count, staticStreamData.Values, MessageType.StaticData, staticStreamData.MessageAction);
                break;
            case OmfMessage<DynamicStreamData> dynamicStreamData:
                Process(dynamicStreamData.Count, dynamicStreamData.Values, MessageType.DynamicData, dynamicStreamData.MessageAction, dynamicStreamData.PartitionKey);
                break;
            case ScopeMaterializationBarrier barrier:
                ProcessMaterializationBarrier(barrier);
                break;
        }
    }

    /// <inheritdoc/>
    protected override Task HandleAsync(Message message)
    {
        throw new NotImplementedException();
    }

    private static ArraySegment<T> GetArraySegment<T>(T[] array, int maxSegmentLength, ref int offset)
    {
        int remaining = array.Length - offset;

        int segmentLength = Math.Min(remaining, maxSegmentLength);

        var segment = new ArraySegment<T>(array, offset, segmentLength);

        offset += segmentLength;

        return segment;
    }

    private static ArraySegment<object> GetArraySegment(
        object[] array,
        StreamData streamData,
        int maxSegmentLength,
        ref int offset)
    {
        int remaining = array.Length - offset;

        int segmentLength = Math.Min(remaining, maxSegmentLength);

        var segment = new ArraySegment<object>(array, offset, segmentLength);

        offset += segmentLength;

        streamData.Values = [.. segment];

        return segment;
    }

    private static int GetStreamDataValuesCount<T>(ArraySegment<T> items, MessageType type)
    {
        if (type != MessageType.StaticData && type != MessageType.DynamicData && type != MessageType.Instance)
        {
            return 0;
        }

        var numItems = 0;

        foreach (var item in items)
        {
            if (item is StreamData streamData)
            {
                numItems += streamData.Values is ICollection<object> valuesCollection
                    ? valuesCollection.Count
                    : streamData.Values.Count();
            }
        }

        return numItems;
    }

    private void ProcessInstance(InstanceMessage message)
    {
        var sidecar = message.Sidecar;
        ArraySegment<StreamData> streamingDataSegment = default;
        ArraySegment<StaticStreamData> entitiesSegment = default;
        ArraySegment<Event> eventsSegment = default;
        ArraySegment<Link> relationshipsSegment = default;

        if (message.StreamingDataObjectCount > 0)
        {
            streamingDataSegment = new ArraySegment<StreamData>(message.StreamingData, 0, message.StreamingDataObjectCount);
        }

        if (message.EntitiesCount > 0)
        {
            entitiesSegment = new ArraySegment<StaticStreamData>(message.Entities, 0, message.EntitiesCount);
        }

        if (message.EventsCount > 0)
        {
            eventsSegment = new ArraySegment<Event>(message.Events, 0, message.EventsCount);
        }

        if (message.RelationshipCount > 0)
        {
            relationshipsSegment = new ArraySegment<Link>(message.Relationships, 0, message.RelationshipCount);
        }

        var bytes = SerializeWithWrapper(new InstanceMessageWrapper
        {
            StreamingData = streamingDataSegment,
            Entities = entitiesSegment,
            Events = eventsSegment,
            Relationships = relationshipsSegment,
        });

        var byteCount = bytes.Length;
        var over = byteCount > _maxByteCount;
        var instanceItemCount = message.StreamingDataTotalCount + message.EntitiesCount + message.EventsCount + message.RelationshipCount;

        if (over)
        {
            _logger.LogTrace("The Instance message has {EventsCount} events, {EntitiesCount} entities, {RelationshipCount} links, and {StreamingDataTotalCount} streaming data values. Total byte count {ByteCount} is larger than {MaxByteCount}. The message will be split and flushed in chunks if any part is still oversize.", message.EventsCount, message.EntitiesCount, message.RelationshipCount, message.StreamingDataTotalCount, byteCount, _maxByteCount);

            if (message.EventsCount > 0 || message.EntitiesCount > 0 || message.RelationshipCount > 0)
            {
                var bytesWithoutStreamingData = SerializeWithWrapper(new InstanceMessageWrapper
                {
                    Events = eventsSegment,
                    Entities = entitiesSegment,
                    Relationships = relationshipsSegment,                    
                });

                if (bytesWithoutStreamingData.Length > _maxByteCount)
                {
                    if (message.EventsCount > 0)
                    {
                        var eventsBytes = SerializeWithWrapper(new InstanceMessageWrapper
                        {
                            Events = eventsSegment,
                        });

                        if (eventsBytes.Length > _maxByteCount)
                        {
                            FlushInChunksInstance(eventsSegment.ToArray(), eventsBytes.Length, message.MessageAction, slice: new Slice(sidecar, ScopeSidecarKind.Events));
                        }
                        else
                        {
                            Flush(message.EventsCount, MessageType.Instance, eventsBytes, message.MessageAction, membership: Membership(sidecar, ScopeSidecarKind.Events, message.EventsCount));
                        }
                    }
                    
                    if (message.EntitiesCount > 0 || message.RelationshipCount > 0)
                    {
                        var bytesOfEntitiesAndLinks = SerializeWithWrapper(new InstanceMessageWrapper
                        {   
                            Entities = entitiesSegment,
                            Relationships = relationshipsSegment,
                        });

                        if (bytesOfEntitiesAndLinks.Length > _maxByteCount)
                        {
                            if (message.EntitiesCount > 0)
                            {
                                var entitiesBytes = SerializeWithWrapper(new InstanceMessageWrapper
                                {
                                    Entities = entitiesSegment,
                                });

                                if (entitiesBytes.Length > _maxByteCount)
                                {
                                    FlushInChunksInstance(entitiesSegment.ToArray(), entitiesBytes.Length, message.MessageAction, slice: new Slice(sidecar, ScopeSidecarKind.Entities));
                                }
                                else
                                {
                                    Flush(message.EntitiesCount, MessageType.Instance, entitiesBytes, message.MessageAction, membership: Membership(sidecar, ScopeSidecarKind.Entities, message.EntitiesCount));
                                }
                            }

                            if (message.RelationshipCount > 0)
                            {
                                var linksBytes = SerializeWithWrapper(new InstanceMessageWrapper
                                {
                                    Relationships = relationshipsSegment,
                                });

                                if (linksBytes.Length > _maxByteCount)
                                {
                                    FlushInChunksInstance(relationshipsSegment.ToArray(), linksBytes.Length, message.MessageAction, slice: new Slice(sidecar, ScopeSidecarKind.Relationships));
                                }
                                else
                                {
                                    Flush(message.RelationshipCount, MessageType.Instance, linksBytes, message.MessageAction, membership: Membership(sidecar, ScopeSidecarKind.Relationships, message.RelationshipCount));
                                }
                            }
                        }
                        else
                        {
                            Flush(message.EntitiesCount + message.RelationshipCount, MessageType.Instance, bytesOfEntitiesAndLinks, message.MessageAction,
                                membership: Membership(sidecar, ScopeSidecarKind.Entities, message.EntitiesCount, Membership(sidecar, ScopeSidecarKind.Relationships, message.RelationshipCount)));
                        }
                    }
                }
                else // if Events + Entities + Relationships not oversize
                {
                    Flush(message.EventsCount + message.EntitiesCount + message.RelationshipCount, MessageType.Instance, bytesWithoutStreamingData, message.MessageAction,
                        membership: DiscreteMembership(message));
                }
            }

            if (message.StreamingDataObjectCount > 0)
            {
                var streamingDataBytes = SerializeWithWrapper(new InstanceMessageWrapper
                {
                    StreamingData = streamingDataSegment,
                });

                if (streamingDataBytes.Length > _maxByteCount)
                {
                    _logger.LogTrace("The Instance message's StreamingData byte count {StreamingDataByteCount} is larger than {MaxByteCount}. Flushing the StreamingData in chunks.", streamingDataBytes.Length, _maxByteCount);
                    FlushInChunksInstance(streamingDataSegment.ToArray(), streamingDataBytes.Length, message.MessageAction, message.PartitionKey, new Slice(sidecar, ScopeSidecarKind.StreamingData));
                }
                else
                {
                    Flush(message.StreamingDataTotalCount, MessageType.Instance, streamingDataBytes, message.MessageAction, message.PartitionKey,
                        Membership(sidecar, ScopeSidecarKind.StreamingData, message.StreamingDataObjectCount));
                }
            }
        }
        else // if the whole Instance message is not oversize
        {
            Flush(instanceItemCount, MessageType.Instance, bytes, message.MessageAction, message.PartitionKey,
                Membership(sidecar, ScopeSidecarKind.StreamingData, message.StreamingDataObjectCount, DiscreteMembership(message)));
        }

        // OMF 2.0 instance messages participate in batch optimization.
        if (instanceItemCount > 0)
        {
            _dataBatchingStrategyOptimizer?.Update(instanceItemCount, byteCount, over);
        }
    }

    private void ProcessSchema(SchemaMessage message)
    {
        var sidecar = message.Sidecar;
        ArraySegment<DataType> typesSegment = null;
        ArraySegment<DataStream> streamsSegment = null;
        ArraySegment<Link> relationshipsSegment = null;

        if (message.TypeCount > 0)
        {
            typesSegment = new ArraySegment<DataType>(message.Types, 0, message.TypeCount);
        }

        if (message.ContainerCount > 0)
        {
            streamsSegment = new ArraySegment<DataStream>(message.Containers, 0, message.ContainerCount);
        }

        if (message.RelationshipCount > 0)
        {
            relationshipsSegment = new ArraySegment<Link>(message.Relationships, 0, message.RelationshipCount);
        }

        var bytes = SerializeWithWrapper(new SchemaMessageWrapper
        {
            Types = typesSegment,
            Streams = streamsSegment,
            Relationships = relationshipsSegment,
        });

        var byteCount = bytes.Length;
        var over = byteCount > _maxByteCount;
        var count = message.TypeCount + message.ContainerCount + message.RelationshipCount;

        if (over)
        {
            _logger.LogTrace("The Schema message has {TypeCount} types, {ContainerCount} streams and {RelationshipCount} links. Total byte count {ByteCount} is larger than {MaxByteCount}. The message will be split by the types part, streams part and links part, and be flushed in chunks if any part is still oversize.", message.TypeCount, message.ContainerCount, message.RelationshipCount, byteCount, _maxByteCount);

            if (message.TypeCount > 0)
            {
                var typesBytes = SerializeWithWrapper(new SchemaMessageWrapper { Types = typesSegment, });

                if (typesBytes.Length > _maxByteCount)
                {
                    FlushInChunksSchema(typesSegment.ToArray(), typesBytes.Length, message.MessageAction, new Slice(sidecar, ScopeSidecarKind.Types));
                }
                else
                {
                    Flush(message.TypeCount, MessageType.Schema, typesBytes, message.MessageAction, membership: Membership(sidecar, ScopeSidecarKind.Types, message.TypeCount));
                }
            }

            if (message.ContainerCount > 0)
            {
                var streamsBytes = SerializeWithWrapper(new SchemaMessageWrapper { Streams = streamsSegment, });

                if (streamsBytes.Length > _maxByteCount)
                {
                    FlushInChunksSchema(streamsSegment.ToArray(), streamsBytes.Length, message.MessageAction, new Slice(sidecar, ScopeSidecarKind.Containers));
                }
                else
                {
                    Flush(message.ContainerCount, MessageType.Schema, streamsBytes, message.MessageAction, membership: Membership(sidecar, ScopeSidecarKind.Containers, message.ContainerCount));
                }
            }

            if (message.RelationshipCount > 0)
            {
                var linksBytes = SerializeWithWrapper(new SchemaMessageWrapper { Relationships = relationshipsSegment, });

                if (linksBytes.Length > _maxByteCount)
                {
                    FlushInChunksSchema(relationshipsSegment.ToArray(), linksBytes.Length, message.MessageAction, new Slice(sidecar, ScopeSidecarKind.Relationships));
                }
                else
                {
                    Flush(message.RelationshipCount, MessageType.Schema, linksBytes, message.MessageAction, membership: Membership(sidecar, ScopeSidecarKind.Relationships, message.RelationshipCount));
                }
            }
        }
        else
        {
            Flush(count, MessageType.Schema, bytes, message.MessageAction, membership: Membership(sidecar, ScopeSidecarKind.Types, message.TypeCount,
                Membership(sidecar, ScopeSidecarKind.Containers, message.ContainerCount, Membership(sidecar, ScopeSidecarKind.Relationships, message.RelationshipCount))));
        }

        if (count > 0)
        {
            _streamsBatchingStrategyOptimizer?.Update(count, byteCount, over);
        }
    }

    private void Process<T>(
        int count,
        T[] array,
        MessageType messageType,
        MessageAction messageAction,
        PartitionKey? partitionKey = null)
    {
        var bytes = SerializeArray(array);

        var byteCount = bytes.Length;

        var over = byteCount > _maxByteCount;

        if (over)
        {
            FlushInChunks(array, byteCount, messageType, messageAction, partitionKey);
        }
        else
        {
            Flush(count, messageType, bytes, messageAction, partitionKey);
        }

        // For OMF 1.2, optimizer feedback is driven by dynamic data and container messages.
        // OMF 2.0 uses instance and schema message feedback in their dedicated handlers.
        if (messageType == MessageType.DynamicData)
        {
            _dataBatchingStrategyOptimizer?.Update(count, byteCount, over);
        }
        else if (messageType == MessageType.Container)
        {
            _streamsBatchingStrategyOptimizer?.Update(count, byteCount, over);
        }
    }

    private byte[] SerializeWithWrapper<T>(T wrapper)
    {
        return _compressor?.Compress(_serializer.Serialize(wrapper))
            ?? _serializer.Serialize(wrapper);
    }

    private byte[] SerializeArray<T>(T[] array)
    {
        return _compressor?.Compress(_serializer.Serialize(array))
            ?? _serializer.Serialize(array);
    }

    private void FlushInChunksSchema<T>(T[] array, int byteCount, MessageAction messageAction, Slice slice = default)
    {
        if (typeof(T) != typeof(DataType) && typeof(T) != typeof(DataStream) && typeof(T) != typeof(Link))
        {
            return;
        }

        SetTuningParameters(array, byteCount, out int maxSegmentLength, out var numberOfSegments, out var offset);

        for (int i = 0; i < numberOfSegments; ++i)
        {
            var segment = GetArraySegment(array, maxSegmentLength, ref offset);

            if (segment.Count == 0)
            {
                return;
            }

            byte[] bytes = typeof(T) switch
            {
                var t when t == typeof(DataType) => SerializeWithWrapper(new SchemaMessageWrapper { Types = segment.Cast<DataType>().ToArray() }),
                var t when t == typeof(DataStream) => SerializeWithWrapper(new SchemaMessageWrapper { Streams = segment.Cast<DataStream>().ToArray() }),
                var t when t == typeof(Link) => SerializeWithWrapper(new SchemaMessageWrapper { Relationships = segment.Cast<Link>().ToArray() }),
                _ => throw new InvalidOperationException($"Unsupported schema type for chunking: {typeof(T).Name}"),
            };

            if (bytes.Length > _maxByteCount)
            {
                if (segment.Count == 1)
                {
                    _logger.LogError("One {Type} is too big to be sent and cannot be reduced. Final byte size {ByteCount}, max {MaxByteCount}.", typeof(T).Name, bytes.Length, _maxByteCount);
                    Discard(slice, segment.Offset, 1, bytes.Length);
                }
                else
                {
                    FlushInChunksSchema(segment.ToArray(), bytes.Length, messageAction, slice.At(segment.Offset));
                }
            }
            else
            {
                Flush(segment.Count, MessageType.Schema, bytes, messageAction, membership: slice.Membership(segment.Offset, segment.Count));
            }
        }
    }

    private void FlushInChunksInstance<T>(T[] array, int byteCount, MessageAction messageAction, PartitionKey? partitionKey = null, Slice slice = default)
    {
        if (typeof(T) != typeof(StaticStreamData) && typeof(T) != typeof(Event) && typeof(T) != typeof(Link) && typeof(T) != typeof(StreamData))
        {
            return;
        }

        SetTuningParameters(array, byteCount, out int maxSegmentLength, out var numberOfSegments, out var offset);

        for (int i = 0; i < numberOfSegments; ++i)
        {
            var segment = GetArraySegment(array, maxSegmentLength, ref offset);

            if (segment.Count == 0)
            {
                return;
            }

            byte[] bytes = typeof(T) switch
            {
                var t when t == typeof(StaticStreamData) => SerializeWithWrapper(new InstanceMessageWrapper { Entities = segment.Cast<StaticStreamData>().ToArray() }),
                var t when t == typeof(Event) => SerializeWithWrapper(new InstanceMessageWrapper { Events = segment.Cast<Event>().ToArray() }),
                var t when t == typeof(Link) => SerializeWithWrapper(new InstanceMessageWrapper { Relationships = segment.Cast<Link>().ToArray() }),
                var t when t == typeof(StreamData) => SerializeWithWrapper(new InstanceMessageWrapper { StreamingData = segment.Cast<StreamingDataInstance>().ToArray() }),
            };

            if (bytes.Length > _maxByteCount)
            {
                if (typeof(T) == typeof(StreamData))
                {
                    FlushInChunks(segment.ToArray(), bytes.Length, MessageType.Instance, messageAction, partitionKey, slice.At(segment.Offset));
                }
                else if (segment.Count == 1)
                {
                    _logger.LogError("One {Type} is too big to be sent and cannot be reduced. Final byte size {ByteCount}, max {MaxByteCount}.", typeof(T).Name, bytes.Length, _maxByteCount);
                    Discard(slice, segment.Offset, 1, bytes.Length);
                }
                else
                {
                    FlushInChunksInstance(segment.ToArray(), bytes.Length, messageAction, slice: slice.At(segment.Offset));
                }
            }
            else
            {
                if (typeof(T) == typeof(StreamData))
                {
                    var count = GetStreamDataValuesCount(segment, MessageType.Instance);
                    Flush(count, MessageType.Instance, bytes, messageAction, partitionKey, slice.Membership(segment.Offset, segment.Count));
                }
                else
                {
                    Flush(segment.Count, MessageType.Instance, bytes, messageAction, membership: slice.Membership(segment.Offset, segment.Count));
                }
            }
        }
    }

    private void FlushInChunks<T>(
        T[] array,
        int byteCount,
        MessageType messageType,
        MessageAction messageAction,
        PartitionKey? partitionKey = null,
        Slice slice = default)
    {
        SetTuningParameters(array, byteCount, out int maxSegmentLength, out var numberOfSegments, out var offset);

        for (int i = 0; i < numberOfSegments; ++i)
        {
            var segment = GetArraySegment(array, maxSegmentLength, ref offset);

            if (segment.Count == 0)
            {
                return;
            }

            byte[] bytes = null;

            if (messageType == MessageType.Instance)
            {
                bytes = SerializeWithWrapper(new InstanceMessageWrapper { StreamingData = segment.Cast<StreamingDataInstance>().ToArray() });
            }
            else
            {
                bytes = _compressor?.Compress(_serializer.Serialize<IEnumerable<T>>(segment)) ??
                            _serializer.Serialize<IEnumerable<T>>(segment);
            }

            if (bytes.Length > _maxByteCount)
            {
                // worst case, recursive call plus copying data to new array chunk...
                // very low probability of ever getting here
                if (array.Length == 1)
                {
                    if (messageType == MessageType.DynamicData || messageType == MessageType.StaticData || messageType == MessageType.Instance)
                    {
                        if (array is StreamData[] dataArray)
                        {
                            FlushInChunksData([.. dataArray[0].Values], byteCount, dataArray[0], messageType, messageAction, partitionKey, slice.ValuesOfFirstStream());
                        }
                        else
                        {
                            _logger.LogError("Data array cannot be converted to {Type}. Skipping the message.", nameof(StreamData));
                        }
                    }
                    else
                    {
                        // very unlikely. Requires a single type or container to be so big that it is > _maxByteCount
                        _logger.LogError("One message of type {Type} is too big to be sent and cannot be reduced. " +
                                         "Final byte size {ByteCount}, max {MaxByteCount}.", messageType, byteCount, _maxByteCount);
                        return;
                    }
                }
                else
                {
                    FlushInChunks(segment.ToArray(), bytes.Length, messageType, messageAction, partitionKey, slice.At(segment.Offset));
                }
            }
            else
            {
                // do not flush [] message
                if (bytes.Length <= EmptyMessageLength)
                {
                    return;
                }

                var count = GetStreamDataValuesCount(segment, messageType);
                Flush(count, messageType, bytes, messageAction, partitionKey, slice.Membership(segment.Offset, segment.Count));
            }
        }
    }

    private void FlushInChunksData(
        object[] array,
        int byteCount,
        StreamData streamData,
        MessageType messageType,
        MessageAction messageAction,
        PartitionKey? partitionKey = null,
        Slice slice = default)
    {
        SetTuningParameters(array, byteCount, out int maxSegmentLength, out var numberOfSegments, out var offset);

        for (int i = 0; i < numberOfSegments; ++i)
        {
            var segment = GetArraySegment(array, streamData, maxSegmentLength, ref offset);

            if (segment.Count == 0)
            {
                return;
            }

            byte[] bytes = null;

            if (messageType == MessageType.Instance)
            {
                bytes = SerializeWithWrapper(new InstanceMessageWrapper { StreamingData = new ArraySegment<StreamData>([streamData]) });
            }
            else
            {
                bytes = _compressor?.Compress(_serializer.Serialize(new[] { streamData })) ??
                               _serializer.Serialize(new[] { streamData });
            }

            if (bytes.Length > _maxByteCount)
            {
                // worst case, recursive call plus copying data to new array chunk...
                // very low probability of ever getting here
                if (array.Length <= 1)
                {
                    _logger.LogError("One message of type OmfData is too big to be sent and cannot be reduced. " +
                                     "Final byte size {ByteCount}, max {MaxByteCount}.", byteCount, _maxByteCount);
                    Discard(slice, segment.Offset, segment.Count, bytes.Length);
                    return;
                }

                FlushInChunksData([.. segment], bytes.Length, streamData, messageType, messageAction, partitionKey, slice.At(segment.Offset));
            }
            else
            {
                Flush(segment.Count, messageType, bytes, messageAction, partitionKey, slice.Membership(segment.Offset, segment.Count));
            }
        }
    }

    private void Flush(
        int count,
        MessageType messageType,
        byte[] bytes,
        MessageAction messageAction,
        PartitionKey? partitionKey = null,
        Dictionary<ScopeToken, int> membership = null)
    {
        var message = new SerializedOmfMessage(messageType, bytes, messageAction, count, _omfVersion, partitionKey);
        if (_coordinator is not null && membership?.Count > 0)
        {
            message.SerializedMessageId = Guid.NewGuid();
            _coordinator.RegisterBody(message.SerializedMessageId.Value, membership);
        }

        _flushAction(message);
    }

    private static Dictionary<ScopeToken, int> Membership(ScopeSidecar sidecar, ScopeSidecarKind kind, int count, Dictionary<ScopeToken, int> membership = null)
    {
        if (sidecar is null || count <= 0)
        {
            return membership;
        }

        membership ??= [];
        sidecar.AddEntries(kind, 0, count, membership);
        return membership;
    }

    private static Dictionary<ScopeToken, int> DiscreteMembership(InstanceMessage message) =>
        Membership(message.Sidecar, ScopeSidecarKind.Events, message.EventsCount,
            Membership(message.Sidecar, ScopeSidecarKind.Entities, message.EntitiesCount,
                Membership(message.Sidecar, ScopeSidecarKind.Relationships, message.RelationshipCount)));

    private void Discard(Slice slice, int start, int count, int byteCount)
    {
        var membership = slice.Membership(start, count);
        if (_coordinator is null || membership is null)
        {
            return;
        }

        var reason = new OmfOutcomeReason(
            OmfReasonCode.ItemTooLarge,
            string.Create(CultureInfo.InvariantCulture, $"An item serializes to {byteCount} bytes, more than the maximum of {_maxByteCount}."));
        foreach (var (scope, itemCount) in membership)
        {
            _coordinator.RecordItemsDiscarded(scope, itemCount, reason);
        }
    }

    private void ProcessMaterializationBarrier(ScopeMaterializationBarrier barrier)
    {
        _materializationBarrierCounts.TryGetValue(barrier.Scope, out var received);
        if (++received < barrier.ExpectedBarrierCount)
        {
            _materializationBarrierCounts[barrier.Scope] = received;
            return;
        }

        _materializationBarrierCounts.Remove(barrier.Scope);
        _coordinator?.CloseMaterialization(barrier.Scope);
    }

    private void SetTuningParameters<T>(
        IReadOnlyCollection<T> array,
        int byteCount,
        out int maxSegmentLength,
        out int numberOfSegments,
        out int offset)
    {
        var factor = (byteCount / _maxByteCount) + 1;

        maxSegmentLength = array.Count / factor;
        var delta = array.Count - (maxSegmentLength * factor);

        if (delta > 0)
        {
            maxSegmentLength = maxSegmentLength + (delta / factor) + 1;
        }

        numberOfSegments = factor;
        offset = 0;
    }

    /// <summary>
    /// Locates an array being chunked within the grouped message whose <see cref="ScopeSidecar"/> describes it.
    /// </summary>
    /// <param name="Sidecar">The sidecar of the grouped message, or <c>null</c> when no item belongs to a scope.</param>
    /// <param name="Kind">The list the array was taken from.</param>
    /// <param name="Offset">The position of the array's first entry in the message's list, or of its first value in the stream's values.</param>
    /// <param name="Stream">The stream whose values the array holds, or -1 when the array holds entries.</param>
    private readonly record struct Slice(ScopeSidecar Sidecar, ScopeSidecarKind Kind, int Offset = 0, int Stream = -1)
    {
        public Slice At(int offset) => this with { Offset = Offset + offset };

        public Slice ValuesOfFirstStream() => this with { Kind = ScopeSidecarKind.StreamingData, Stream = Offset, Offset = 0 };

        public Dictionary<ScopeToken, int> Membership(int start, int count)
        {
            if (Sidecar is null)
            {
                return null;
            }

            var membership = new Dictionary<ScopeToken, int>();
            if (Stream >= 0)
            {
                Sidecar.AddValues(Stream, Offset + start, count, membership);
            }
            else
            {
                Sidecar.AddEntries(Kind, Offset + start, count, membership);
            }

            return membership;
        }
    }
}
