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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// Extension methods that let a layer of the processor chain call the next layer with a scope.
/// </summary>
public static class ScopedMessageProcessor
{
    /// <summary>
    /// Gets <paramref name="processor"/> as an <see cref="IScopedMessageProcessor"/>. A processor without scope support, such as a
    /// test double, is wrapped so its unscoped methods are called and scope creation throws <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="processor">The next processor in the chain.</param>
    /// <returns>The scoped view of <paramref name="processor"/>.</returns>
    public static IScopedMessageProcessor AsScoped(this IMessageProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        return processor as IScopedMessageProcessor ?? new UnscopedMessageProcessor(processor);
    }

    private sealed class UnscopedMessageProcessor : IScopedMessageProcessor
    {
        private readonly IMessageProcessor _processor;

        public UnscopedMessageProcessor(IMessageProcessor processor)
        {
            _processor = processor;
        }

        public bool TryCreateScope(OmfAwaitableScopeOptions options, out ScopeToken scope) =>
            throw new NotSupportedException($"The message processor '{_processor.GetType().Name}' doesn't support awaitable scopes.");

        public void WriteType(DataType dataType, MessageAction messageAction, ScopeToken scope) =>
            _processor.WriteType(dataType, messageAction);

        public void WriteTypes(DataType[] dataTypes, MessageAction messageAction, ScopeToken scope) =>
            _processor.WriteTypes(dataTypes, messageAction);

        public void WriteStream(DataStream dataStream, MessageAction messageAction, ScopeToken scope) =>
            _processor.WriteStream(dataStream, messageAction);

        public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction, ScopeToken scope) =>
            _processor.WriteStreams(dataStreams, messageAction);

        public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction, ScopeToken scope)
            where T : class =>
            _processor.WriteValue(id, classification, instance, messageAction);

        public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
            where T : class =>
            _processor.WriteDynamicValue(id, instance, messageAction, partitionKey);

        public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction, ScopeToken scope)
            where T : class =>
            _processor.WriteValues(id, classification, instances, messageAction);

        public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
            where T : class =>
            _processor.WriteDynamicValues(id, instances, messageAction, partitionKey);

        public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
            T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction, ScopeToken scope)
            where T : class =>
            _processor.WriteStaticValue(id, extendedPropertyDefinitions, propertyOverrides, instance, metadata, messageAction);

        public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
            IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships,
            MessageAction messageAction, ScopeToken scope)
            where T : class =>
            _processor.WriteStaticValue(typeId, id, name, description, dataSource, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction);

        public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags,
            IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, MessageAction messageAction, ScopeToken scope)
            where T : class =>
            _processor.WriteStaticValue(typeId, id, name, description, dataSource, instance, metadata, tags, propertyOverrides, messageAction);

        public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
            IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
            IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships, MessageAction messageAction, ScopeToken scope)
            where T : class =>
            _processor.WriteEvent(id, typeId, name, description, dataSource, startTime, endTime, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction);

        public void WriteSchemaRelationship(Link link, MessageAction messageAction, ScopeToken scope) =>
            _processor.WriteSchemaRelationship(link, messageAction);

        public void WriteInstanceRelationship(Link link, MessageAction messageAction, ScopeToken scope) =>
            _processor.WriteInstanceRelationship(link, messageAction);
    }
}
