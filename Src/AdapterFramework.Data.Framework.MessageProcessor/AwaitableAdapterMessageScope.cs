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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;

namespace AdapterFramework.Data.Framework.MessageProcessor;

/// <summary>
/// An awaitable scope whose writes run through the core write methods of the <see cref="AdapterMessageProcessor"/> that created it.
/// </summary>
internal sealed class AwaitableAdapterMessageScope : IAwaitableAdapterMessageScope
{
    private readonly AdapterMessageProcessor _processor;
    private readonly OmfAwaitableScopeState _state;

    public AwaitableAdapterMessageScope(AdapterMessageProcessor processor, ScopeToken scope)
    {
        _processor = processor;
        _state = OmfAwaitableScopeState.FromToken(scope);
    }

    public OmfVersion OmfVersion => _processor.OmfVersion;

    public void Seal() => _state.Seal();

    public Task<OmfAcceptanceResult> WaitForAcceptanceAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        _state.WaitForAcceptanceAsync(timeout, cancellationToken);

    public ValueTask DisposeAsync() => _state.DisposeAsync();

    public void WriteType(DataType dataType, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteTypeCore(dataType, messageAction, _state));

    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteTypesCore(dataTypes, messageAction, _state));

    public void WriteStream(DataStream dataStream, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteStreamCore(dataStream, messageAction, _state));

    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteStreamsCore(dataStreams, messageAction, _state));

    public void WriteDynamicValue<T>(IDataSelectionConfiguration dataSelectionItem, T instance, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null)
        where T : class =>
        Write(() => _processor.WriteDynamicValueCore(dataSelectionItem, instance, messageAction, partitionKey, _state));

    public void WriteDynamicValues<T>(IDataSelectionConfiguration dataSelectionItem, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null)
        where T : class =>
        Write(() => _processor.WriteDynamicValuesCore(dataSelectionItem, instances, messageAction, partitionKey, _state));

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null,
        List<string> tags = null, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteStaticValueCore(typeId, id, name, description, dataSource, instance, metadata, tags, propertyOverrides, messageAction, _state));

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteStaticValueCore(typeId, id, name, description, dataSource, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, _state));

    public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteInstanceRelationshipCore(link, messageAction, _state));

    public void WriteTypeRelationship(Link link, MessageAction messageAction = MessageAction.Default) =>
        Write(() => _processor.WriteTypeRelationshipCore(link, messageAction, _state));

    public void WriteEvent<T>(string typeId, string id, string name, string description, DateTime startTime, DateTime? endTime, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default)
        where T : class =>
        Write(() => _processor.WriteEventCore(typeId, id, name, description, startTime, endTime, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, _state));

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
