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
using AdapterFramework.Data.Framework.Abstractions.Constants;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AdapterFramework.Data.Framework.Abstractions.Tests.Configuration;

public class EndpointConfigurationBase_Tests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MyOcsTest.com")]
    [InlineData("opc.tcp://omf.AdapterFramework.int:53530")]
    public void EndpointConfiguration_Endpoint_InvalidInput_Test(string endpoint)
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = endpoint,
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var validationResults = endpointConfiguration.Validate();

        Assert.NotEmpty(validationResults);
    }

    [Theory]
    [InlineData("MyTokenProducer.com")]
    [InlineData("opc.tcp://omf.AdapterFramework.int:53530")]
    public void EndpointConfiguration_TokenEndpoint_InvalidInput_Test(string tokenEndpoint)
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "https://localhost:443",
            UserName = "TestUser",
            Password = "TestPassword",
            TokenEndpoint = tokenEndpoint,
        };
        var validationResults = endpointConfiguration.Validate();

        Assert.NotEmpty(validationResults);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("https://tokenProvider.com")]
    public void EndpointConfiguration_TokenEndpoint_ValidInput_Test(string tokenEndpoint)
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "https://localhost:443",
            UserName = "TestUser",
            Password = "TestPassword",
            TokenEndpoint = tokenEndpoint,
        };

        var validationResults = endpointConfiguration.Validate();

        Assert.Empty(validationResults);
        Assert.NotNull(endpointConfiguration.Id);
    }

    [Fact]
    public void EndpointConfiguration_DuplicateEndpoint_InvalidInput_Test()
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "https://localhost:443",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var configs = new[] { endpointConfiguration, endpointConfiguration };
        var configChangedArgs = new ConfigurationChangedEventArgs(null, configs);
        var validationResults = EndpointConfigurationBase.CheckForDuplicateEndpoints(configChangedArgs, "Test");
        Assert.NotEmpty(validationResults);
    }

    [Fact]
    public void EndpointConfiguration_TwoEndpoint_ValidInput_Test()
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "https://localhost:443",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var endpointConfiguration2 = new EndpointConfigurationBase
        {
            Endpoint = "https://example.com",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var configs = new[] { endpointConfiguration, endpointConfiguration2 };
        var configChangedArgs = new ConfigurationChangedEventArgs(null, configs);
        var validationResults = EndpointConfigurationBase.CheckForDuplicateEndpoints(configChangedArgs, "Test");
        Assert.Empty(validationResults);
    }

    [Fact]
    public void EndpointConfiguration_OCS_Authentication_Valid_Test()
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "https://localhost:443",
            UserName = null,
            Password = null,
            ClientId = "ClientId",
            ClientSecret = "ClientSecret",
            TokenEndpoint = "https://tokenService:443",
            ValidateEndpointCertificate = false,
        };

        Assert.Null(endpointConfiguration.Id);

        var validationResults = endpointConfiguration.Validate();

        Assert.Empty(validationResults);
        Assert.NotNull(endpointConfiguration.Id);
    }

    [Fact]
    public void EndpointConfiguration_HttpEndpoint_ValidInput_Test()
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "http://localhost:443",
        };

        var validationResults = endpointConfiguration.Validate();

        Assert.Empty(validationResults);
        Assert.NotNull(endpointConfiguration.Id);
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("", "", "", "")]
    public void EndpointConfiguration_HttpEndpoint_ValidKerebrosInput_Test(string userName, string password, string clientId, string clientSecret)
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "http://localhost:443",
            UserName = userName,
            Password = password,
            ClientId = clientId,
            ClientSecret = clientSecret,
        };

        var validationResults = endpointConfiguration.Validate();
        Assert.Empty(validationResults);
        Assert.Contains(EdgeSystemConstants.NegotiateAuth, endpointConfiguration.ToString(), System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TestUser", "TestPassword", "TestClientId", "TestSecret")]
    [InlineData("TestUser", null, "TestClientId", "TestSecret")]
    [InlineData("TestUser", null, "TestClientId", null)]
    [InlineData("TestUser", null, null, "TestSecret")]
    [InlineData("TestUser", null, null, null)]
    [InlineData(null, "TestPassword", "TestClientId", "TestSecret")]
    [InlineData(null, "TestPassword", "TestClientId", null)]
    [InlineData(null, "TestPassword", null, "TestSecret")]
    [InlineData(null, "TestPassword", null, null)]
    [InlineData(null, null, "TestClientId", null)]
    [InlineData(null, null, null, "TestSecret")]
    public void EndpointConfiguration_HttpEndpoint_InvalidInput_Test(string userName, string password, string clientId, string clientSecret)
    {
        var endpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = "https://localhost:443",
            UserName = userName,
            Password = password,
            ClientId = clientId,
            ClientSecret = clientSecret,
        };

        var validationResults = endpointConfiguration.Validate();
        Assert.NotEmpty(validationResults);
    }

    [Fact]
    public void EgressEndpointConfiguration_DeprecatedFieldsIgnored_Test()
    {
        var json = @"

    {
        ""Endpoint"": ""https://localhost/piwebapi/omf"",
        ""UserName"": ""Hello"",
        ""Password"": ""HI"",
        ""buffering"": ""hello"",
        ""maxbuffersizemb"" : 1337
            }
";

        var endpointConfiguration = JsonSerializer.Deserialize<EndpointConfigurationBase>(json, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
        var validationResults = endpointConfiguration.Validate();
        Assert.Empty(validationResults);
        Assert.Empty(endpointConfiguration.JsonExtensionData);

        var serializedJson = JsonSerializer.Serialize(endpointConfiguration);
        Assert.DoesNotContain("buffering", serializedJson, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("maxbuffersizemb", serializedJson, System.StringComparison.OrdinalIgnoreCase);
    }
}
