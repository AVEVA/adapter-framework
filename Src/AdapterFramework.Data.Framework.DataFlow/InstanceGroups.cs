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
using System.Runtime.InteropServices;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Messages;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Partitions the events, entities and relationships of an <see cref="InstanceMessage"/> into groups of related items.
/// A relationship always belongs to the same group as the event/entity its source references (by instance id); its target is not
/// considered. Items are sorted by group, so any contiguous range of groups maps to contiguous (zero-copy) segments of each
/// array and can be sent in a single message.
/// </summary>
/// <remarks>
/// The sorted arrays are rented from <see cref="ArrayPool{T}"/>; dispose the instance to return them.
/// Instances sharing an id belong to the same group. Every other instance forms a group of its own, and so does a
/// relationship whose source references no instance of the message.
/// </remarks>
internal sealed class InstanceGroups : IDisposable
{
    private readonly Event[] _events;
    private readonly StaticStreamData[] _entities;
    private readonly Link[] _links;

    // For group g, the items of the group are at [starts[g], starts[g + 1]) in the corresponding sorted array.
    private readonly int[] _eventStarts;
    private readonly int[] _entityStarts;
    private readonly int[] _linkStarts;
    private bool _disposed;

    private InstanceGroups(
        int groupCount,
        Event[] events,
        StaticStreamData[] entities,
        Link[] links,
        int[] eventStarts,
        int[] entityStarts,
        int[] linkStarts)
    {
        GroupCount = groupCount;
        _events = events;
        _entities = entities;
        _links = links;
        _eventStarts = eventStarts;
        _entityStarts = entityStarts;
        _linkStarts = linkStarts;
    }

    /// <summary>
    /// Gets the number of groups.
    /// </summary>
    public int GroupCount { get; }

    /// <summary>
    /// Groups the events, entities and relationships of <paramref name="message"/>.
    /// </summary>
    /// <param name="message">The message to group.</param>
    /// <returns>The groups. The caller must dispose them.</returns>
    public static InstanceGroups Create(InstanceMessage message)
    {
        var eventsCount = message.EventsCount;
        var entitiesCount = message.EntitiesCount;
        var linksCount = message.RelationshipCount;

        // Nodes [0, eventsCount) are events, [eventsCount, nodeCount) are entities.
        var nodeCount = eventsCount + entitiesCount;

        var groupOfNode = ArrayPool<int>.Shared.Rent(nodeCount);
        var groupOfLink = ArrayPool<int>.Shared.Rent(linksCount);

        try
        {
            var groupCount = AssignGroupIds(message, groupOfNode, groupOfLink);

            var groups = new InstanceGroups(
                groupCount,
                ArrayPool<Event>.Shared.Rent(eventsCount),
                ArrayPool<StaticStreamData>.Shared.Rent(entitiesCount),
                ArrayPool<Link>.Shared.Rent(linksCount),
                ArrayPool<int>.Shared.Rent(groupCount + 1),
                ArrayPool<int>.Shared.Rent(groupCount + 1),
                ArrayPool<int>.Shared.Rent(groupCount + 1));

            try
            {
                SortByGroup(message.Events.AsSpan(0, eventsCount), groupOfNode.AsSpan(0, eventsCount), groupCount, groups._eventStarts, groups._events);
                SortByGroup(message.Entities.AsSpan(0, entitiesCount), groupOfNode.AsSpan(eventsCount, entitiesCount), groupCount, groups._entityStarts, groups._entities);
                SortByGroup(message.Relationships.AsSpan(0, linksCount), groupOfLink.AsSpan(0, linksCount), groupCount, groups._linkStarts, groups._links);
            }
            catch
            {
                groups.Dispose();
                throw;
            }

            return groups;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(groupOfNode);
            ArrayPool<int>.Shared.Return(groupOfLink);
        }
    }

    /// <summary>
    /// Gets the number of events, entities and relationships in the groups [<paramref name="groupStart"/>, <paramref name="groupEnd"/>).
    /// </summary>
    /// <param name="groupStart">The first group.</param>
    /// <param name="groupEnd">The group after the last one.</param>
    /// <returns>The number of items.</returns>
    public int ItemCount(int groupStart, int groupEnd)
    {
        return _eventStarts[groupEnd] - _eventStarts[groupStart]
            + _entityStarts[groupEnd] - _entityStarts[groupStart]
            + _linkStarts[groupEnd] - _linkStarts[groupStart];
    }

