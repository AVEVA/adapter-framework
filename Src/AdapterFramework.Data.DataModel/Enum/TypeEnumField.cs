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
using AdapterFramework.Data.DataModel.Extensions;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.DataModel.Enum;

/// <summary>
/// An object representing an OMF Type Message's Enum OMF Spec 1.2.
/// https://docs.aveva.com/bundle/omf/page/1283989.html.
/// </summary>
public class TypeEnumField
{
    /// <summary>
    /// Creates an instance of TypeEnumField. Required for JSON deserialization of reference types.
    /// </summary>
    public TypeEnumField()
    {
    }

    /// <summary>
    /// Creates an instance of TypeEnumField
    /// </summary>
    /// <param name="values">The enum values.</param>
    /// <param name="type">The backing field type of the enum. If no type is given, the enum type will be of type Int16.</param>
    public TypeEnumField(IEnumerable<EnumValueItem> values, Type type = null)
    {
        ThrowHelper.ThrowIfArgumentNull(values, nameof(values));

        type ??= typeof(short);

        var propertyDef = type.ToPropertyDefinition();
        if (propertyDef.Type != Tokens.IntegerToken)
        {
            throw new ArgumentException($"{nameof(type)} can only be {Tokens.IntegerToken}.", nameof(type));
        }

        Type = propertyDef.Type;
        Format = propertyDef.Format;
        Values = values;
    }

    /// <summary>Gets or sets the type of the data being sent. Only 'integer' is supported.</summary>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Type)]
    public string Type { get; set; }

    /// <summary>Gets or sets optional format of the Type Property type that, if specified, must be from the Supported Formats table.</summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Format)]
    public string Format { get; set; }

    /// <summary>Gets or sets required Enum values field.</summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Values)]
    [JsonConverter(typeof(RequiredPropertyConverter<IEnumerable<EnumValueItem>>))]
    public IEnumerable<EnumValueItem> Values { get; set; }
}
