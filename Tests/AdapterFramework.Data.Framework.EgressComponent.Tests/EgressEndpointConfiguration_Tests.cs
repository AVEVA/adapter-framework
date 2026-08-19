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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests;

public class EgressEndpointConfiguration_Tests
{
    [Fact]
    public void EgressEndpointConfiguration_DuplicateEndpoint_InvalidInput_Test()
    {
        var endpointConfiguration = new EgressEndpointConfiguration
        {
            Endpoint = "https://localhost:443",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var configs = new[] { endpointConfiguration, endpointConfiguration };
        var configChangedArgs = new ConfigurationChangedEventArgs(null, configs);
        var validationResults = EgressEndpointConfiguration.CheckForDuplicateEndpoints(configChangedArgs);
        Assert.NotEmpty(validationResults);
    }

    [Fact]
    public void EgressEndpointConfiguration_DuplicateEndpoint_ValidInput_Test()
    {
        var endpointConfiguration = new EgressEndpointConfiguration
        {
            Endpoint = "https://localhost:443",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var endpointConfiguration2 = new EgressEndpointConfiguration
        {
            Endpoint = "https://example.com",
            UserName = "TestUser",
            Password = "TestPassword",
        };

        var configs = new[] { endpointConfiguration, endpointConfiguration2 };
        var configChangedArgs = new ConfigurationChangedEventArgs(null, configs);
        var validationResults = EgressEndpointConfiguration.CheckForDuplicateEndpoints(configChangedArgs);
        Assert.Empty(validationResults);
    }
}
