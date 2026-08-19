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
using System.Globalization;
using System.Text.Json;
using AdapterFramework.Data.Framework.ConfigurationProvider.Converters;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests.Converters;

public class StringToTimeSpanConverter_Tests
{
    public static readonly TheoryData<TimeSpan?> MemberData = new TheoryData<TimeSpan?>
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(50.342),
        TimeSpan.FromDays(1.234),
        TimeSpan.FromTicks(99999),
        TimeSpan.FromSeconds(-1),
        null,
    };

    private readonly JsonSerializerOptions _serializerOptions;

    public StringToTimeSpanConverter_Tests() => _serializerOptions = new JsonSerializerOptions { Converters = { new StringToTimeSpanConverter() } };

    [MemberData(nameof(MemberData))]
    [Theory]
    public void StringToTimeSpanConverter_ConvertsTimeSpan(TimeSpan? timeSpan)
    {
        var stringValue = timeSpan == null ? "null" : $"\"{timeSpan}\"";

        var deserializedValue = JsonSerializer.Deserialize<TimeSpan?>(stringValue, _serializerOptions);
        Assert.Equal(timeSpan, deserializedValue);
    }

    [Theory]
    [InlineData(4242)]
    [InlineData(0.5)]
    [InlineData(0.001)]
    [InlineData(-4242)]
    public void StringToTimeSpanConverter_Converts_Number_ToSeconds(float number)
    {
        var expectedTimespan = TimeSpan.FromSeconds(number);

        var deserializedValue = JsonSerializer.Deserialize<TimeSpan>(number.ToString(CultureInfo.CurrentCulture), _serializerOptions);

        Assert.Equal(expectedTimespan, deserializedValue);
    }

    [Theory]
    [InlineData("{\"TestProperty\":\"4242\"}")]
    [InlineData("{\"TestProperty\":\"0.5\"}")]
    [InlineData("{\"TestProperty\":\"0.001\"}")]
    [InlineData("{\"TestProperty\":\"-4242\"}")]
    public void StringToTimeSpanConverter_Converts_StringNumber_ToSeconds(string json)
    {
        var singleValue = JsonSerializer.Deserialize<TestSingle>(json, new JsonSerializerOptions { Converters = { new StringToFloatConverter() } });

        var expectedTimespan = TimeSpan.FromSeconds(singleValue.TestProperty);
        
        var deserializedValue = JsonSerializer.Deserialize<TestTimeSpan>(json, _serializerOptions);

        Assert.Equal(expectedTimespan, deserializedValue.TestProperty);
    }

    [Fact]
    public void StringToTimeSpanConverter_Converts_InvalidInput()
    {
        Assert.Throws<OverflowException>(() => JsonSerializer.Deserialize<TestTimeSpan>("{\"TestProperty\":\"999999999999999999\"}", _serializerOptions));
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<TestTimeSpan>("{\"TestProperty\":\"NotTimeSpan\"}", _serializerOptions));
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<TestTimeSpan>("{\"TestProperty\":\"99l5\"}", _serializerOptions));
    }

    private sealed class TestTimeSpan
    {
        public TimeSpan TestProperty { get; set; }
    }

    private sealed class TestSingle
    {
        public float TestProperty { get; set; }
    }
}
