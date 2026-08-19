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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using Xunit;

namespace AdapterFramework.Data.Framework.Abstractions.Tests.Configuration;

public class GeneralConfiguration_Tests
{
    private const string ApplicationDataDirectory = "UnitTests";

    [Theory]
    [InlineData("{\"EnableMETadata\": true}", MetadataInfo.High)]
    [InlineData("{\"enablemetadata\": false}", MetadataInfo.Low)]
    public void GeneralConfiguration_Validate_Migration_CaseInsensitive_Test(string payload, MetadataInfo expectedValue)
    {
        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        var output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);

        Assert.True(configurationProvider.IsConfigurationValid(output, out var errors));
        Assert.Empty(errors);
        Assert.Empty(output.JsonExtensionData);
        Assert.Equal(expectedValue, output.MetadataLevel);
    }

    [Theory]
    [InlineData("{\"EnableMETadata\": \"invalidBoolean\"}")]
    [InlineData("{\"enablemetadata\": 42}")]
    public void GeneralConfiguration_Validate_Migration_InvalidConfiguration_Test(string payload)
    {
        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        var output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);

        Assert.False(configurationProvider.IsConfigurationValid(output, out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void GeneralConfiguration_ValidatePrefix()
    {
        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        // over 100 characters = fail
        var payload = $"{{\"HealthPrefix\": \"{new string('a', 101)}\"}}"; 
        var output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);
        Assert.False(configurationProvider.IsConfigurationValid(output, out var errors));
        Assert.NotEmpty(errors);

        // only white spaces = fail
        payload = $"{{\"HealthPrefix\": \"   \"}}";
        output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);
        Assert.False(configurationProvider.IsConfigurationValid(output, out errors));
        Assert.NotEmpty(errors);

        // over 100 characters when sanitized = fail
        payload = $"{{\"HealthPrefix\": \"{new string('a', 98)}?\"}}";
        output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);
        Assert.False(configurationProvider.IsConfigurationValid(output, out errors));
        Assert.NotEmpty(errors);

        // < 100 characters = pass
        payload = $"{{\"HealthPrefix\": \"{new string('a', 99)}\"}}";
        output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);
        Assert.True(configurationProvider.IsConfigurationValid(output, out _));

        // contains whitespace OK = pass
        payload = $"{{\"HealthPrefix\": \"a a a\"}}";
        output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);
        Assert.True(configurationProvider.IsConfigurationValid(output, out _));

        // null = pass
        payload = $"{{\"HealthPrefix\": null}}";
        output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);
        Assert.True(configurationProvider.IsConfigurationValid(output, out _));

        // empty = pass
        payload = $"{{\"HealthPrefix\": \"\"}}";
        output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);
        Assert.True(configurationProvider.IsConfigurationValid(output, out _));
    }

    [Theory]
    [InlineData("{\"TestProperty\": \"invalidBoolean\"}")]
    [InlineData("{\"TestProperty\": 42}")]
    public void GeneralConfiguration_Validate_UnknownProperty_Test(string payload)
    {
        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        var output = JsonSerializer.Deserialize<GeneralConfiguration>(payload);

        Assert.False(configurationProvider.IsConfigurationValid(output, out var errors));
        Assert.NotEmpty(errors);
    }
}
