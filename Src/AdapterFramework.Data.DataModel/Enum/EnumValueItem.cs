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
using AdapterFramework.Data.DataModel.Converters;

namespace AdapterFramework.Data.DataModel.Enum;

/// <summary>
/// An object representing an OMF Type's Enum's Values's value based on OMF Spec 1.2.
/// https://docs.aveva.com/bundle/omf/page/1283989.html#_enum_values_collection_keywords.
/// </summary>
public class EnumValueItem
{
    /// <summary>
    /// Creates an instance of type EnumValueItem. Required for JSON deserialization of reference types.
    /// </summary>
    public EnumValueItem()
    {
    }

    /// <summary>
    /// Creates an instance of type EnumValueItem.
    /// </summary>
    /// <param name="name">The name of the enum field.</param>
    /// <param name="value">The value of the enum field.</param>
    public EnumValueItem(string name, object value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>Gets or sets the Name of the enum field.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Name)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public string Name { get; set; }

    /// <summary>Gets or sets the Value of the enum field.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Value)]
    public object Value { get; set; }
}
