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
using System.Text.Json;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests.Converters;

public class StringToPrimitiveTypeConverter_Tests
{
    private readonly JsonSerializerOptions _serializerOptions;

    public StringToPrimitiveTypeConverter_Tests()
    {
        _serializerOptions = ConfigurationCommandHelper.SerializerOptions;
    }

    private enum Weekdays
    {
        Monday,
        Tuesday,
        Wednesday,
        Thursday,
        Friday,
    }

    [InlineData(true)]
    [InlineData(false)]
    [Theory]
    public void Converter_ConvertsBool(bool boolean)
    {
        var str = "\"" + boolean + "\"";
        var retVal = JsonSerializer.Deserialize<bool>(str, _serializerOptions);
        Assert.Equal(boolean, retVal);
    }

    [Theory]
    [MemberData(nameof(InternalNumberHandling.TestData), MemberType = typeof(InternalNumberHandling))]
    public void NumberHandling_ToNumericType_Success(Type type, object number, JsonSerializerOptions serializerOptions)
    {
        var str = "\"" + number + "\"";

        var actual = JsonSerializer.Deserialize(str, type, serializerOptions);

        Assert.Equal(number, actual);
    }

    [Fact]
    public void Converter_ConvertsEnum()
    {
        var str = "\"" + 1 + "\"";
        var retVal = JsonSerializer.Deserialize<Weekdays>(str, _serializerOptions);
        Assert.Equal(Weekdays.Tuesday, retVal);

        str = "\"" + Weekdays.Tuesday + "\"";
        retVal = JsonSerializer.Deserialize<Weekdays>(str, _serializerOptions);
        Assert.Equal(Weekdays.Tuesday, retVal);
    }

    private class InternalNumberHandling
    {
        public static IEnumerable<object[]> TestData
        {
            get
            {
                // byte
                yield return new object[] { typeof(byte), (byte)0, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(byte), (byte)100, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(byte), (byte)0, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(byte), (byte)100, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // decimal
                yield return new object[] { typeof(decimal), 0M, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(decimal), 100M, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(decimal), 85.85M, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(decimal), -8327.93M, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(decimal), 0M, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(decimal), 100M, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(decimal), 85.85M, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(decimal), -8327.93M, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // double
                yield return new object[] { typeof(double), 0D, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(double), 100D, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(double), 85.85D, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(double), -8327.93D, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(double), 0D, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(double), 100D, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(double), 85.85D, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(double), -8327.93D, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // float
                yield return new object[] { typeof(float), 0F, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(float), 100F, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(float), 85.85F, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(float), -8327.93F, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(float), 0F, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(float), 100F, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(float), 85.85F, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(float), -8327.93F, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // int
                yield return new object[] { typeof(int), 0, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(int), 100, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(int), -1000, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(int), 0, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(int), 100, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(int), -1000, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // long
                yield return new object[] { typeof(long), 0L, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(long), 100L, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(long), -1000L, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(long), 0L, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(long), 100L, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(long), -1000L, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // sbyte
                yield return new object[] { typeof(sbyte), (sbyte)0, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(sbyte), (sbyte)100, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(sbyte), (sbyte)0, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(sbyte), (sbyte)100, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // short
                yield return new object[] { typeof(short), (short)0, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(short), (short)100, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(short), (short)-1000, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(short), (short)0, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(short), (short)100, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(short), (short)-1000, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // ulong
                yield return new object[] { typeof(ulong), 0UL, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(ulong), 100UL, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(ulong), 1000UL, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(ulong), 0UL, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(ulong), 100UL, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(ulong), 1000UL, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // ushort
                yield return new object[] { typeof(ushort), (ushort)0, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(ushort), (ushort)100, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(ushort), (ushort)0, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(ushort), (ushort)100, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };

                // uint
                yield return new object[] { typeof(uint), 0U, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(uint), 100U, ConfigurationCommandHelper.SerializerOptions };
                yield return new object[] { typeof(uint), 1000U, ConfigurationCommandHelper.SerializerOptions };

                yield return new object[] { typeof(uint), 0U, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(uint), 100U, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
                yield return new object[] { typeof(uint), 1000U, ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter };
            }
        }
    }
}
