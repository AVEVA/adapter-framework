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

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing;

/// <summary>
/// Represents a type used to process data types, streams and messages.
/// </summary>
public interface IMessageProcessor
{
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
    /// Writes a single instance of a data message.
    /// </summary>
    /// <param name="id">The ID of the data message, which refers to type ID for static data messages and stream ID for dynamic data message.</param>
    /// <param name="classification">The classification enumeration of the data messages, either Static or Dynamic.</param>
    /// <param name="instance">The actual instance of the data message.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>    
    void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes a single instance of a dynamic data message.
    /// </summary>
    /// <param name="id">The ID of the dynamic data message, which refers to stream ID.</param>    
    /// <param name="instance">The actual instance of the dynamic data message.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null) where T : class;

    /// <summary>
    /// Writes multiple instances of a data message.
    /// </summary>
    /// <param name="id">The ID of the data message, which refers to type ID for static data messages and stream ID for dynamic data message.</param>
    /// <param name="classification">The classification enumeration of the data messages, either Static or Dynamic.</param>
    /// <param name="instances">The <see cref="IReadOnlyList{T}"/> collection of data message instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>    
    void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes multiple instances of a dynamic data message.
    /// </summary>
    /// <param name="id">The ID of the dynamic data message, which refers to stream ID.</param>    
    /// <param name="instances">The <see cref="IReadOnlyList{T}"/> collection of dynamic data message instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null) where T : class;

    /// <summary>
    /// Writes a single instance of a static data message with optional extended properties.
    /// </summary>
    /// <param name="id">The ID of the data message, which refers to type ID for static data message.</param>
    /// <param name="extendedPropertyDefinitions">Property definitions of extended properties for the static data message.</param>
    /// <param name="propertyOverrides">Overrides for the property definitions.</param>
    /// <param name="instance">The actual instance of the static data message.</param>
    /// <param name="metadata">Metadata associated with the static data message.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata = null, MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes a single instance of static value with optional extended properties that support omf 1.3.
    /// </summary>
    /// <param name="typeId">Type ID of the static data value.</param>
    /// <param name="id">The ID of the static data value.</param>
    /// <param name="name">Name of the static data value.</param>
    /// <param name="description">Description of the static data value.</param>
    /// <param name="dataSource">Data Source of the static data value.</param>
    /// <param name="extendedPropertyDefinitions">Property definitions of extended properties for the static data message.</param>
    /// <param name="propertyOverrides">Overrides for the property definitions.</param>
    /// <param name="instance">The actual instance of the static data value.</param>
    /// <param name="metadata">Metadata associated with the static data value.</param>
    /// <param name="tags">Optional collection of tags associated with the value.</param>
    /// <param name="relationships">Optional collection of relationships associated with the value.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <remarks>Intended for writing OMF 1.3+ messages.</remarks>
    void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes a single instance of static value with fields that support omf 1.3.
    /// </summary>
    /// <param name="typeId">Type ID of the static data value.</param>
    /// <param name="id">The ID of the static data value.</param>
    /// <param name="name">Name of the static data value.</param>
    /// <param name="description">Description of the static data value.</param>
    /// <param name="dataSource">Data Source of the static data value.</param>
    /// <param name="instance">The actual instance of the static data value.</param>
    /// <param name="metadata">Metadata associated with the static data value.</param>
    /// <param name="tags">Optional collection of tags associated with the value.</param>
    /// <param name="propertyOverrides">Overrides for the property definitions.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <remarks>Intended for writing OMF 1.3+ messages.</remarks>
    void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes a single instance of an event.
    /// </summary>
    /// <param name="id">The ID of the event.</param>
    /// <param name="typeId">Type ID of the event.</param>
    /// <param name="name">Name of the event.</param>
    /// <param name="description">Description of the event.</param>
    /// <param name="dataSource">Data Source of the event.</param>
    /// <param name="startTime">Start time of the event.</param>
    /// <param name="endTime">End time of the event.</param>
    /// <param name="extendedPropertyDefinitions">Property definitions of extended properties for the event.</param>
    /// <param name="propertyOverrides">Overrides for the property definitions.</param>
    /// <param name="instance">The actual instance of the event.</param>
    /// <param name="metadata">Metadata associated with the event.</param>
    /// <param name="tags">Optional collection of tags associated with the event.</param>
    /// <param name="relationships">Optional collection of relationships associated with the event.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <remarks>Intended for writing OMF 1.3+ messages.</remarks>
    void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime, 
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
        IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class;

    /// <summary>
    /// Writes a schema-level relationship between two static types.
    /// </summary>
    /// <param name="link">The <see cref="Link"/> that describes the relationship between the source and target types.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteSchemaRelationship(Link link, MessageAction messageAction = MessageAction.Default);
    
    /// <summary>
    /// Writes an instance-level relationship between two static data values.
    /// </summary>
    /// <param name="link">The <see cref="Link"/> that describes the relationship between the source and target instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default);
}