    /// <summary>
    /// Creates the wrapper holding the items of the groups [<paramref name="groupStart"/>, <paramref name="groupEnd"/>).
    /// It references the rented arrays, so it must not be used after this instance has been disposed.
    /// </summary>
    /// <param name="groupStart">The first group.</param>
    /// <param name="groupEnd">The group after the last one.</param>
    /// <returns>The wrapper.</returns>
    public InstanceMessageWrapper CreateWrapper(int groupStart, int groupEnd)
    {
        return new InstanceMessageWrapper
        {
            Events = Segment(_events, _eventStarts, groupStart, groupEnd),
            Entities = Segment(_entities, _entityStarts, groupStart, groupEnd),
            Relationships = Segment(_links, _linkStarts, groupStart, groupEnd),
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ArrayPool<int>.Shared.Return(_eventStarts);
        ArrayPool<int>.Shared.Return(_entityStarts);
        ArrayPool<int>.Shared.Return(_linkStarts);
        ArrayPool<Event>.Shared.Return(_events, clearArray: true);
        ArrayPool<StaticStreamData>.Shared.Return(_entities, clearArray: true);
        ArrayPool<Link>.Shared.Return(_links, clearArray: true);

        _disposed = true;
    }

    /// <summary>
    /// Assigns dense group ids: <paramref name="groupOfNode"/>[node] for each event then entity (events first), and
    /// <paramref name="groupOfLink"/>[link] for each relationship, which gets the group of the instance its source references.
    /// </summary>
    /// <returns>The number of groups.</returns>
    private static int AssignGroupIds(InstanceMessage message, int[] groupOfNode, int[] groupOfLink)
    {
        var eventsCount = message.EventsCount;
        var nodeCount = eventsCount + message.EntitiesCount;
        var linksCount = message.RelationshipCount;
        var groupCount = 0;

        // Without relationships there is nothing to keep together, so ids need not be looked at.
        var groupById = linksCount > 0 ? new Dictionary<string, int>(nodeCount, StringComparer.Ordinal) : null;

        for (var i = 0; i < nodeCount; i++)
        {
            var id = i < eventsCount ? message.Events[i]?.Id : message.Entities[i - eventsCount]?.InstanceId;

            if (groupById is null || id is null)
            {
                groupOfNode[i] = groupCount++;
                continue;
            }

            // Instances sharing an id (including an event and an entity) are kept together.
            ref var group = ref CollectionsMarshal.GetValueRefOrAddDefault(groupById, id, out var exists);
            if (!exists)
            {
                group = groupCount++;
            }

            groupOfNode[i] = group;
        }

        for (var i = 0; i < linksCount; i++)
        {
            // A relationship whose source references no instance of the message is a group of its own.
            groupOfLink[i] = message.Relationships[i]?.Source?.Index is { } sourceId && groupById.TryGetValue(sourceId, out var sourceGroup)
                ? sourceGroup
                : groupCount++;
        }

        return groupCount;
    }

    /// <summary>
    /// Stable counting sort of <paramref name="source"/> by group id into <paramref name="destination"/>. On return
    /// <paramref name="starts"/>[g] is the start offset of group g in <paramref name="destination"/> and
    /// <paramref name="starts"/>[<paramref name="groupCount"/>] is the number of items.
    /// </summary>
    private static void SortByGroup<T>(ReadOnlySpan<T> source, ReadOnlySpan<int> groupOfItem, int groupCount, int[] starts, T[] destination)
    {
        Array.Clear(starts, 0, groupCount + 1);

        foreach (var group in groupOfItem)
        {
            starts[group]++;
        }

        for (var g = 1; g < groupCount; g++)
        {
            starts[g] += starts[g - 1];
        }

        starts[groupCount] = source.Length;

        for (var i = source.Length - 1; i >= 0; i--)
        {
            destination[--starts[groupOfItem[i]]] = source[i];
        }
    }

    // Empty parts must be default so they are omitted from the serialized payload.
    private static ArraySegment<T> Segment<T>(T[] array, int[] starts, int groupStart, int groupEnd)
    {
        var count = starts[groupEnd] - starts[groupStart];
        return count == 0 ? default : new ArraySegment<T>(array, starts[groupStart], count);
    }
}
