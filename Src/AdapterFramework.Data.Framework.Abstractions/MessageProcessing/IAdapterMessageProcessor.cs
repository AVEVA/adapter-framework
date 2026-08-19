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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing;

public interface IAdapterMessageProcessor
{
    /// <summary>
    /// Gets the target OMF protocol version.
    /// </summary>
    OmfVersion OmfVersion { get; }

    /// <summary>
    /// Writes a single instance of <see cref="DataType"/> message.
    /// </summary>
    /// <param name="dataType">The type message instance.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteType(DataType dataType, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes an array of <see cref="DataType"/> type messages.
    /// </summary>
    /// <param name="dataTypes">An array of <see cref="DataType"/> instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteTypes(DataType[] dataTypes, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes a single instance of <see cref="DataStream"/> message.
    /// </summary>
    /// <param name="dataStream">The <see cref="DataStream"/> instance.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteStream(DataStream dataStream, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes an array of <see cref="DataStream"/> messages.
    /// </summary>
    /// <param name="dataStreams">An array of <see cref="DataStream"/> instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteStreams(DataStream[] dataStreams, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes a single instance of dynamic value.
    /// </summary>
    /// <param name="dataSelectionItem">The DataSelectionItem passed into the Adapter code by the ProcessSelectionUpdateAsync() method.</param>
    /// <param name="instance">The actual instance of dynamic value.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    void WriteDynamicValue<T>(IDataSelectionConfiguration dataSelectionItem, T instance, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null) where T : class;

    /// <summary>
    /// Writes multiple instances of dynamic values.
    /// </summary>
    /// <param name="dataSelectionItem">The DataSelectionItem passed into the Adapter code by the ProcessSelectionUpdateAsync() method.</param>
    /// <param name="instances">The <see cref="IReadOnlyList{T}"/> collection of dynamic value instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    void WriteDynamicValues<T>(IDataSelectionConfiguration dataSelectionItem, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null) where T : class;

    /// <summary>
    /// Writes a single instance of static value with fields that support OMF 2.0+.
    /// </summary>
    /// <param name="typeId">Type ID of the static data value.</param>
    /// <param name="id">The ID of the static data value.</param>
    /// <param name="name">Name of the static data value.</param>
    /// <param name="description">Description of the static data value.</param>
    /// <param name="dataSource">Data source of the static data value.</param>
    /// <param name="instance">The actual instance of the static data value.</param>
    /// <param name="metadata">Optional metadata of the static data message.</param>
    /// <param name="tags">Optional collection of tags associated with the value.</param>
    /// <param name="propertyOverrides">Optional property overrides for the static data message.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <remarks>Intended for writing OMF 2.0+ messages.</remarks>
    void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null,
        List<string> tags = null, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null,
        MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes a single instance of static value with optional extended properties that support OMF 2.0+.
    /// </summary>
    /// <param name="typeId">Type ID of the static data value.</param>
    /// <param name="id">The ID of the static data value.</param>
    /// <param name="name">Name of the static data value.</param>
    /// <param name="description">Description of the static data value.</param>
    /// <param name="dataSource">Data source of the static data value.</param>
    /// <param name="extendedPropertyDefinitions">Property definitions of extended properties for the static data message.</param>
    /// <param name="propertyOverrides">Property overrides for the static data message.</param>
    /// <param name="instance">The actual instance of the static data value.</param>
    /// <param name="metadata">Optional metadata of the static data value.</param>
    /// <param name="tags">Optional collection of tags associated with the value.</param>
    /// <param name="relationships">Optional collection of relationships associated with the value.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <remarks>Intended for writing OMF 2.0+ messages.</remarks>
    void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes a relationship between two instance-level entities (e.g., linking stream or static data instances) represented by a <see cref="Link"/>.
    /// </summary>
    /// <param name="link">The <see cref="Link"/> object describing the source and target of the instance relationship.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> directive for downstream processing.</param>
    void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes a relationship between two type (schema) entities represented by a <see cref="Link"/>.
    /// </summary>
    /// <param name="link">The <see cref="Link"/> object describing the source and target of the type relationship.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> directive for downstream processing.</param>
    void WriteTypeRelationship(Link link, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes a single event instance with temporal bounds and optional extended properties, overrides, metadata, and tags.
    /// </summary>
    /// <typeparam name="T">The type of the event payload instance.</typeparam>
    /// <param name="typeId">The OMF Type identifier that the event instance conforms to.</param>
    /// <param name="id">Stable identifier for the event instance.</param>
    /// <param name="name">Human-friendly name for the event.</param>
    /// <param name="description">Optional description for the event.</param>
    /// <param name="startTime">Start timestamp of the event.</param>
    /// <param name="endTime">Optional end timestamp (null for an ongoing/open event).</param>
    /// <param name="extendedPropertyDefinitions">Extended property definitions applicable to the event type.</param>
    /// <param name="propertyOverrides">Overrides applied to extended properties for this event instance.</param>
    /// <param name="instance">The event payload whose structure matches the specified type.</param>
    /// <param name="metadata">Optional metadata key/value pairs associated with the event.</param>
    /// <param name="tags">Optional collection of tags classifying the event.</param>
    /// <param name="relationships">Optional collection of relationships associated with the event.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> directive for downstream processing.</param>
    void WriteEvent<T>(string typeId, string id, string name, string description, DateTime startTime, DateTime? endTime, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default) where T : class;
}
