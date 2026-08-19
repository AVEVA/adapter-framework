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
using Xunit;

namespace AdapterFramework.Data.DataModel.Tests;

public class DynamicDataType_Tests
{
    private const string TimestampPropertyName = "Timestamp";
    private const string TemperatureQualityPropertyName = "TemperatureQuality";
    private const string PressurePropertyName = "Pressure";
    private const string TemperaturePropertyName = "Temperature";
    private const string DateTimePropertyName = "ValueTime";
    private const string QualityPropertyName = "Quality";
    private const string Quality2PropertyName = "Quality2";
    private const string TypeId = "typeId";
    private const string TypeName = "typeName";

    [Fact]
    public void DynamicDataType_Constructor_ValidDictionary()
    {
        var dictionary = new Dictionary<string, (Type, bool, bool, string)>
        {
            [TimestampPropertyName] = (typeof(DateTime), true, false, null),
            [PressurePropertyName] = (typeof(float), false, false, null),
            [TemperaturePropertyName] = (typeof(int), false, false, null),
            [DateTimePropertyName] = (typeof(DateTime), false, false, null),
        };

        var dataType = new DynamicDataType(TypeId, TypeName, dictionary);

        Assert.Equal(TypeId, dataType.Id);
        Assert.Equal(TypeName, dataType.Name);
        Assert.True(dataType.Properties.Count == dictionary.Count);

        Assert.True(dataType.Properties.ContainsKey(TimestampPropertyName));
        Assert.True(dataType.Properties[TimestampPropertyName].IsIndex);
        Assert.True(dataType.Properties.ContainsKey(PressurePropertyName));
        Assert.Null(dataType.Properties[PressurePropertyName].IsIndex);
        Assert.True(dataType.Properties.ContainsKey(TemperaturePropertyName));
        Assert.Null(dataType.Properties[TemperaturePropertyName].IsIndex);
        Assert.True(dataType.Properties.ContainsKey(DateTimePropertyName));
        Assert.Null(dataType.Properties[DateTimePropertyName].IsIndex);
    }

    [Fact]
    public void DynamicDataType_Constructor_NoIndex()
    {
        var dictionary = new Dictionary<string, (Type, bool, bool, string)>
        {
            [PressurePropertyName] = (typeof(float), false, false, null),
            [TemperaturePropertyName] = (typeof(int), false, false, null),
        };

        Assert.Throws<InvalidOperationException>(() => new DynamicDataType(TypeId, TypeName, dictionary));
    }

    [Theory]
    [InlineData(true, null, OmfVersion.Omf12)]
    [InlineData(false, "TestQuality", OmfVersion.Omf20)]
    [InlineData(true, "TestQuality", OmfVersion.Omf20)]
    public void DynamicDataType_Constructor_Check_Quality_Gets_Set(bool isQuality, string qualitySchema, OmfVersion omfVersion)
    {
        var dictionary = new Dictionary<string, (Type, bool, bool, string)>
        {
            [TemperaturePropertyName] = (typeof(int), true, false, null),
            [TemperatureQualityPropertyName] = (typeof(int), false, isQuality, qualitySchema),
        };

        var dataType = new DynamicDataType(TypeId, TypeName, dictionary, omfVersion);

        if (omfVersion == OmfVersion.Omf20)
        {
            Assert.Null(dataType.Properties[TemperatureQualityPropertyName].IsQuality);
            Assert.Equal(qualitySchema, dataType.Properties[TemperatureQualityPropertyName].QualitySchema);
        }
        else
        {
            Assert.True(dataType.Properties[TemperatureQualityPropertyName].IsQuality);
            Assert.Null(dataType.Properties[TemperatureQualityPropertyName].QualitySchema);
        }
    }

    [Fact]
    public void DynamicDataType_Constructor_Fail_If_IsIndex_And_IsQuality()
    {
        var dictionary = new Dictionary<string, (Type, bool, bool, string)>
        {
            [TemperaturePropertyName] = (typeof(int), false, false, null),
            [TemperatureQualityPropertyName] = (typeof(int), true, true, null),
        };

        Assert.Throws<NotSupportedException>(() => new DynamicDataType(TypeId, TypeName, dictionary));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData("test", null)]
    public void DynamicDataType_Constructor_InvalidInput(string typeId, Dictionary<string, (Type, bool, bool, string)> properties)
    {
        Assert.ThrowsAny<Exception>(() => new DynamicDataType(typeId, TypeName, properties));
    }

    [Fact]
    public void DynamicDataType_Constructor_Allows_Multiple_Quality_Properties()
    {
        var dictionary = new Dictionary<string, (Type DataType, bool IsIndex, bool IsQuality, string QualitySchema)>
        {
            [PressurePropertyName] = (typeof(float), true, false, null),
            [QualityPropertyName] = (typeof(int), false, true, null),
            [Quality2PropertyName] = (typeof(int), false, true, null),
        };

        Assert.True(dictionary[QualityPropertyName].IsQuality);
        Assert.True(dictionary[Quality2PropertyName].IsQuality);
    }
}
