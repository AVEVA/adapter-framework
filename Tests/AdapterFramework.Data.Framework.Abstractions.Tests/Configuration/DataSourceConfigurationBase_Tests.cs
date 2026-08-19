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

public class DataSourceConfigurationBase_Tests
{
    [Theory]
    [InlineData(100, false)]
    [InlineData(99, false)]
    [InlineData(101, true)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    public void DataSourceConfigurationBase_Validate_StreamIdPrefixLengthTest(int streamIdPrefixLength, bool exceedsMaximumLength)
    {
        var dataSource = new TestDataSourceConfigurationBase
        {
            StreamIdPrefix = TestUtilities.GenerateRandomString(streamIdPrefixLength),
        };

        var validationErrors = dataSource.Validate().ToArray();

        if (exceedsMaximumLength)
        {
            var expectedMessage = string.Format(CultureInfo.InvariantCulture,
                EdgeSystemConstants.MaximumIdentifierLengthExceededError,
                nameof(DataSourceConfigurationBase.StreamIdPrefix),
                EdgeSystemConstants.MaximumStreamIdPrefixLength);

            Assert.True(validationErrors.Length > 0);
            Assert.Equal(expectedMessage, validationErrors[0].ErrorMessage);
        }
        else
        {
            Assert.Empty(validationErrors);
        }
    }

    [Fact]
    public void DataSourceConfigurationBase_Validate_StreamIdPrefixLengthWithInvalidCharacters_Test()
    {
        var streamIdPrefixInitial = TestUtilities.GenerateRandomString(EdgeSystemConstants.MaximumStreamIdPrefixLength - 1);

        foreach (var (invalidCharacter, _) in StringExtensions.OmfSubstitutionCharactersMap)
        {
            var dataSource = new TestDataSourceConfigurationBase
            {
                StreamIdPrefix = streamIdPrefixInitial + invalidCharacter,
            };

            var validationErrors = dataSource.Validate().ToArray();
            var expectedMessage = string.Format(CultureInfo.InvariantCulture,
                EdgeSystemConstants.MaximumIdentifierLengthExceededError,
                nameof(DataSourceConfigurationBase.StreamIdPrefix),
                EdgeSystemConstants.MaximumStreamIdPrefixLength);

            Assert.True(validationErrors.Length > 0);
            Assert.Equal(expectedMessage, validationErrors[0].ErrorMessage);
        }
    }

    [Theory]
    [InlineData("    ")]
    [InlineData("")]
    [InlineData(null)]
    public void DataSourceConfigurationBase_Validate_StreamIdPrefixLength_NoCheckWhenNullOrWhiteSpace(string streamIdPrefix)
    {
        var dataSource = new TestDataSourceConfigurationBase
        {
            StreamIdPrefix = streamIdPrefix,
        };

        var validationErrors = dataSource.Validate();
        Assert.Empty(validationErrors);
    }
}
