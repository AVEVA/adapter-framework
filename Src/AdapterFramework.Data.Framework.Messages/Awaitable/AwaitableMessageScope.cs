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
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// An awaitable scope whose writes go through an <see cref="IScopedMessageProcessor"/> with the scope's token.
/// </summary>
public sealed class AwaitableMessageScope : IAwaitableMessageScope
{
    private readonly IScopedMessageProcessor _processor;
    private readonly OmfAwaitableScopeState _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="AwaitableMessageScope"/> class.
    /// </summary>
    /// <param name="processor">The processor that receives the scope's writes.</param>
    /// <param name="scope">The scope token that <paramref name="processor"/> created.</param>
    public AwaitableMessageScope(IScopedMessageProcessor processor, ScopeToken scope)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
        _state = OmfAwaitableScopeState.FromToken(scope);
    }

    /// <inheritdoc/>
    public void Seal() => _state.Seal();

    /// <inheritdoc/>
    public Task<OmfAcceptanceResult> WaitForAcceptanceAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _state.WaitForAcceptanceAsync(timeout, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _state.DisposeAsync();

    /// <inheritdoc/>
    public void WriteType(DataType dataType, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteType(dataType, messageAction, _state));

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteTypes(dataTypes, messageAction, _state));

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteStream(dataStream, messageAction, _state));

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteStreams(dataStreams, messageAction, _state));

    /// <inheritdoc/>
    public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteValue(id, classification, instance, messageAction, _state));

    /// <inheritdoc/>
    public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null)
        where T : class =>
        Write(() => _processor.WriteDynamicValue(id, instance, messageAction, partitionKey, _state));

    /// <inheritdoc/>
    public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteValues(id, classification, instances, messageAction, _state));

    /// <inheritdoc/>
    public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null)
        where T : class =>
        Write(() => _processor.WriteDynamicValues(id, instances, messageAction, partitionKey, _state));

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata = null, MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteStaticValue(id, extendedPropertyDefinitions, propertyOverrides, instance, metadata, messageAction, _state));

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteStaticValue(typeId, id, name, description, dataSource, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, _state));

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteStaticValue(typeId, id, name, description, dataSource, instance, metadata, tags, propertyOverrides, messageAction, _state));

    /// <inheritdoc/>
    public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
        IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteEvent(id, typeId, name, description, dataSource, startTime, endTime, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, _state));

    /// <inheritdoc/>
    public void WriteSchemaRelationship(Link link, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteSchemaRelationship(link, messageAction, _state));

    /// <inheritdoc/>
    public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteInstanceRelationship(link, messageAction, _state));

    private void Write(Action write)
    {
        _state.EnterWrite();
        try
        {
            write();
        }
        finally
        {
            _state.ExitWrite();
        }
    }
}
