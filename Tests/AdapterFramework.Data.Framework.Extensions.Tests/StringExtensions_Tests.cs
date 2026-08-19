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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace AdapterFramework.Data.Framework.Extensions.Tests;

public class StringExtensions_Tests
{
    private const string HelloString = "Hello";
    private const string WorldString = "World!";

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void StringExtensions_ToEdsIdentifier_InvalidInput_Test(string input)
    {
        Assert.ThrowsAny<Exception>(() => input.ToEdsIdentifier());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void StringExtensions_ToOmfIdentifier_InvalidInput_Test(string input)
    {
        Assert.ThrowsAny<Exception>(() => input.ToOmfIdentifier());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void StringExtensions_ToPrefixedOmfIdentifier_InvalidInput_Test(string input)
    {
        Assert.ThrowsAny<Exception>(() => input.ToPrefixedOmfIdentifier(string.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void StringExtensions_ToPrefixedOmfIdentifier_NullEmptyWhitespaceDoesNotThrow_Test(string prefix)
    {
        const string InputString = HelloString + WorldString;

        var prefixedOmfIdentifier = InputString.ToPrefixedOmfIdentifier(prefix);
        var omfIdentifier = InputString.ToOmfIdentifier();

        Assert.Equal(omfIdentifier, prefixedOmfIdentifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void StringExtensions_DecodeIdentifier_InvalidInput_Test(string input)
    {
        var encodedInput = input.DecodeIdentifier();

        Assert.Equal(input, encodedInput);
    }

    [Fact]
    public void StringExtensions_ToEdsIdentifier_DecodeIdentifier_Test()
    {
        var invalidStreamIdBuilder = new StringBuilder(HelloString);

        foreach (var invalidCharacter in StringExtensions.EdsSubstitutionCharactersMap.Keys)
        {
            invalidStreamIdBuilder.Append(invalidCharacter);
        }

        invalidStreamIdBuilder.Append(WorldString);

        var invalidStreamId = invalidStreamIdBuilder.ToString();

        var encodedStreamId = invalidStreamId.ToEdsIdentifier();

        Assert.NotEqual(invalidStreamId, encodedStreamId);

        var decodedStreamId = encodedStreamId.DecodeIdentifier();

        Assert.Equal(invalidStreamId, decodedStreamId);
    }

    [Fact]
    public void StringExtensions_ToOmfIdentifier_DecodeIdentifier_Test()
    {
        var invalidStreamIdBuilder = new StringBuilder(HelloString);

        foreach (var invalidCharacter in StringExtensions.OmfSubstitutionCharactersMap.Keys)
        {
            invalidStreamIdBuilder.Append(invalidCharacter);
        }

        invalidStreamIdBuilder.Append(WorldString);

        var invalidStreamId = invalidStreamIdBuilder.ToString();

        var encodedStreamId = invalidStreamId.ToOmfIdentifier();

        Assert.NotEqual(invalidStreamId, encodedStreamId);
        foreach (var invalidCharacter in StringExtensions.OmfSubstitutionCharactersMap.Keys)
        {
            Assert.DoesNotContain(encodedStreamId, invalidCharacter.ToString(CultureInfo.InvariantCulture), StringComparison.InvariantCulture);
        }

        var decodedStreamId = encodedStreamId.DecodeIdentifier();

        Assert.Equal(invalidStreamId, decodedStreamId);
    }

    [Fact]
    public void StringExtensions_ToPrefixedOmfIdentifier_DecodeIdentifier_Test()
    {
        const string Prefix = "Prefix_";
        var invalidStreamIdBuilder = new StringBuilder(HelloString);

        foreach (var invalidCharacter in StringExtensions.OmfSubstitutionCharactersMap.Keys)
        {
            invalidStreamIdBuilder.Append(invalidCharacter);
        }

        invalidStreamIdBuilder.Append(WorldString);

        var invalidStreamId = invalidStreamIdBuilder.ToString();

        var prefixedEncodedStreamId = invalidStreamId.ToPrefixedOmfIdentifier(Prefix);

        Assert.NotEqual(invalidStreamId, prefixedEncodedStreamId);
        foreach (var invalidCharacter in StringExtensions.OmfSubstitutionCharactersMap.Keys)
        {
            Assert.DoesNotContain(prefixedEncodedStreamId, invalidCharacter.ToString(CultureInfo.InvariantCulture), StringComparison.InvariantCulture);
        }

        var decodedStreamId = prefixedEncodedStreamId.DecodeIdentifier();

        Assert.Equal(Prefix + invalidStreamId, decodedStreamId);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void StringExtensions_ToEdsIdentifier_MaxLength_Test(int inputLength)
    {
        var invalidStreamIdBuilder = BuildRandomStringFromCharacters(StringExtensions.EdsSubstitutionCharactersMap.Keys.ToReadOnlyList(), inputLength);

        var invalidStreamId = invalidStreamIdBuilder.ToString();

        var encodedStreamId = invalidStreamId.ToEdsIdentifier();

        var replacementStringLength = StringExtensions.EdsSubstitutionCharactersMap.Values.FirstOrDefault()?.Length;
        var expectedLength = inputLength * replacementStringLength;

        if (expectedLength <= StringExtensions.MaxSdsIdentifierLength)
        {
            Assert.Equal(expectedLength, encodedStreamId.Length);
        }
        else
        {
            Assert.True(encodedStreamId.Length > StringExtensions.MaxSdsIdentifierLength - replacementStringLength);
            Assert.True(encodedStreamId.Length <= StringExtensions.MaxSdsIdentifierLength);
        }

        var decodedStreamId = encodedStreamId.DecodeIdentifier();

        Assert.StartsWith(decodedStreamId, invalidStreamId, StringComparison.InvariantCulture);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Test", null)]
    public void StringExtensions_ReplaceCharacters_InvalidInput_Test(string value, Dictionary<char, string> substitutionMap)
    {
        Assert.Throws<ArgumentNullException>(() => value.ReplaceCharacters(substitutionMap));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void StringExtensions_ReplaceCharacters_ValidInput_Test(string value)
    {
        var substitutionMap = new Dictionary<char, string>();
        var replaced = value.ReplaceCharacters(substitutionMap);

        Assert.Equal(value, replaced);
    }

    [Fact]
    public void StringExtensions_ReplaceCharacters_Test()
    {
        const string InputString = "Bonsai";
        const string ExpectedString = "Aurora";

        var substitutionMap = new Dictionary<char, string>
        {
            { 'B', "A" },
            { 'o', "u" },
            { 'n', "r" },
            { 's', "o" },
            { 'a', "r" },
            { 'i', "a" },
        };

        var replaced = InputString.ReplaceCharacters(substitutionMap);

        Assert.Equal(ExpectedString, replaced);
    }

    [Theory]
    [InlineData("[*sa$d%h` ", 4, true)]
    [InlineData("", 0, false)]
    [InlineData("moon night sky", 0, false)]
    public void StringExtensions_ContainsUnsupportedCharacters_Test(string input, int expectedUnsupportedCharacterCount, bool expectedResult)
    {
        var containsUnsupportedCharacters = input.ContainsEdsIdentifierUnsupportedCharacters(out var unsupportedCharacters);

        if (expectedResult)
        {
            Assert.True(containsUnsupportedCharacters, $"The string {input} contains unsupported characters.");
        }
        else
        {
            Assert.False(containsUnsupportedCharacters, $"The string {input} does not contain unsupported characters.");
        }

        Assert.Equal(expectedUnsupportedCharacterCount, unsupportedCharacters.Count);
    }

    [Fact]
    public void StringExtensions_ContainsInvalidFileNameCharacters_Test()
    {
        var invalidCharactersSet = Path.GetInvalidFileNameChars();
        var invalidCharactersCount = invalidCharactersSet.Length;

        var result = new string(invalidCharactersSet).ContainsInvalidFileNameCharacters(out var unsupportedCharacters);

        Assert.True(result);
        Assert.Equal(invalidCharactersCount, unsupportedCharacters.Count);

        result = "Aurora Team Rocks".ContainsEdsIdentifierUnsupportedCharacters(out unsupportedCharacters);

        Assert.False(result);
        Assert.Empty(unsupportedCharacters);
    }

    [Theory]
    [InlineData("[*sa$d%h` ", true)]
    [InlineData(" asdsadsad", true)]
    [InlineData("moon night sky", false)]
    [InlineData(" sdassad ", true)]
    public void StringExtensions_ContainsLeadingOrTrailingSpaces_Test(string input, bool expectedResult)
    {
        var containsLeadingOrTrailingSpaces = input.ContainsLeadingOrTrailingSpaces();

        if (expectedResult)
        {
            Assert.True(containsLeadingOrTrailingSpaces, $"The string {input} contains leading or trailing spaces.");
        }
        else
        {
            Assert.False(containsLeadingOrTrailingSpaces, $"The string {input} does not contain leading or trailing spaces.");
        }
    }

    private static StringBuilder BuildRandomStringFromCharacters(IReadOnlyList<char> characters, int length)
    {
        var random = new Random();
        var invalidStreamIdBuilder = new StringBuilder();

        for (var i = 0; i < length; i++)
        {
            invalidStreamIdBuilder.Append(characters[random.Next(0, characters.Count)]);
        }

        return invalidStreamIdBuilder;
    }
}
