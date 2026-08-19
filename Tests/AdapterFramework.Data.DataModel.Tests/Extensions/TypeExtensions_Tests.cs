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
using System.Reflection.Metadata;
using AdapterFramework.Data.DataModel.Extensions;
using Xunit;

namespace AdapterFramework.Data.DataModel.Tests.Extensions;

public class TypeExtensions_Tests
{
    [Theory]
    [InlineData(typeof(bool?), "boolean")]
    [InlineData(typeof(DateTime?), "string")]
    [InlineData(typeof(byte?), "integer")]
    [InlineData(typeof(sbyte?), "integer")]
    [InlineData(typeof(decimal?), "number")]
    [InlineData(typeof(double?), "number")]
    [InlineData(typeof(float?), "number")]
    [InlineData(typeof(int?), "integer")]
    [InlineData(typeof(uint?), "integer")]
    [InlineData(typeof(long?), "integer")]
    [InlineData(typeof(ulong?), "integer")]
    [InlineData(typeof(short?), "integer")]
    [InlineData(typeof(ushort?), "integer")]
    public void ToPropertyDefinition_NullableTypes(Type type, string propertyType)
    {
        var nullableProperty = (NullablePropertyDefinition)type.ToPropertyDefinition();
        Assert.Contains("null", nullableProperty.Type);
        Assert.Contains(propertyType, nullableProperty.Type);
    }

    [Theory]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(DateTime), "string")]
    [InlineData(typeof(byte), "integer")]
    [InlineData(typeof(sbyte), "integer")]
    [InlineData(typeof(decimal), "number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(uint), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(ulong), "integer")]
    [InlineData(typeof(short), "integer")]
    [InlineData(typeof(ushort), "integer")]
    [InlineData(typeof(string), "string")]
    public void ToPropertyDefinition(Type type, string propertyType)
    {
        var propertyDefinition = type.ToPropertyDefinition();
        Assert.True(propertyType.Equals(propertyDefinition.Type, StringComparison.OrdinalIgnoreCase), $"Expected Type: {propertyType}, Actual: {propertyDefinition.Type}");
    }
}
