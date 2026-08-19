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
using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdapterFramework.Data.Framework.ConfigurationProvider.Converters;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests.Converters;

public class FlagEnumConverter_Tests
{
    public static readonly TheoryData<Colors> MemberData = new TheoryData<Colors>
    {
        Colors.None,
        Colors.None | Colors.Black,
        Colors.Black | Colors.White,
        Colors.None | Colors.All,
        Colors.Red | Colors.Black | Colors.White | Colors.All,
    };

    private readonly JsonSerializerOptions _serializerOptions;

    public FlagEnumConverter_Tests() => _serializerOptions = new JsonSerializerOptions { Converters = { new FlagEnumConverter<Colors>(null) } };

    [Flags]
    public enum Colors
    {
        None = 0,
        Black = 1,
        White = 2,
        Red = 4,
        All = 0xFFFF,
    }

    [Theory]
    [MemberData(nameof(MemberData))]
    public void StringToFlagEnum_Converts(Colors colors)
    {
        var jsonStr = "[";
        foreach (var color in Enum.GetValues(typeof(Colors)))
        {
            if (colors.HasFlag((Colors)color))
            {
                jsonStr += "\"" + color.ToString() + "\",";
            }
        }

        jsonStr = jsonStr.TrimEnd(',');
        jsonStr += "]";

        var deserializedValue = JsonSerializer.Deserialize<Colors>(jsonStr, _serializerOptions);
        Assert.Equal(colors, deserializedValue);
    }

    [Theory]
    [MemberData(nameof(MemberData))]
    public void FlagEnumToString_Converts(Colors colors)
    {
        var jsonStr = "[" + Environment.NewLine;

        if (colors == Colors.None || colors.HasFlag(Colors.All))
        {
            jsonStr += "\"" + colors.ToString() + "\",";
        }
        else
        {
            foreach (var colorObj in Enum.GetValues(typeof(Colors)))
            {
                var color = (Colors)colorObj;
                if (colors.HasFlag(color) && !(color == Colors.None || color == Colors.All))
                {
                    jsonStr += "\"" + color.ToString() + "\",";
                }
            }
        }

        jsonStr = jsonStr.TrimEnd(',');
        jsonStr += "]";

        var serializedString = JsonSerializer.Serialize(colors, _serializerOptions);
        serializedString = Regex.Replace(serializedString, @"\s+", "");
        jsonStr = Regex.Replace(jsonStr, @"\s+", "");
        Assert.Equal(jsonStr, serializedString);
    }

    [Fact]
    public void StringTFlagEnum_Converts_InvalidInput()
    {
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<Colors>("{}", _serializerOptions));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<Colors>("\"a\"", _serializerOptions));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<Colors>("[\"Any\"}", _serializerOptions));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<Colors>("[\"None\", \"Fruit\"]", _serializerOptions));
    }

    [Fact]
    public void DeserializeNullParameter_NotNull()
    {
        Func<Colors> nullIsBlack = () => Colors.Black;

#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        var serializerOptions = new JsonSerializerOptions { Converters = { new FlagEnumConverter<Colors>(nullIsBlack) } };
#pragma warning restore CA1869 // Cache and reuse 'JsonSerializerOptions' instances

        var color = JsonSerializer.Deserialize<Colors>("null", serializerOptions);

        Assert.Equal(Colors.Black, color);
    }

    [Fact]
    public void DeserializeNullParameter_Null()
    {
#pragma warning disable CA1869 // Cache and reuse 'JsonSerializerOptions' instances
        var serializerOptions = new JsonSerializerOptions { Converters = { new FlagEnumConverter<Colors>(null) } };
#pragma warning restore CA1869 // Cache and reuse 'JsonSerializerOptions' instances

        var color = JsonSerializer.Deserialize<Colors>("null", serializerOptions);

        Assert.Equal(default, color);
    }
}
