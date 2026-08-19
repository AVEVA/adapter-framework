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
using AdapterFramework.Data.Framework.AdapterCommon.Configuration;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Configuration;

public class StreamIdPatternValidator_Tests
{
    private const string DefaultPattern = "{hello}.{world}";
    private readonly string[] _keywords = { "hello", "world" };

    [Theory]
    [InlineData("hello.{WORLD}")]
    [InlineData("{hello}.WORLD")]
    [InlineData("{heLLO}.{WORLD}")]
    [InlineData("{hello}.{hello}.{world}")]
    public void StreamIdPatternValidator_IsValidDefaultStreamIdPattern_ValidInput(string input)
    {
        Assert.True(StreamIdPatternValidator.IsValidDefaultStreamIdPattern(input, DefaultPattern, _keywords, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{hello}.{unknown}.{world}")]
    [InlineData("hello.unknown.world")]
    [InlineData("{hello.unknown.world")]
    [InlineData("{hello.unknown}.world}")]
    public void StreamIdPatternValidator_IsValidDefaultStreamIdPattern_InvalidInput(string input)
    {
        Assert.False(StreamIdPatternValidator.IsValidDefaultStreamIdPattern(input, DefaultPattern, _keywords, out var error));
        Assert.NotEmpty(error);
    }
}
