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
using AdapterFramework.Data.DataModel.Converters;

namespace AdapterFramework.Data.DataModel;

public class Event
{
    /// <inheritdoc/>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.TypeId)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public string TypeId { get; set; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Id)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public string Id { get; set; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName(Tokens.StartTime)]
    public DateTime StartTime { get; set; }

    [JsonPropertyOrder(5)]
    [JsonPropertyName(Tokens.EndTime)]
    public DateTime? EndTime { get; set; }

    [JsonPropertyOrder(6)]
    [JsonPropertyName(Tokens.DataSource)]
    public string DataSource { get; set; }

    [JsonPropertyOrder(7)]
    [JsonPropertyName(Tokens.Tags)]
    public IEnumerable<string> Tags { get; set; }

    /// <summary>Extended property definitions for the event instance.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName(Tokens.Properties)]
    public IReadOnlyDictionary<string, PropertyDefinition> Properties { get; set; }

    /// <summary>Gets or sets optional key-value pairs defining overrides to properties of the Type.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName(Tokens.PropertyOverrides)]
    public IReadOnlyDictionary<string, PropertyDefinitionOverride> PropertyOverrides { get; set; }

    /// <summary>Gets or sets value associated with the event instance.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName(Tokens.Value)]
    [JsonConverter(typeof(RequiredPropertyConverter<object>))]
    public object Value { get; set; }

    /// <summary>Gets or sets optional key-value pairs associated with the event instance.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName(Tokens.Metadata)]
    public IReadOnlyDictionary<string, object> Metadata { get; set; }
}
