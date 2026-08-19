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

namespace AdapterFramework.Data.Framework.Abstractions.Tests.Configuration;

public class ManagedSecretConfiguration_Tests
{
    [Theory]
    [InlineData("{{cannotmatchpattern}}")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ManagedSecretConfiguration_InvalidValueTest(string value)
    {
        var secretConfig = new ManagedSecretConfiguration
        {
            Id = "testId",
            Value = value,
        };

        var validationResults = EdgeConfigurationBase.ValidateConfiguration(secretConfig);
        Assert.NotEmpty(validationResults);
    }

    [Theory]
    [InlineData("plaintext")]
    [InlineData("asfg asfgfaslkdf/sdf/sdfs")]
    public void ManagedSecretConfiguration_ValidValueTest(string value)
    {
        var secretConfig = new ManagedSecretConfiguration
        {
            Id = "testId",
            Value = value,
        };

        var validationResults = secretConfig.Validate();
        Assert.Empty(validationResults);
    }

    [Theory]
    [InlineData("{{cannotmatchpattern}}")]
    [InlineData("{{noBraces")]
    [InlineData("noBraces}}")]
    [InlineData("{noBraces}}")]
    [InlineData("no{Brac}es")]
    [InlineData("noBrac}es")]
    [InlineData("no{Braces")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" ")]
    public void ManagedSecretConfiguration_IdInvalidTest(string id)
    {
        var secretConfig = new ManagedSecretConfiguration
        {
            Id = id,
            Value = "testPlaintext",
        };

        var validationResults = EdgeConfigurationBase.ValidateConfiguration(secretConfig);
        Assert.NotEmpty(validationResults);
    }

    [Theory]
    [InlineData("id-123")]
    [InlineData("valid.id")]
    public void ManagedSecretConfiguration_ValidIdTest(string id)
    {
        var secretConfig = new ManagedSecretConfiguration
        {
            Id = id,
            Value = "testPlaintext",
        };

        var validationResults = secretConfig.Validate();
        Assert.Empty(validationResults);
    }
}
