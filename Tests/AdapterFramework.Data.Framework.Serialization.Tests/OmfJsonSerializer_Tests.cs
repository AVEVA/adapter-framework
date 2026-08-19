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
using System.Text;
using System.Text.Json;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.DataModel.Enum;
using Xunit;

namespace AdapterFramework.Data.Framework.Serialization.Tests;

public class OmfJsonSerializer_Tests
{
    [Fact]
    public void OmfJsonSerializer_Serialize_Deserialize_String_Test()
    {
        var expected = "test";
        var jsonSerializer = new OmfJsonSerializer();

        var serializedValue = jsonSerializer.Serialize("test");
        var actual = jsonSerializer.Deserialize<string>(serializedValue);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(short.MinValue)]
    [InlineData(short.MaxValue)]
    [InlineData(ushort.MinValue)]
    [InlineData(ushort.MaxValue)]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(byte.MinValue)]
    [InlineData(byte.MaxValue)]
    [InlineData(sbyte.MinValue)]
    [InlineData(sbyte.MaxValue)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(uint.MaxValue)]
    [InlineData(uint.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(ulong.MaxValue)]
    [InlineData(ulong.MinValue)]
    [InlineData(double.NaN)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(float.NaN)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.PositiveInfinity)]
    public void OmfJsonSerializer_Serialize_Deserialize_VariousDataTypes_Test<T>(T value)
    {
        var jsonSerializer = new OmfJsonSerializer();

        var serializedValue = jsonSerializer.Serialize(value);
        var actual = jsonSerializer.Deserialize<T>(serializedValue);

        Assert.Equal(value, actual);
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Deserialize_IntegerArray_Test()
    {
        int[] expected = { 1 };
        var jsonSerializer = new OmfJsonSerializer();

        var serializedValue = jsonSerializer.Serialize(expected);
        var actual = jsonSerializer.Deserialize<int[]>(serializedValue);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Deserialize_ByteArray_Test()
    {
        byte[] expected = { 1 };
        var jsonSerializer = new OmfJsonSerializer();

        var serializedValue = jsonSerializer.Serialize(expected);
        var actual = jsonSerializer.Deserialize<byte[]>(serializedValue);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Deserialize_ComplexClass_Test()
    {
        var expected = new TestClass() { IntValue = 1, FloatValue = 1.2f, StringArrayValue = new[] { "a", "b" } };
        var jsonSerializer = new OmfJsonSerializer();

        var serialized = jsonSerializer.Serialize(expected);
        var actual = jsonSerializer.Deserialize<TestClass>(serialized);

        Assert.Equal(expected.IntValue, actual.IntValue);
        Assert.Equal(expected.FloatValue, actual.FloatValue);
        Assert.Equal(expected.StringArrayValue.Length, expected.StringArrayValue.Length);

        for (var i = 0; i < expected.StringArrayValue.Length; i++)
        {
            Assert.Equal(expected.StringArrayValue[i], actual.StringArrayValue[i]);
        }
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Deserialize_NullableType_Test()
    {
        var expected = "{\"id\":\"testType\",\"classification\":\"dynamic\",\"type\":\"object\",\"name\":\"test\"," +
            "\"properties\":{\"Timestamp\":{\"type\":\"string\",\"format\":\"date-time\",\"isindex\":true}," +
            "\"Pressure\":{\"type\":[\"number\",\"null\"],\"format\":\"float32\"}," +
            "\"Temperature\":{\"type\":[\"integer\",\"null\"],\"format\":\"int32\"}}}";

        var properties = new Dictionary<string, (Type, bool, bool, string)>
        {
            ["Timestamp"] = (typeof(DateTime), true, false, null),
            ["Pressure"] = (typeof(float?), false, false, null),
            ["Temperature"] = (typeof(int?), false, false, null),
        };

        var dynamicType = new DynamicDataType("testType", "test", properties);
        var jsonSerializer = new OmfJsonSerializer();
        var serialized = jsonSerializer.Serialize(dynamicType);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Deserialize_ComplexClassObject_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var myTestClass = new TestClass { IntValue = 1, FloatValue = 1.2f, StringArrayValue = new[] { "a", "b" } };
        var serialized = jsonSerializer.Serialize(myTestClass);
        var deserialized = jsonSerializer.Deserialize(serialized);
        var converted = JsonSerializer.Deserialize<TestClass>(((JsonElement)deserialized).GetRawText());

        Assert.NotNull(converted);
        Assert.Equal(myTestClass.IntValue, converted.IntValue);
        Assert.Equal(myTestClass.FloatValue, converted.FloatValue);
        Assert.Equal(myTestClass.StringArrayValue.Length, converted.StringArrayValue.Length);

        for (var i = 0; i < myTestClass.StringArrayValue.Length; i++)
        {
            Assert.Equal(myTestClass.StringArrayValue[i], converted.StringArrayValue[i]);
        }
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Static_Type_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var expected = "{\"id\":\"Hello\",\"classification\":\"static\",\"type\":\"object\",\"description\":\"Description\"," +
               "\"extrapolation\":\"Forward\",\"properties\":{\"someProperty\":{\"isindex\":true," +
               "\"interpolation\":\"Continuous\"}}}";

        var type = new StaticDataType()
        {
            Id = "Hello",
            Description = "Description",
            Properties = new Dictionary<string, PropertyDefinition>()
            {
                {
                    "someProperty",
                    new PropertyDefinition() { IsIndex = true, Interpolation = Interpolation.Continuous, }
                },
            },
            Extrapolation = Extrapolation.Forward,
        };

        var serialized = jsonSerializer.Serialize(type);

        Assert.NotNull(serialized);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Dynamic_Type_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var expected = "{\"id\":\"Hello\",\"classification\":\"dynamic\",\"type\":\"object\",\"description\":\"Description\"" +
               ",\"extrapolation\":\"Forward\",\"properties\":{\"someProperty\":{\"isindex\":true," +
               "\"interpolation\":\"Continuous\"}}}";

        var type = new DynamicDataType
        {
            Id = "Hello",
            Description = "Description",
            Properties = new Dictionary<string, PropertyDefinition>()
            {
                {
                    "someProperty",
                    new PropertyDefinition() { IsIndex = true, Interpolation = Interpolation.Continuous, }
                },
            },
            Extrapolation = Extrapolation.Forward,
        };

        var serialized = jsonSerializer.Serialize(type);

        Assert.NotNull(serialized);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Enum_Type_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var expected = "{\"id\":\"EnumId\",\"name\":\"Boolean\",\"enum\":{\"type\":\"integer\",\"format\":\"int16\"" +
                       ",\"values\":[{\"name\":\"False\",\"value\":0},{\"name\":\"True\",\"value\":1}]}}";

        var enumValues = new List<EnumValueItem>
        {
            new("False", 0),
            new("True", 1),
        };

        var type = new EnumDataType("EnumId", new TypeEnumField(enumValues, typeof(short)), "Boolean");

        var serialized = jsonSerializer.Serialize(type);

        Assert.NotNull(serialized);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Container_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var expected = "{\"id\":\"NewStream\",\"typeid\":\"Hello\",\"datasource\":\"DS\",\"extrapolation\":\"Forward\"" +
                       ",\"propertyoverrides\":{\"someProperty\":{\"name\":\"newName\"}}}";

        var container = new DataStream()
        {
            TypeId = "Hello",
            Id = "NewStream",
            DataSource = "DS",
            PropertyOverrides = new Dictionary<string, PropertyDefinitionOverride>()
            {
                {
                    "someProperty",
                    new PropertyDefinitionOverride { Name = "newName", }
                },
            },
            Extrapolation = Extrapolation.Forward,
        };

        var serialized = jsonSerializer.Serialize(container);

        Assert.NotNull(serialized);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }

    [Theory]
    [InlineData(Classification.Dynamic, "{\"containerid\":\"HelloWorld\",\"values\":[0,1,2]}")]
    [InlineData(Classification.Static, "{\"typeid\":\"HelloWorld\",\"values\":[0,1,2]}")]
    public void OmfJsonSerializer_Serialize_Data_Test(Classification classification, string expectedOutput)
    {
        byte[] output;
        var jsonSerializer = new OmfJsonSerializer();

        if (classification == Classification.Dynamic)
        {
            var dynamicData = new DynamicStreamData { Id = "HelloWorld", Values = new List<object> { 0, 1, 2 }, };
            output = jsonSerializer.Serialize(dynamicData);
        }
        else
        {
            var staticData = new StaticStreamData { Id = "HelloWorld", Values = new List<object> { 0, 1, 2 }, };
            output = jsonSerializer.Serialize(staticData);

            var deserialized = jsonSerializer.Deserialize(output);
        }

        Assert.NotNull(output);
        Assert.Equal(expectedOutput, Encoding.UTF8.GetString(output));
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Asset_To_Asset_Link_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var expected = "{\"source\":{\"typeid\":\"Parent\",\"index\":\"ParentInstance\"},\"target\":{\"typeid\":\"Child\",\"index\":\"ChildInstance\"}}";
        var source = new DataTypeLinkNode("Parent", "ParentInstance");
        var target = new DataTypeLinkNode("Child", "ChildInstance");

        var link = new Link(source, target);

        var serialized = jsonSerializer.Serialize(link);

        Assert.NotNull(serialized);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Stream_Reference_Link_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var expected = "{\"source\":{\"typeid\":\"Parent\",\"index\":\"ParentInstance\"},\"target\":{\"containerid\":\"HelloWorld\"}}";
        var source = new DataTypeLinkNode("Parent", "ParentInstance");
        var target = new DataStreamLinkNode("HelloWorld");

        var link = new Link(source, target);

        var serialized = jsonSerializer.Serialize(link);

        Assert.NotNull(serialized);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }

    [Fact]
    public void OmfJsonSerializer_Serialize_Entity_To_Entity_Link_Test()
    {
        var jsonSerializer = new OmfJsonSerializer();
        var expected = "{\"source\":{\"typeid\":\"Parent\",\"id\":\"ParentInstance\"},\"target\":{\"typeid\":\"Child\",\"id\":\"ChildInstance\"}}";
        var source = new RelationshipLinkNode("ParentInstance", "Parent");
        var target = new RelationshipLinkNode("ChildInstance", "Child");

        var link = new Link(source, target);

        var serialized = jsonSerializer.Serialize(link);

        Assert.NotNull(serialized);
        Assert.Equal(expected, Encoding.UTF8.GetString(serialized));
    }
}
