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
using System.Text.Json.Serialization;
using AdapterFramework.Data.DataModel.Converters;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object defining a static OMF data message schema.
/// </summary>
public class StaticStreamData : StreamData
{
    /// <inheritdoc/>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.TypeId)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public override string Id { get; set; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Id)]
    public string InstanceId { get; set; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; }

    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.DataSource)]
    public string DataSource { get; set; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; }

    [JsonPropertyOrder(5)]
    [JsonPropertyName(Tokens.Tags)]
    public IEnumerable<string> Tags { get; set; }

    /// <summary>Extended property definitions for the static data instance.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName(Tokens.Properties)]
    public IReadOnlyDictionary<string, PropertyDefinition> Properties { get; set; }

    /// <summary>Gets or sets optional key-value pairs defining overrides to properties of the Type.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName(Tokens.PropertyOverrides)]
    public IReadOnlyDictionary<string, PropertyDefinitionOverride> PropertyOverrides { get; set; }

    /// <summary>Gets or sets value associated with the static data instance.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName(Tokens.Value)]
    public object Value { get; set; }

    /// <summary>Gets or sets optional key-value pairs associated with the static data instance.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName(Tokens.Metadata)]
    public IReadOnlyDictionary<string, object> Metadata { get; set; }
}
