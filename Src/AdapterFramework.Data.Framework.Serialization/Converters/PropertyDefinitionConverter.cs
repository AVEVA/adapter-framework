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
using System.Text.Json;
using System.Text.Json.Serialization;
using AdapterFramework.Data.DataModel;

namespace AdapterFramework.Data.Framework.Serialization.Converters;

/// <inheritdoc/>
public class PropertyDefinitionConverter : JsonConverter<PropertyDefinition>
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters =
            {
                new NonFiniteDoubleToNewtonsoftString(),
                new NonFiniteFloatToNewtonsoftString(),
                new ExtrapolationInterpolationEnumStringConverter(),
                new FailoverRoleEnumStringConverter(),
            },
    };

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(PropertyDefinition).IsAssignableFrom(typeToConvert);
    }

    /// <inheritdoc/>
    public override PropertyDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PropertyDefinition value, JsonSerializerOptions options)
    {
        if (value is NullablePropertyDefinition nullableDef)
        {
            JsonSerializer.Serialize(writer, nullableDef, _serializerOptions);
        }
        else
        {
            JsonSerializer.Serialize(writer, value, _serializerOptions);
        }
    }
}
