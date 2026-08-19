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
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.ConfigurationProvider.Converters;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests.Converters;

public class VerboseStringEnumConverter_Tests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new JsonStringEnumConverterFactory(), },
    };

    [Fact]
    public void EnumConversion_Serialize_ValidInput_Test()
    {
        var loggingConfiguration = new LoggerConfiguration();
        var payload = JsonSerializer.Serialize(loggingConfiguration, _options);
        var jsonElement = JsonDocument.Parse(payload).RootElement;
        var logLevel = jsonElement.GetProperty(nameof(loggingConfiguration.LogLevel)).GetString();

        Assert.Equal(loggingConfiguration.LogLevel.ToString(), logLevel);
    }

    [Fact]
    public void EnumConversion_Deserialize_ValidInput_Test()
    {
        var loggingConfiguration = new LoggerConfiguration();
        var payload = JsonSerializer.Serialize(loggingConfiguration, _options);

        var deserializedLoggingConfiguration = JsonSerializer.Deserialize<LoggerConfiguration>(payload, _options);

        Assert.Equivalent(loggingConfiguration, deserializedLoggingConfiguration);
    }

    [Fact]
    public void EnumConversion_Deserialize_InvalidInput_Test()
    {
        var loggingConfiguration = new LoggerConfiguration();
        var payload = JsonSerializer.Serialize(loggingConfiguration, _options);
        var jsonNode = JsonNode.Parse(payload);

        jsonNode[nameof(loggingConfiguration.LogLevel)] = "InvalidOption";

        var serializationException = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LoggerConfiguration>(jsonNode.ToJsonString(), _options));

        var loggingOptions = string.Join(", ", typeof(LogLevel).GetEnumNames());
        Assert.Contains(loggingOptions, serializationException.Message);
    }

    [Fact]
    public void EnumConversion_Deserialize_NumericValidInput_Test()
    {
        var loggingConfiguration = new LoggerConfiguration();
        var payload = JsonSerializer.Serialize(loggingConfiguration);

        var deserializedLoggingConfiguration = JsonSerializer.Deserialize<LoggerConfiguration>(payload, _options);

        Assert.Equivalent(loggingConfiguration, deserializedLoggingConfiguration);
    }
}
