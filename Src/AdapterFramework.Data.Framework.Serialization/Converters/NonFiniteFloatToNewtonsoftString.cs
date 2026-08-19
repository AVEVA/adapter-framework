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

public class NonFiniteFloatToNewtonsoftString : JsonConverter<float>
{
    public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return float.Parse(reader.GetString(), CultureInfo.InvariantCulture);
        }

        return reader.GetSingle();
    }

    public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options)
    {
        ThrowHelper.ThrowIfArgumentNull(writer, nameof(writer));

        if (float.IsFinite(value))
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            if (float.IsPositiveInfinity(value))
            {
                writer.WriteStringValue(NonFiniteDoubleToNewtonsoftString.InfinityString);
            }
            else if (float.IsNegativeInfinity(value))
            {
                writer.WriteStringValue(NonFiniteDoubleToNewtonsoftString.NegativeInfinityString);
            }
            else
            {
                writer.WriteStringValue(NonFiniteDoubleToNewtonsoftString.NotANumberString);
            }
        }
    }
}
