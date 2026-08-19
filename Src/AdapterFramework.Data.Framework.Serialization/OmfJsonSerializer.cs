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
using System.Text.Json;
using System.Text.Json.Serialization;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Serialization.Converters;

namespace AdapterFramework.Data.Framework.Serialization;

/// <summary>
/// An object defining <see cref="OmfJsonSerializer"/> class.
/// </summary>
public class OmfJsonSerializer : ISerializer
{
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="OmfJsonSerializer"/> class.
    /// </summary>
    public OmfJsonSerializer()
    {
        _serializerOptions = new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new NonFiniteDoubleToNewtonsoftString(),
                new NonFiniteFloatToNewtonsoftString(),
                new ExtrapolationInterpolationEnumStringConverter(),
                new FailoverRoleEnumStringConverter(),
                new PropertyDefinitionConverter(),
            },
        };
    }

    public string Format => "Json";

    public byte[] Serialize(object obj)
    {
        return JsonSerializer.SerializeToUtf8Bytes(obj, _serializerOptions);
    }

    public byte[] Serialize<T>(T obj)
    {
        return JsonSerializer.SerializeToUtf8Bytes(obj, _serializerOptions);
    }

    public object Deserialize(byte[] bytes)
    {
        return JsonSerializer.Deserialize<object>(bytes, _serializerOptions);
    }

    public T Deserialize<T>(byte[] bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes, _serializerOptions);
    }

    public object Deserialize(string content)
    {
        return JsonSerializer.Deserialize<object>(content, _serializerOptions);
    }

    public T Deserialize<T>(string content)
    {
        return JsonSerializer.Deserialize<T>(content, _serializerOptions);
    }
}
