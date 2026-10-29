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
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

/// <summary>
/// The <see cref="IMessageProcessor"/> writes with a required scope, so layers of the processor chain can't drop the scope.
/// </summary>
/// <remarks>
/// Framework infrastructure; not intended for adapter code. It doesn't inherit <see cref="IMessageProcessor"/>, so a layer can call
/// downstream only through methods that take a scope. A null scope means an unscoped write.
/// </remarks>
public interface IScopedMessageProcessor
{
    /// <summary>
    /// Creates the state for a new awaitable scope.
    /// </summary>
    /// <param name="options">The scope options.</param>
    /// <param name="scope">The scope token, or null when the active-scope limit is reached.</param>
    /// <returns><c>true</c> if the scope was created; <c>false</c> when the active-scope limit is reached.</returns>
    /// <exception cref="NotSupportedException">The configuration or a layer of the processor chain doesn't support scopes.</exception>
    bool TryCreateScope(OmfAwaitableScopeOptions options, out ScopeToken scope);

    void WriteType(DataType dataType, MessageAction messageAction, ScopeToken scope);

    void WriteTypes(DataType[] dataTypes, MessageAction messageAction, ScopeToken scope);

    void WriteStream(DataStream dataStream, MessageAction messageAction, ScopeToken scope);

    void WriteStreams(DataStream[] dataStreams, MessageAction messageAction, ScopeToken scope);

    void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction, ScopeToken scope)
        where T : class;

    void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
        where T : class;

    void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction, ScopeToken scope)
        where T : class;

    void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
        where T : class;

    void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction, ScopeToken scope)
        where T : class;

    void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships,
        MessageAction messageAction, ScopeToken scope)
        where T : class;

    void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, MessageAction messageAction, ScopeToken scope)
        where T : class;

    void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
        IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships, MessageAction messageAction, ScopeToken scope)
        where T : class;

    void WriteSchemaRelationship(Link link, MessageAction messageAction, ScopeToken scope);

    void WriteInstanceRelationship(Link link, MessageAction messageAction, ScopeToken scope);
}
