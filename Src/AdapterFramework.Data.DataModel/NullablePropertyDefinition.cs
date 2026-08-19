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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using AdapterFramework.Data.DataModel.Converters;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// 
/// </summary>
public class NullablePropertyDefinition : PropertyDefinition
{
    /// <summary>Gets or sets optional type of the Type Property
    /// which must match a type listed in the Supported Formats table below.</summary>
    /// <remarks>Type and RefTypeId are mutually exclusive, and at least one is required.</remarks>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Type)]
    [JsonConverter(typeof(RequiredPropertyConverter<string[]>))]
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "OMF Type field must serialize as an array per contract.")]
    public new string[] Type { get; set; }
}
