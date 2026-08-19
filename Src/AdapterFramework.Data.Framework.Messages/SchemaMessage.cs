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
using DataType = AdapterFramework.Data.DataModel.DataType;

namespace AdapterFramework.Data.Framework.Messages;

/// <summary>
/// Represents an OMF schema message containing type, container, and relationship definitions.
/// </summary>
public class SchemaMessage : Message, IDisposable
{
    private readonly bool _rentedFromPool;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaMessage"/> class.
    /// </summary>
    /// <param name="types">An array of OMF Type definitions.</param>
    /// <param name="containers">An array of OMF Container definitions.</param>
    /// <param name="relationships">An array of OMF Relationship (Link) definitions.</param>
    /// <param name="typeCount">The number of valid elements in <paramref name="types"/>.</param>
    /// <param name="containerCount">The number of valid elements in <paramref name="containers"/>.</param>
    /// <param name="relationshipCount">The number of valid elements in <paramref name="relationships"/>.</param>
    /// <param name="messageAction">The <see cref="Abstractions.Messages.MessageAction"/> that describes the intent for these schema objects.</param>
    /// <param name="rentedFromPool">True if the arrays were rented from an <see cref="ArrayPool{T}"/> and should be returned on dispose; otherwise false.</param>
    /// <remarks>
    /// The counts allow callers to reuse larger pooled arrays while only sending the active subset.
    /// When <paramref name="rentedFromPool"/> is true, the arrays will be returned to their respective pools on <see cref="Dispose"/>.
    /// </remarks>
    public SchemaMessage(
        DataType[] types,
        DataStream[] containers,
        Link[] relationships,
        int typeCount,
        int containerCount,
        int relationshipCount,
        MessageAction messageAction,
        bool rentedFromPool = false)
    {
        Types = types;
        Containers = containers;
        Relationships = relationships;
        TypeCount = typeCount;
        ContainerCount = containerCount;
        RelationshipCount = relationshipCount;
        MessageAction = messageAction;
        _rentedFromPool = rentedFromPool;
    }

    /// <summary>Gets the array of OMF Type definitions.</summary>
    public DataType[] Types { get; }

    /// <summary>Gets the array of OMF Container definitions.</summary>
    public DataStream[] Containers { get; }

    /// <summary>Gets the array of OMF Relationship (Link) definitions.</summary>
    public Link[] Relationships { get; }

    /// <summary>Gets the number of type elements considered part of this message.</summary>
    public int TypeCount { get; }

    /// <summary>Gets the number of container elements considered part of this message.</summary>
    public int ContainerCount { get; }

    /// <summary>Gets the number of relationship elements considered part of this message.</summary>
    public int RelationshipCount { get; }

    /// <summary>Gets the <see cref="Abstractions.Messages.MessageAction"/> describing the schema operation.</summary>
    public MessageAction MessageAction { get; }

    /// <summary>
    /// Releases resources used by the <see cref="SchemaMessage"/> and returns rented arrays to their pools when applicable.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Performs the dispose pattern.
    /// </summary>
    /// <param name="disposing">True to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_rentedFromPool && Types != null)
                {
                    ArrayPool<DataType>.Shared.Return(Types, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<DataType>());
                }

                if (_rentedFromPool && Containers != null)
                {
                    ArrayPool<DataStream>.Shared.Return(Containers, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<DataStream>());
                }

                if (_rentedFromPool && Relationships != null)
                {
                    ArrayPool<Link>.Shared.Return(Relationships, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<Link>());
                }
            }

            _disposed = true;
        }
    }
}
