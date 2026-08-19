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

public class DataSelectionConfigurationBase_Tests
{
    [Theory]
    [InlineData(1900, false)]
    [InlineData(1899, false)]
    [InlineData(1901, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void DataSelectionConfigurationBase_Validate_StreamIdLengthTest(int streamIdLength, bool exceedsMaximumLength)
    {
        var dataSelection = new TestDataSelectionConfigurationBase
        {
            StreamId = TestUtilities.GenerateRandomString(streamIdLength),
        };

        var validationErrors = dataSelection.Validate().ToArray();

        if (exceedsMaximumLength)
        {
            var expectedMessage = string.Format(CultureInfo.InvariantCulture,
                EdgeSystemConstants.MaximumIdentifierLengthExceededError,
                nameof(DataSelectionConfigurationBase.StreamId),
                EdgeSystemConstants.MaximumStreamIdLengthNoPrefix);

            Assert.True(validationErrors.Length > 0);
            Assert.Equal(expectedMessage, validationErrors[0].ErrorMessage);
        }
        else
        {
            Assert.Empty(validationErrors);
        }
    }

    [Theory]
    [InlineData(2000, false)]
    [InlineData(2001, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void DataSelectionConfigurationBase_Validate_StreamNameLengthTest(int streamNameLength, bool exceedsMaximumLength)
    {
        var dataSelection = new TestDataSelectionConfigurationBase
        {
            StreamId = "ValidStreamId",
            Name = TestUtilities.GenerateRandomString(streamNameLength),
        };

        var validationErrors = dataSelection.Validate().ToArray();

        if (exceedsMaximumLength)
        {
            var expectedMessage = string.Format(CultureInfo.InvariantCulture,
                EdgeSystemConstants.MaximumStringLengthExceededError,
                nameof(DataSelectionConfigurationBase.Name),
                EdgeSystemConstants.MaximumStreamIdLengthNoPrefix + EdgeSystemConstants.MaximumStreamIdPrefixLength);

            Assert.True(validationErrors.Length > 0);
            Assert.Equal(expectedMessage, validationErrors[0].ErrorMessage);
        }
        else
        {
            Assert.Empty(validationErrors);
        }
    }

    [Fact]
    public void DataSelectionConfigurationBase_Validate_StreamIdLengthWithInvalidCharacters_Test()
    {
        var streamIdInitial = TestUtilities.GenerateRandomString(EdgeSystemConstants.MaximumStreamIdLengthNoPrefix - 1);

        foreach (var (invalidCharacter, _) in StringExtensions.OmfSubstitutionCharactersMap)
        {
            var dataSelection = new TestDataSelectionConfigurationBase
            {
                StreamId = streamIdInitial + invalidCharacter,
            };

            var validationErrors = dataSelection.Validate().ToArray();
            var expectedMessage = string.Format(CultureInfo.InvariantCulture,
                EdgeSystemConstants.MaximumIdentifierLengthExceededError,
                nameof(DataSelectionConfigurationBase.StreamId),
                EdgeSystemConstants.MaximumStreamIdLengthNoPrefix);

            Assert.True(validationErrors.Length > 0);
            Assert.Equal(expectedMessage, validationErrors[0].ErrorMessage);
        }
    }

    [Theory]
    [InlineData("    ")]
    [InlineData("")]
    [InlineData(null)]
    public void DataSelectionConfigurationBase_Validate_StreamIdLength_NoCheckWhenNullOrWhiteSpace(string streamId)
    {
        var dataSelection = new TestDataSelectionConfigurationBase
        {
            StreamId = streamId,
        };

        var validationErrors = dataSelection.Validate();
        Assert.Empty(validationErrors);
    }
}
