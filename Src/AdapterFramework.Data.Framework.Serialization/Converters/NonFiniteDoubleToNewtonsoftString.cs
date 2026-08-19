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
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Serialization.Converters;

public class NonFiniteDoubleToNewtonsoftString : JsonConverter<double>
{
    internal static readonly JsonEncodedText NotANumberString = JsonEncodedText.Encode("NaN");
    internal static readonly JsonEncodedText InfinityString = JsonEncodedText.Encode("Infinity");
    internal static readonly JsonEncodedText NegativeInfinityString = JsonEncodedText.Encode("-Infinity");

    public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return double.Parse(reader.GetString(), CultureInfo.InvariantCulture);
        }

        return reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double doubleValue, JsonSerializerOptions options)
    {
        ThrowHelper.ThrowIfArgumentNull(writer, nameof(writer));

        if (double.IsFinite(doubleValue))
        {
            writer.WriteNumberValue(doubleValue);
        }
        else
        {
            if (double.IsPositiveInfinity(doubleValue))
            {
                writer.WriteStringValue(InfinityString);
            }
            else if (double.IsNegativeInfinity(doubleValue))
            {
                writer.WriteStringValue(NegativeInfinityString);
            }
            else
            {
                writer.WriteStringValue(NotANumberString);
            }
        }
    }
}
