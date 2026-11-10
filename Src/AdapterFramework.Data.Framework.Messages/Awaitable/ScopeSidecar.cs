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
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// Records which scope wrote each item of a grouped message. The sidecar is never serialized; the serialization block uses it
/// to count the items each body carries per scope.
/// </summary>
/// <remarks>
/// An <em>entry</em> is one position in one of the message's lists: a type, container, relationship, entity, event, or stream.
/// Each entry other than a stream maps to a scope index, where -1 means unscoped. A stream's values are described by
/// <see cref="ScopeRange"/> runs; values outside every run are unscoped. A <c>null</c> list means no item of that kind is scoped.
/// </remarks>
public abstract class ScopeSidecar
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeSidecar"/> class.
    /// </summary>
    /// <param name="scopeTable">The scope table that indexes refer to, shared by every body cut from the grouped message.</param>
    private protected ScopeSidecar(IReadOnlyList<ScopeToken> scopeTable)
    {
        ArgumentNullException.ThrowIfNull(scopeTable);
        ScopeTable = scopeTable;
    }

    /// <summary>Gets the scope table that indexes refer to.</summary>
    public IReadOnlyList<ScopeToken> ScopeTable { get; }

    /// <summary>
    /// Adds the scoped items of a run of entries to <paramref name="membership"/>.
    /// For <see cref="ScopeSidecarKind.StreamingData"/>, every scoped value of each stream counts.
    /// </summary>
    /// <param name="kind">The list.</param>
    /// <param name="start">The position of the first entry in the message's list.</param>
    /// <param name="count">The number of entries.</param>
    /// <param name="membership">The per-scope item counts to add to.</param>
    public void AddEntries(ScopeSidecarKind kind, int start, int count, IDictionary<ScopeToken, int> membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        if (kind == ScopeSidecarKind.StreamingData)
        {
            for (var stream = start; stream < start + count; stream++)
            {
                var ranges = GetStreamRanges(stream);
                if (ranges is null)
                {
                    continue;
                }

                foreach (var range in ranges)
                {
                    Add(membership, ScopeTable[range.ScopeIndex], range.Count);
                }
            }

            return;
        }

        var scopes = GetEntryScopes(kind);
        if (scopes is null)
        {
            return;
        }

        var end = Math.Min(start + count, scopes.Count);
        for (var index = start; index < end; index++)
        {
            if (scopes[index] >= 0)
            {
                Add(membership, ScopeTable[scopes[index]], 1);
            }
        }
    }

    /// <summary>
    /// Adds the scoped values in a window of one stream's values to <paramref name="membership"/>.
    /// </summary>
    /// <param name="stream">The position of the stream in the message's streaming data.</param>
    /// <param name="start">The position of the first value of the window in the stream's original value list.</param>
    /// <param name="count">The number of values in the window.</param>
    /// <param name="membership">The per-scope item counts to add to.</param>
    public void AddValues(int stream, int start, int count, IDictionary<ScopeToken, int> membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        var ranges = GetStreamRanges(stream);
        if (ranges is null)
        {
            return;
        }

        var end = (long)start + count;
        foreach (var range in ranges)
        {
            var clippedStart = Math.Max(range.Start, start);
            var clippedEnd = Math.Min((long)range.Start + range.Count, end);
            if (clippedEnd > clippedStart)
            {
                Add(membership, ScopeTable[range.ScopeIndex], (int)(clippedEnd - clippedStart));
            }
        }
    }

    /// <summary>
    /// Gets the scope index per entry of a list other than streaming data.
    /// </summary>
    /// <param name="kind">The list.</param>
    /// <returns>The scope indexes, or <c>null</c> when no entry of that list is scoped or the message has no such list.</returns>
    private protected abstract IReadOnlyList<int> GetEntryScopes(ScopeSidecarKind kind);

    /// <summary>
    /// Gets the scoped value runs of one stream.
    /// </summary>
    /// <param name="stream">The position of the stream in the message's streaming data.</param>
    /// <returns>The runs, or <c>null</c> when the stream has no scoped values or the message has no streaming data.</returns>
    private protected virtual IReadOnlyList<ScopeRange> GetStreamRanges(int stream) => null;

    private static void Add(IDictionary<ScopeToken, int> membership, ScopeToken scope, int count)
    {
        membership.TryGetValue(scope, out var current);
        membership[scope] = current + count;
    }
}
