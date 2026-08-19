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
using System.Buffers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Converters;

public class StringToUShortConverter : JsonConverter<ushort>
{
    public override ushort Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var span = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
            if (Utf8Parser.TryParse(span, out ushort number, out int bytesConsumed) && span.Length == bytesConsumed)
            {
                return number;
            }

            if (ushort.TryParse(reader.GetString(), out number))
            {
                return number;
            }
        }

        return reader.GetUInt16();
    }

    public override void Write(Utf8JsonWriter writer, ushort ushortValue, JsonSerializerOptions options)
    {
        ThrowHelper.ThrowIfArgumentNull(writer, nameof(writer));
        writer.WriteNumberValue(ushortValue);
    }
}
