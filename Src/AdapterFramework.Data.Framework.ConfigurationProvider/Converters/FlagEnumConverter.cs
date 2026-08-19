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
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Converters;

/// <summary>
/// JSON converter for flag enums.
/// </summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
public class FlagEnumConverter<TEnum>(Func<TEnum>? handleNull) : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly string[] _separator = [", "];

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var outVal = 0;

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return (TEnum)Enum.ToObject(typeof(TEnum), outVal);
                }

                if (reader.TokenType == JsonTokenType.String)
                {
                    var enumString = reader.GetString() ?? throw new JsonException("Expected a non-null string.");

                    try
                    {
                        outVal |= (int)Enum.Parse(typeof(TEnum), enumString, true);
                    }
                    catch (ArgumentException ex)
                    {
                        var enums = string.Join(", ", Enum.GetNames(typeof(TEnum)));
                        throw new ArgumentException($"Failed to convert {enumString} to an enum. Supported values: {enums}", ex);
                    }
                }
                else
                {
                    throw new JsonException($"Unexpected token type. Got {reader.TokenType}, expected a string representing an enum value.");
                }
            }
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            if (handleNull != null)
            {
                return handleNull();
            }

            return default;
        }
        else
        {
            throw new JsonException("Unexpected json. Expected an array starting with '['. ");
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        ThrowHelper.ThrowIfArgumentNull(writer, nameof(writer));
        var flags = value.ToString().Split(_separator, StringSplitOptions.RemoveEmptyEntries);

        writer.WriteStartArray();
        foreach (var flag in flags)
        {
            writer.WriteStringValue(flag);
        }

        writer.WriteEndArray();
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(TEnum);
    }
}
