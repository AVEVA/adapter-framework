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
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.ConfigurationProvider.Converters;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests.Converters;

public class JsonStringEnumConverterFactory_Tests
{
    public static readonly TheoryData<Type, bool> CanConvertData = new()
    {
        { typeof(LogLevel), true },
        { typeof(int), false },
        { typeof(string), false },
        { typeof(bool), false },
        { typeof(long), false },
        { typeof(double), false },
        { typeof(LoggerConfiguration), false },
    };

    [Theory]
    [MemberData(nameof(CanConvertData))]
    public void EnumConverterFactory_CanConvert_Test(Type type, bool canCovert)
    {
        var factory = new JsonStringEnumConverterFactory();

        Assert.Equal(canCovert, factory.CanConvert(type));
    }

    [Fact]
    public void EnumConverterFactory_CreateConverter_Test()
    {
        var factory = new JsonStringEnumConverterFactory();

        Assert.NotNull(factory.CreateConverter(typeof(LogLevel), new JsonSerializerOptions()));
    }
}
