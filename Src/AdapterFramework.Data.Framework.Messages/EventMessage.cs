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
using System.Text.Json.Serialization;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

/// <remarks>
/// Initializes a new instance of the <see cref="EventMessage"/> class.
/// </remarks>
/// <param name="typeId">The OMF Type identifier this event instance conforms to.</param>
/// <param name="id">The stable identifier for the event instance.</param>
/// <param name="name">Human friendly name for the event.</param>
/// <param name="description">Optional description of the event.</param>
/// <param name="dataSource">Origin / source system of the event data.</param>
/// <param name="startTime">Event start timestamp (UTC recommended).</param>
/// <param name="endTime">Optional event end timestamp (null for open / ongoing events).</param>
/// <param name="tags">Optional collection of tag strings that classify the event.</param>
/// <param name="properties">Extended property definitions for the underlying type (nullable if none).</param>
/// <param name="propertyOverrides">Per-property overrides (nullable if none).</param>
/// <param name="metadata">Arbitrary metadata key/value pairs associated with the event (nullable).</param>
/// <param name="instance">Event payload (object whose shape matches <paramref name="typeId"/> schema).</param>
/// <param name="relationships">Set of relationship to be sent with the event instance.</param>
/// <param name="messageAction">The <see cref="MessageAction"/> directive for downstream processing.</param>
public class EventMessage(
    string typeId,
    string id,
    string name,
    string description,
    string dataSource,
    DateTime startTime,
    DateTime? endTime,
    List<string> tags,
    IReadOnlyDictionary<string, PropertyDefinition> properties,
    IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
    IReadOnlyDictionary<string, object> metadata,
    object instance,
    IList<Link> relationships,
    MessageAction messageAction) : DataMessage(typeId, Classification.Static, instance, messageAction)
{
    /// <summary>Gets or sets the event instance identifier.</summary>
    [JsonPropertyName(Tokens.Id)]
    public string InstanceId { get; set; } = id;

    /// <summary>Gets or sets the event name.</summary>
    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; } = name;

    /// <summary>Gets or sets the event description.</summary>
    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; } = description;

    /// <summary>Gets or sets the event data source.</summary>
    [JsonPropertyName(Tokens.DataSource)]
    public string DataSource { get; set; } = dataSource;

    /// <summary>Gets or sets the event start time.</summary>
    [JsonPropertyName(Tokens.StartTime)]
    public DateTime StartTime { get; set; } = startTime;

    /// <summary>Gets or sets the event end time; null if open ended / ongoing.</summary>
    [JsonPropertyName(Tokens.EndTime)]
    public DateTime? EndTime { get; set; } = endTime;

    /// <summary>Gets or sets tags associated with the event.</summary>
    [JsonPropertyName(Tokens.Tags)]
    public IEnumerable<string> Tags { get; set; } = tags;

    /// <summary>Gets extended property definitions associated with the event type.</summary>
    [JsonPropertyName(Tokens.ExtendedPropertiesDefinition)]
    public IReadOnlyDictionary<string, PropertyDefinition> ExtendedPropertiesDefinition { get; } = properties;

    /// <summary>Gets property overrides applied to the event instance.</summary>
    [JsonPropertyName(Tokens.PropertyOverrides)]
    public IReadOnlyDictionary<string, PropertyDefinitionOverride> PropertyOverrides { get; } = propertyOverrides;

    /// <summary>Gets or sets additional metadata key/value pairs for the event.</summary>
    [JsonPropertyName(Tokens.Metadata)]
    public IReadOnlyDictionary<string, object> Metadata { get; set; } = metadata;

#pragma warning disable CA2227 // Collection properties should be read only
    [JsonIgnore]
    public IList<Link> Relationships { get; set; } = relationships;
#pragma warning restore CA2227 // Collection properties should be read only
}
