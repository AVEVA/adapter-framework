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

#pragma warning disable CA2227 // Collection properties should be read only
/// <summary>
/// An object representing an OMF Container Message OMF Spec 1.2.
/// https://docs.aveva.com/bundle/omf/page/1283991.html.
/// </summary>
public class DataStream
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataStream"/> with the default values.
    /// </summary>
    public DataStream()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DataStream"/> including the <paramref name="typeId"/>, <paramref name="streamId"/>, and <paramref name="name"/>.
    /// </summary>
    /// <param name="typeId">The type ID.</param>
    /// <param name="streamId">The stream ID.</param>
    /// <param name="name">The friendly name.</param>
    public DataStream(string typeId, string streamId, string name)
    {
        Id = streamId;
        TypeId = typeId;
        Name = name;
    }

    /// <summary>Gets or sets unique identifier of the Container.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Id)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public string Id { get; set; }

    /// <summary>Gets or sets ID of the Type used by the Container.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.TypeId)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public string TypeId { get; set; }

    /// <summary>Gets or sets optional version of the Type used by the Container. If omitted, version 1.0.0.0 is used.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.TypeVersion)]
    public string TypeVersion { get; set; }

    /// <summary>Gets or sets optional friendly name for the Container.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; }

    /// <summary>Gets or sets optional description for the Container.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; }

    /// <summary>Gets or sets optional string to specify source of the Container.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName(Tokens.DataSource)]
    public string DataSource { get; set; }

    /// <summary>Gets or sets optional array of strings to tag the Container.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName(Tokens.Tags)]
    public IEnumerable<string> Tags { get; set; }

    /// <summary>Gets or sets optional key-value pairs associated with the Container.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName(Tokens.Metadata)]
    public Dictionary<string, object> Metadata { get; set; }

    /// <summary>Gets or sets optional array of Type Property ids to be used as secondary indexes for the Container.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName(Tokens.Indexes)]
    public IEnumerable<string> Indexes { get; set; }

    /// <summary>Gets or sets optional data mode used to provide consistency when reading values.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName(Tokens.Extrapolation)]
    public Extrapolation? Extrapolation { get; set; }

    /// <summary>Gets or sets optional key-value pairs defining overrides to properties of the Type.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName(Tokens.PropertyOverrides)]
    public Dictionary<string, PropertyDefinitionOverride> PropertyOverrides { get; set; }
}
#pragma warning restore CA2227 // Collection properties should be read only

