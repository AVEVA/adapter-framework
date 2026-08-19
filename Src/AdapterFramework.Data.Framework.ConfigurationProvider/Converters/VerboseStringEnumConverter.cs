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

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Converters;

public class VerboseStringEnumConverter<T> : JsonConverter<T> where T : Enum
{
    private const string ValueSeparator = ", ";
    private readonly JsonConverter<T> _baseConverter;

    public VerboseStringEnumConverter(JsonConverter<T> converter)
    {
        _baseConverter = converter;
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        try
        {
            return _baseConverter.Read(ref reader, typeToConvert, options);
        }
        catch (JsonException)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw;
            }

            var input = reader.GetString();
            throw new JsonException(
                $"Error converting value '{input}' to {typeToConvert.Name} enumeration set. Valid values: {string.Join(ValueSeparator, typeToConvert.GetEnumNames())}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        _baseConverter.Write(writer, value, options);
    }
}
