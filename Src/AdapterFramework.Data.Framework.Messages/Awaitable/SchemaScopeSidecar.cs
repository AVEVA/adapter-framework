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
/// Records which scope wrote each item of a grouped <see cref="SchemaMessage"/>.
/// </summary>
public sealed class SchemaScopeSidecar : ScopeSidecar
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaScopeSidecar"/> class.
    /// </summary>
    /// <param name="scopeTable">The scope table that indexes refer to.</param>
    /// <param name="typeScopes">The scope index per type, parallel to <see cref="SchemaMessage.Types"/>.</param>
    /// <param name="containerScopes">The scope index per container, parallel to <see cref="SchemaMessage.Containers"/>.</param>
    /// <param name="relationshipScopes">The scope index per relationship, parallel to <see cref="SchemaMessage.Relationships"/>.</param>
    public SchemaScopeSidecar(
        IReadOnlyList<ScopeToken> scopeTable,
        IReadOnlyList<int> typeScopes,
        IReadOnlyList<int> containerScopes,
        IReadOnlyList<int> relationshipScopes)
        : base(scopeTable)
    {
        TypeScopes = typeScopes;
        ContainerScopes = containerScopes;
        RelationshipScopes = relationshipScopes;
    }

    /// <summary>Gets the scope index per type.</summary>
    public IReadOnlyList<int> TypeScopes { get; }

    /// <summary>Gets the scope index per container.</summary>
    public IReadOnlyList<int> ContainerScopes { get; }

    /// <summary>Gets the scope index per relationship.</summary>
    public IReadOnlyList<int> RelationshipScopes { get; }

    /// <inheritdoc/>
    private protected override IReadOnlyList<int> GetEntryScopes(ScopeSidecarKind kind) => kind switch
    {
        ScopeSidecarKind.Types => TypeScopes,
        ScopeSidecarKind.Containers => ContainerScopes,
        ScopeSidecarKind.Relationships => RelationshipScopes,
        _ => null,
    };
}
