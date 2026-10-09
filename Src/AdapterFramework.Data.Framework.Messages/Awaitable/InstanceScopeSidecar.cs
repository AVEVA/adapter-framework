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
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// Records which scope wrote each item of a grouped <see cref="InstanceMessage"/>.
/// </summary>
public sealed class InstanceScopeSidecar : ScopeSidecar
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceScopeSidecar"/> class.
    /// </summary>
    /// <param name="scopeTable">The scope table that indexes refer to.</param>
    /// <param name="streamRanges">The scoped value runs, parallel to <see cref="InstanceMessage.StreamingData"/>.</param>
    /// <param name="entityScopes">The scope index per entity, parallel to <see cref="InstanceMessage.Entities"/>.</param>
    /// <param name="eventScopes">The scope index per event, parallel to <see cref="InstanceMessage.Events"/>.</param>
    /// <param name="relationshipScopes">The scope index per relationship, parallel to <see cref="InstanceMessage.Relationships"/>.</param>
    public InstanceScopeSidecar(
        IReadOnlyList<ScopeToken> scopeTable,
        IReadOnlyList<IReadOnlyList<ScopeRange>> streamRanges,
        IReadOnlyList<int> entityScopes,
        IReadOnlyList<int> eventScopes,
        IReadOnlyList<int> relationshipScopes)
        : base(scopeTable)
    {
        StreamRanges = streamRanges;
        EntityScopes = entityScopes;
        EventScopes = eventScopes;
        RelationshipScopes = relationshipScopes;
    }

    /// <summary>Gets the scoped value runs per stream, in absolute value positions.</summary>
    public IReadOnlyList<IReadOnlyList<ScopeRange>> StreamRanges { get; }

    /// <summary>Gets the scope index per entity.</summary>
    public IReadOnlyList<int> EntityScopes { get; }

    /// <summary>Gets the scope index per event.</summary>
    public IReadOnlyList<int> EventScopes { get; }

    /// <summary>Gets the scope index per relationship.</summary>
    public IReadOnlyList<int> RelationshipScopes { get; }

    /// <inheritdoc/>
    private protected override IReadOnlyList<int> GetEntryScopes(ScopeSidecarKind kind) => kind switch
    {
        ScopeSidecarKind.Entities => EntityScopes,
        ScopeSidecarKind.Events => EventScopes,
        ScopeSidecarKind.Relationships => RelationshipScopes,
        _ => null,
    };

    /// <inheritdoc/>
    private protected override IReadOnlyList<ScopeRange> GetStreamRanges(int stream) =>
        StreamRanges is not null && stream >= 0 && stream < StreamRanges.Count ? StreamRanges[stream] : null;
}
