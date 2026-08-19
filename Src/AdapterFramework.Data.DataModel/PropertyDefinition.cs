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

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// OMF Type Properties Class Schema
/// https://docs.aveva.com/bundle/omf/page/1283988.html.
/// </summary>
public class PropertyDefinition
{
    /// <summary>Gets or sets optional type of the Type Property
    /// which must match a type listed in the Supported Formats table below.</summary>
    /// <remarks>Type and RefTypeId are mutually exclusive, and at least one is required.</remarks>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Type)]
    public string Type { get; set; }

    /// <summary>Gets or sets optional format of the Type Property type that, if specified, must be from the table below.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Format)]
    public string Format { get; set; }

    /// <summary>This property is only valid on arrays. A required object used to define the type of objects contained by the property.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Items)]
    public PropertyDefinition Items { get; set; }

    /// <summary>Gets or sets ID to a previously defined Type. Either type or reftypeid is required for each property.</summary>
    /// <remarks>Type and RefTypeId are mutually exclusive, and at least one is required.</remarks>
    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.RefTypeId)]
    public string RefTypeId { get; set; }

    /// <summary>Gets or sets a value indicating whether at least one Type Property must be designated as the index by supplying the
    /// isIndex keyword with a value of true. The designated isIndex property is used to
    /// uniquely identify discrete Data objects so that they can be updated or deleted
    /// after their initial creation. For a compound index, the order of index properties
    /// within the message determines the order within the index.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName(Tokens.IsIndex)]
    public bool? IsIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether one Type Property may be optionally designated as the name by supplying the
    /// isName keyword with a value of true. Because the index must be unique across all Data
    /// objects, the isName keyword allows for multiple distinct Data objects to share a common name.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName(Tokens.IsName)]
    public bool? IsName { get; set; }

    /// <summary>Gets or sets a value indicating whether this property represents a data quality value.</summary>
    [JsonPropertyOrder(6)]
    [JsonPropertyName(Tokens.IsQuality)]
    public bool? IsQuality { get; set; }

    /// <summary>Gets or sets optional friendly name for the Type Property.</summary>
    [JsonPropertyOrder(7)]
    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; }

    /// <summary>Gets or sets optional description for the Type Property.</summary>
    [JsonPropertyOrder(8)]
    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; }

    /// <summary>Gets or sets optional unit of measure for the Type Property.</summary>
    [JsonPropertyOrder(9)]
    [JsonPropertyName(Tokens.Uom)]
    public string Uom { get; set; }

    /// <summary>Gets or sets optional minimum value for the Type Property.</summary>
    [JsonPropertyOrder(10)]
    [JsonPropertyName(Tokens.Minimum)]
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the optional maximum value for the Type Property.</summary>
    [JsonPropertyOrder(11)]
    [JsonPropertyName(Tokens.Maximum)]
    public double? Maximum { get; set; }

    /// <summary>Gets or sets optional data mode used to provide consistency when reading values.</summary>
    [JsonPropertyOrder(12)]
    [JsonPropertyName(Tokens.Interpolation)]
    public Interpolation? Interpolation { get; set; }

    [JsonPropertyOrder(13)]
    [JsonPropertyName(Tokens.QualitySchema)]
    public string QualitySchema { get; set; }

#pragma warning disable CA2227 // Collection properties should be read only
    /// <summary>Gets or sets optional key-value pairs associated with the Property.</summary>
    [JsonPropertyOrder(14)]
    [JsonPropertyName(Tokens.Metadata)]
    public Dictionary<string, object> Metadata { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only

    /// <summary>Gets or sets additional Properties => a dictionary of objects, indexed by a string key.
    /// The additionalProperties keyword defines the dictionary's value type.
    /// This is required when <see cref="Format"/> is set to dictionary.</summary>
    [JsonPropertyOrder(15)]
    [JsonPropertyName(Tokens.AdditionalProperties)]
    public PropertyDefinition AdditionalPropertiesDefinition { get; set; }
}
