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
using System.Text.Json.Serialization;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object defining an override of a property of the OMF Type definition for an OMF Container.
/// </summary>
public class PropertyDefinitionOverride
{
    /// <summary>Gets or sets name of the property override.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; }

    /// <summary>Gets or sets description of the property override.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; }

    /// <summary>Gets or sets unit of Measure (UoM) of the property override.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Uom)]
    public string Uom { get; set; }

    /// <summary>Gets or sets optional minimum value for the property override.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.Minimum)]
    public double? Minimum { get; set; }

    /// <summary>Gets or sets the optional maximum value for the property override.</summary>
    [JsonPropertyOrder(4)]
    [JsonPropertyName(Tokens.Maximum)]
    public double? Maximum { get; set; }

    /// <summary>Gets or sets optional data mode used to provide consistency when reading values.</summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName(Tokens.Interpolation)]
    public Interpolation? Interpolation { get; set; }
}
