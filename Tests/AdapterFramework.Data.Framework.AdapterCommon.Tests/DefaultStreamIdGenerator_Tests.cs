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
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests;

public class DefaultStreamIdGenerator_Tests
{
    private const string AdapterGeneratedStream = "{Derp}derp{HERP}herp{derp}";
    private const string DefaultStreamId = "{Herp}herp{DERP}derp{herp}";
    private const string SecondaryAdapterGeneratedStream = "{Derp}{Herp}";
    private const string SecondaryDefaultStreamId = "{Derp}fancy{Herp}";
    private readonly string[] _defaultKeywords = { "DeRp", "HeRp", "Hello", "Bye" };

    [Theory]
    [InlineData(AdapterGeneratedStream, DefaultStreamId, "Apple", "Pie", "Trash", "Cow")]
    [InlineData(null, DefaultStreamId, "Apple", "Pie", "Trash", "Cow")]
    [InlineData(null, null, "Apple", "Pie", "Trash", "Cow")]
    [InlineData(AdapterGeneratedStream, null, "Apple", "Pie", "Trash", "Cow")]
    [InlineData(AdapterGeneratedStream, null, null, null, null, null)]
    [InlineData(AdapterGeneratedStream, DefaultStreamId, "Apple")]
    public void GetDefaultStream(string adapterStreamId, string streamId, params string[] values)
    {
        string expectedReturnString = string.Empty;
        string template = streamId ?? adapterStreamId;
        var generator = new DefaultStreamIdGenerator();
        if (adapterStreamId == null)
        {
            Assert.ThrowsAny<ArgumentNullException>(() => generator.SetDefaultStreamIdPattern(adapterStreamId, _defaultKeywords));
            return;
        }

        generator.SetDefaultStreamIdPattern(adapterStreamId, _defaultKeywords);
        generator.UpdateDefaultStreamIdPattern(streamId);

        expectedReturnString += template;

        for (int i = 0; i < _defaultKeywords.Length; i++)
        {
            var stringToReplace = "{" + _defaultKeywords[i] + "}";
            if (expectedReturnString.Contains(stringToReplace, StringComparison.InvariantCultureIgnoreCase))
            {
                if (i > values.Length - 1)
                {
                    Assert.ThrowsAny<ArgumentException>(() => generator.GetDefaultStreamId(values));
                    return;
                }
            }

            expectedReturnString = expectedReturnString.Replace(stringToReplace, values[i], StringComparison.InvariantCultureIgnoreCase);
        }

        var actualStreamId = generator.GetDefaultStreamId(values);
        Assert.Equal(expectedReturnString, actualStreamId);
    }

    [InlineData(SecondaryDefaultStreamId, "Apple", "Pie", "Trash", "Cow")]
    [InlineData(null, "Apple", "Pie", "Trash", "Cow")]
    [InlineData(null, null, null, null, null)]
    [Theory]
    public void SetOrUpdateDefaultStreamIdPattern(string newStreamId, params string[] values)
    {
        var generator = new DefaultStreamIdGenerator();
        generator.SetDefaultStreamIdPattern(AdapterGeneratedStream, _defaultKeywords);
        generator.UpdateDefaultStreamIdPattern(DefaultStreamId);            
        generator.UpdateDefaultStreamIdPattern(newStreamId);

        var generator2 = new DefaultStreamIdGenerator();
        generator2.SetDefaultStreamIdPattern(AdapterGeneratedStream, _defaultKeywords);
        generator2.UpdateDefaultStreamIdPattern(newStreamId);

        Assert.Equal(generator2.GetDefaultStreamId(values), generator.GetDefaultStreamId(values));
    }

    [InlineData(SecondaryAdapterGeneratedStream + "{Bad}", SecondaryDefaultStreamId + "{Bad}")]
    [InlineData(SecondaryAdapterGeneratedStream + "{}", SecondaryDefaultStreamId + "{}")]
    [InlineData(null, SecondaryDefaultStreamId + "{Bad}")]
    [Theory]
    public void SetOrUpdateDefaultStreamIdPattern_BadStreamId(string newAdapterStreamId, string newStreamId)
    {
        var generator = new DefaultStreamIdGenerator();
        generator.SetDefaultStreamIdPattern(AdapterGeneratedStream, _defaultKeywords);
        generator.UpdateDefaultStreamIdPattern(DefaultStreamId);

        Assert.ThrowsAny<ArgumentException>(() => generator.UpdateDefaultStreamIdPattern(newStreamId));
        Assert.ThrowsAny<ArgumentException>(() => generator.SetDefaultStreamIdPattern(newAdapterStreamId, _defaultKeywords));
    }

    [InlineData(DefaultStreamId, "Apple", "Pie", "", "Cow")]
    [InlineData(DefaultStreamId, "Apple", "Pie", "  ", "Cow")]
    [InlineData(DefaultStreamId, "Apple", null, "Trash", "Cow")]
    [Theory]
    public void DefaultStreamIdGenerator_BadKeywords(string streamId, params string[] keywords)
    {
        var generator = new DefaultStreamIdGenerator();
        Assert.ThrowsAny<ArgumentException>(() => generator.SetDefaultStreamIdPattern(streamId, keywords));
    }
}
