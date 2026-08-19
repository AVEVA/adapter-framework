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
using AdapterFramework.Data.DataModel.Enum;

namespace AdapterFramework.Data.DataModel;

#pragma warning disable CA2227 // Collection properties should be read only
/// <summary>
/// An object representing an OMF Type Message based on OMF Spec 1.2, 1.3 and 2.0.
/// https://docs.aveva.com/bundle/omf/page/1283986.html.
/// </summary>
public abstract class DataType
{
    /// <summary>
    /// Constructor.
    /// </summary>
    protected DataType()
    {
    }

    /// <summary>Gets or sets unique identifier of the OMF Type.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Id)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public string Id { get; set; }

    /// <summary>Gets or sets optional version of the Type. If omitted version 1.0.0.0 is assumed.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Version)]
    public string Version { get; set; }

    /// <summary>Gets OMF Type classification.</summary>
    /// <value>Either static, dynamic, streamingdata, event, entity, or null.</value>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Classification)]
    public virtual string Classification { get; }

    /// <summary>Gets Type which is inherited from JSON Schema.</summary>
    /// <value>Must be an object.</value>
    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.Type)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public string Type { get; set; }

    /// <summary>Gets or sets optional base type ID for the Type.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName(Tokens.BaseTypeId)]
    public string BaseTypeId { get; set; }

    /// <summary>Gets or sets optional friendly name for the Type.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; }

    /// <summary>Gets or sets optional description for the Type.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; }

    /// <summary>Gets or sets optional array of strings to tag the Type.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName(Tokens.Tags)]
    public IEnumerable<string> Tags { get; set; }

    /// <summary>Gets or sets optional key-value pairs associated with the Type.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName(Tokens.Metadata)]
    public IDictionary<string, object> Metadata { get; set; }

    /// <summary>
    /// Optional array of name/value pairs used to define an allowed set of values.
    /// </summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName(Tokens.Enum)]
    public TypeEnumField Enum { get; set; }

    /// <summary>Gets or sets optional data mode used to provide consistency when reading values.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName(Tokens.Extrapolation)]
    public Extrapolation? Extrapolation { get; set; }

    /// <summary>Gets or sets key-value pairs defining the properties of the Type.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName(Tokens.Properties)]
    [JsonConverter(typeof(RequiredPropertyConverter<IDictionary<string, PropertyDefinition>>))]
    public IDictionary<string, PropertyDefinition> Properties { get; set; }

    [JsonIgnore]
    public IList<Link> Relationships { get; set; }

    /// <summary>Creates and returns a shallow copy of the current <see cref="DataType"/> instance.</summary>    
    public DataType Clone() => (DataType)MemberwiseClone();
}

#pragma warning restore CA2227 // Collection properties should be read only
