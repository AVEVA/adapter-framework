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
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Converters;

public sealed class JsonStringEnumConverterFactory : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _baseConverter;

    public JsonStringEnumConverterFactory(JsonNamingPolicy namingPolicy = null, bool allowIntegerValues = true)
    {
        _baseConverter = new JsonStringEnumConverter(namingPolicy, allowIntegerValues);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonConverter = _baseConverter.CreateConverter(typeToConvert, options);

        return (JsonConverter)Activator.CreateInstance(
            typeof(VerboseStringEnumConverter<>).MakeGenericType(typeToConvert),
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new object[] { jsonConverter },
            null);
    }

    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return typeToConvert.IsEnum;
    }
}
