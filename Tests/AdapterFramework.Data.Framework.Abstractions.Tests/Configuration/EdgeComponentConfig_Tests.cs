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
using System.Globalization;
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Abstractions.Tests.Configuration;

public class EdgeComponentConfig_Tests
{
    [Theory]
    [InlineData("[*sa$d%h` ", true)]
    [InlineData("moon night sky", false)]
    [InlineData("", true)]
    [InlineData("    ", true)]
    [InlineData(null, true)]
    [InlineData("sadsad asdsdsad", false)]
    [InlineData(" asdsadsad ", true)]
    public void EdgeComponentConfig_InvalidComponentId_Test(string componentId, bool expectedResult)
    {
        var component = new EdgeComponentConfig
        {
            ComponentId = componentId,
            ComponentType = "TestAdapter",
        };

        var validationResults = EdgeConfigurationBase.ValidateConfiguration(component);

        if (expectedResult)
        {
            Assert.NotEmpty(validationResults);
        }
        else
        {
            Assert.Empty(validationResults);
        }
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(98, false)]
    [InlineData(100, true)]
    public void EdgeComponentConfig_Validate_ComponentIdLengthTest(int componentIdLength, bool exceedsMaximumLength)
    {
        var component = new EdgeComponentConfig
        {
            ComponentId = TestUtilities.GenerateRandomString(componentIdLength),
            ComponentType = "TestAdapter",
        };

        var validationErrors = component.Validate().ToArray();

        if (exceedsMaximumLength)
        {
            var expectedMessage = string.Format(CultureInfo.InvariantCulture,
                EdgeSystemConstants.MaximumIdentifierLengthExceededError,
                nameof(EdgeComponentConfig.ComponentId),
                EdgeSystemConstants.MaximumComponentIdLength);

            Assert.True(validationErrors.Length > 0);
            Assert.Equal(expectedMessage, validationErrors[0].ErrorMessage);
        }
        else
        {
            Assert.Empty(validationErrors);
        }
    }

    [Fact]
    public void EdgeComponentConfig_Validate_ComponentIdLengthWithInvalidCharacters_Test()
    {
        var componentIdInitial = TestUtilities.GenerateRandomString(EdgeSystemConstants.MaximumComponentIdLength - 1);

        foreach (var (invalidCharacter, _) in StringExtensions.OmfSubstitutionCharactersMap)
        {
            var component = new EdgeComponentConfig
            {
                ComponentId = componentIdInitial + invalidCharacter,
                ComponentType = "TestAdapter",
            };

            var validationErrors = component.Validate().ToArray();
            var expectedMessage = string.Format(CultureInfo.InvariantCulture,
                EdgeSystemConstants.MaximumIdentifierLengthExceededError,
                nameof(EdgeComponentConfig.ComponentId),
                EdgeSystemConstants.MaximumComponentIdLength);

            Assert.True(validationErrors.Length > 0);
            Assert.Equal(expectedMessage, validationErrors[0].ErrorMessage);
        }
    }
}
