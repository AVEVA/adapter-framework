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
using System.Runtime.CompilerServices;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

/// <summary>
/// Represents a composite instance message that groups streaming (dynamic) data objects, static entity definitions,
/// and relationship (link) definitions into a single payload for serialization.
/// </summary>
/// <remarks>
/// <para>The message can optionally originate from arrays rented from <see cref="ArrayPool{T}"/>; if so, the
/// <paramref name="rentedFromPool"/> flag must be set to ensure arrays are returned during disposal.</para>
/// <para>Counts are split into two dimensions for streaming data: <see cref="StreamingDataObjectCount"/> is the number of
/// container objects present (i.e., number of <see cref="StreamingDataInstance"/> entries) while
/// <see cref="StreamingDataTotalCount"/> is the total number of individual value objects across all containers, used
/// for reporting / batching decisions.</para>
/// </remarks>
public class InstanceMessage : Message, IDisposable
{
    private readonly bool _rentedFromPool;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceMessage"/> class.
    /// </summary>
    /// <param name="streamingData">Array of streaming (dynamic) data instances for one or more containers.</param>
    /// <param name="entities">Array of static entity data instances.</param>
    /// <param name="events">Array of event instances.</param>
    /// <param name="relationships">Array of link relationship definitions between entities / streams / types.</param>
    /// <param name="streamingDataObjectCount">Number of elements in <paramref name="streamingData"/> actually used (logical length).</param>
    /// <param name="streamingDataTotalCount">Total number of individual value objects contained within the streaming data instances.</param>
    /// <param name="entitiesCount">Number of elements in <paramref name="entities"/> actually used (logical length).</param>
    /// <param name="eventsCount">Number of events in <paramref name="events"/>.</param>
    /// <param name="relationshipCount">Number of elements in <paramref name="relationships"/> actually used (logical length).</param>
    /// <param name="messageAction">Action indicating create/update/delete semantics for downstream processing.</param>    
    /// <param name="rentedFromPool">True if arrays were rented from pools and must be returned on dispose.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    public InstanceMessage(
        StreamingDataInstance[] streamingData,
        StaticStreamData[] entities,
        Event[] events,
        Link[] relationships,
        int streamingDataObjectCount,
        int streamingDataTotalCount,
        int entitiesCount,
        int eventsCount,
        int relationshipCount,
        MessageAction messageAction,        
        bool rentedFromPool = false,
        PartitionKey? partitionKey = null)
    {
        StreamingData = streamingData;
        Entities = entities;
        Events = events;
        Relationships = relationships;
        StreamingDataObjectCount = streamingDataObjectCount;
        StreamingDataTotalCount = streamingDataTotalCount;
        EntitiesCount = entitiesCount;
        EventsCount = eventsCount;
        RelationshipCount = relationshipCount;
        MessageAction = messageAction;        
        _rentedFromPool = rentedFromPool;
        PartitionKey = partitionKey;
    }

    /// <summary>Gets the array of streaming (dynamic) data instances.</summary>
    public StreamingDataInstance[] StreamingData { get; }

    /// <summary>Gets the array of static entity data instances.</summary>
    public StaticStreamData[] Entities { get; }

    /// <summary>Gets or sets the array of event objects associated with this instance message.</summary>
    public Event[] Events { get; }

    /// <summary>Gets the array of OMF Relationship (Link) definitions.</summary>
    public Link[] Relationships { get; }

    /// <summary>Gets the logical number of streaming data instance objects contained (subset length of <see cref="StreamingData"/>).</summary>
    public int StreamingDataObjectCount { get; }

    /// <summary>Gets the total number of individual value entries across all streaming data instances.</summary>
    public int StreamingDataTotalCount { get; }

    /// <summary>Gets the logical number of static entity data objects contained (subset length of <see cref="Entities"/>).</summary>
    public int EntitiesCount { get; }

    public int EventsCount { get; }

    /// <summary>Gets the logical number of relationship definitions contained (subset length of <see cref="Relationships"/>).</summary>
    public int RelationshipCount { get; }

    /// <summary>Gets the <see cref="Abstractions.Messages.MessageAction"/> describing the composite instance operation.</summary>
    public MessageAction MessageAction { get; }

    /// <summary>Gets the PartitionKey that will be sent with the message to the OMFIngress Service.</summary>
    public PartitionKey? PartitionKey { get; }

    /// <summary>
    /// Releases resources used by the <see cref="InstanceMessage"/> and returns rented arrays to their pools when applicable.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs the dispose pattern, optionally returning arrays to their pools.
    /// </summary>
    /// <param name="disposing">True to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_rentedFromPool && StreamingData != null)
                {
                    ArrayPool<StreamingDataInstance>.Shared.Return(StreamingData, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<StreamingDataInstance>());
                }

                if (_rentedFromPool && Entities != null)
                {
                    ArrayPool<StaticStreamData>.Shared.Return(Entities, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<StaticStreamData>());
                }

                if (_rentedFromPool && Relationships != null)
                {
                    ArrayPool<Link>.Shared.Return(Relationships, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Link>());
                }

                if (_rentedFromPool && Events != null)
                {
                    ArrayPool<Event>.Shared.Return(Events, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Event>());
                }
            }

            _disposed = true;
        }
    }
}
