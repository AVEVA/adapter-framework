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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Collects the scope of each item a grouping block buffers, and builds a <see cref="ScopeSidecar"/> for each grouped message at flush.
/// A grouping block creates a builder on the first scoped item and drops it after each flush.
/// </summary>
internal sealed class ScopeSidecarBuilder
{
    private const int EntryListCount = (int)ScopeSidecarKind.StreamingData;

    private readonly List<ScopeToken> _scopes = [];
    private readonly Dictionary<ScopeToken, int> _indexes = [];
    private readonly Dictionary<(PartitionKey? PartitionKey, string Id), List<ScopeRange>> _streamRanges = [];
    private readonly List<int>[] _entryScopes = new List<int>[EntryListCount];
    private ScopeToken[] _scopeTable;

    /// <summary>
    /// Returns whether the block buffers at least one item of <paramref name="scope"/>.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <returns><c>true</c> if an item of the scope is buffered; otherwise <c>false</c>.</returns>
    public bool Contains(ScopeToken scope) => _indexes.ContainsKey(scope);

    /// <summary>
    /// Records that a run of values appended to a stream's value list belongs to a scope.
    /// </summary>
    /// <param name="partitionKey">The partition key of the stream's message.</param>
    /// <param name="id">The stream ID.</param>
    /// <param name="scope">The scope, or <c>null</c> for unscoped values.</param>
    /// <param name="start">The position of the first value in the stream's value list.</param>
    /// <param name="count">The number of values.</param>
    public void AddValues(PartitionKey? partitionKey, string id, ScopeToken scope, int start, int count)
    {
        if (scope is null || count <= 0)
        {
            return;
        }

        var index = IndexOf(scope);
        ref var ranges = ref CollectionsMarshal.GetValueRefOrAddDefault(_streamRanges, (partitionKey, id), out _);
        ranges ??= [];
        if (ranges.Count > 0)
        {
            var last = ranges[^1];
            if (last.ScopeIndex == index && last.Start + last.Count == start)
            {
                ranges[^1] = last with { Count = last.Count + count };
                return;
            }
        }

        ranges.Add(new ScopeRange(index, start, count));
    }

    /// <summary>
    /// Records the scope of an entry appended to one of the block's lists other than streaming data.
    /// Once a list holds a scoped entry, every later entry of that list must be recorded.
    /// </summary>
    /// <param name="kind">The list.</param>
    /// <param name="position">The position of the entry in the list.</param>
    /// <param name="scope">The scope, or <c>null</c> for an unscoped entry.</param>
    public void AddEntry(ScopeSidecarKind kind, int position, ScopeToken scope)
    {
        ref var list = ref _entryScopes[(int)kind];
        if (list is null)
        {
            if (scope is null)
            {
                return;
            }

            list = new List<int>(position + 1);
            for (var i = 0; i < position; i++)
            {
                list.Add(-1);
            }
        }

        list.Add(scope is null ? -1 : IndexOf(scope));
    }

    /// <summary>
    /// Records the scope of a run of entries appended to one of the block's lists other than streaming data.
    /// </summary>
    /// <param name="kind">The list.</param>
    /// <param name="position">The position of the first entry in the list.</param>
    /// <param name="count">The number of entries.</param>
    /// <param name="scope">The scope, or <c>null</c> for unscoped entries.</param>
    public void AddEntries(ScopeSidecarKind kind, int position, int count, ScopeToken scope)
    {
        for (var i = 0; i < count; i++)
        {
            AddEntry(kind, position + i, scope);
        }
    }

    /// <summary>
    /// Builds the sidecar for one grouped instance message. Entities, events, and relationships travel only in the message without a
    /// partition key.
    /// </summary>
    /// <param name="partitionKey">The partition key of the message, or <c>null</c> for the message that also carries entities, events, and relationships.</param>
    /// <param name="streamIds">The stream IDs in the order of the message's streaming data.</param>
    /// <returns>The sidecar, or <c>null</c> when no item of the message belongs to a scope.</returns>
    public InstanceScopeSidecar BuildInstance(PartitionKey? partitionKey, ICollection<string> streamIds)
    {
        ScopeRange[][] streamRanges = null;
        if (streamIds.Count > 0 && _streamRanges.Count > 0)
        {
            var stream = 0;
            foreach (var id in streamIds)
            {
                if (_streamRanges.TryGetValue((partitionKey, id), out var ranges))
                {
                    streamRanges ??= new ScopeRange[streamIds.Count][];
                    streamRanges[stream] = [.. ranges];
                }

                stream++;
            }
        }

        var includeEntries = partitionKey is null;
        var entities = includeEntries ? Snapshot(ScopeSidecarKind.Entities) : null;
        var events = includeEntries ? Snapshot(ScopeSidecarKind.Events) : null;
        var relationships = includeEntries ? Snapshot(ScopeSidecarKind.Relationships) : null;
        if (streamRanges is null && entities is null && events is null && relationships is null)
        {
            return null;
        }

        return new InstanceScopeSidecar(GetScopeTable(), streamRanges, entities, events, relationships);
    }

    /// <summary>
    /// Builds the sidecar for one grouped schema message.
    /// </summary>
    /// <returns>The sidecar, or <c>null</c> when no item of the message belongs to a scope.</returns>
    public SchemaScopeSidecar BuildSchema()
    {
        var types = Snapshot(ScopeSidecarKind.Types);
        var containers = Snapshot(ScopeSidecarKind.Containers);
        var relationships = Snapshot(ScopeSidecarKind.Relationships);
        if (types is null && containers is null && relationships is null)
        {
            return null;
        }

        return new SchemaScopeSidecar(GetScopeTable(), types, containers, relationships);
    }

    private int[] Snapshot(ScopeSidecarKind kind) => _entryScopes[(int)kind]?.ToArray();

    private ScopeToken[] GetScopeTable() => _scopeTable ??= [.. _scopes];

    private int IndexOf(ScopeToken scope)
    {
        ref var index = ref CollectionsMarshal.GetValueRefOrAddDefault(_indexes, scope, out var exists);
        if (!exists)
        {
            index = _scopes.Count;
            _scopes.Add(scope);
            _scopeTable = null;
        }

        return index;
    }
}
