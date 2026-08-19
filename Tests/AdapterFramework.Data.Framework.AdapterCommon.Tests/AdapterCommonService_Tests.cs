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
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.DataModel.Enum;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests;

public class AdapterCommonService_Tests
{
    private const string ComponentId = "TestId";
    private const string AdapterType = "TestType";
    private const string TimestampPropertyName = "Timestamp";
    private const string ValuePropertyName = "Value";
    private const string EnumName = "Enum";
    private const string StringName = "string";
    private const string ObjectName = "object";
    private const string DateTimeName = "date-time";

    private readonly AdapterCommonService _service;
    private readonly Mock<IAdapterMessageProcessor> _messageProcessor;

    public AdapterCommonService_Tests()
    {
        _messageProcessor = new Mock<IAdapterMessageProcessor>();
        _service = CreateAdapterCommonService(_messageProcessor);
        _service.DefaultStreamIdGenerator.SetDefaultStreamIdPattern("Goodbye", "None");
    }

    [Fact]
    public void CheckCreatedAdapterCommonServices()
    {
        var logger = new Mock<ILogger>();
        var processor = new Mock<IAdapterMessageProcessor>();
        var protector = new Mock<IEdgeDataProtector>();
        var healthService = new Mock<IHealthService>();

        var service = CreateAdapterCommonService(processor, logger, protector, healthService);

        Assert.Equal(logger.Object, service.Logger);
        Assert.Equal(processor.Object, service.MessageProcessor);
        Assert.Equal(protector.Object, service.DataProtector);
        Assert.Equal(healthService.Object, service.HealthService);

        var newLogger = new Mock<ILogger>();
        var newProcessor = new Mock<IAdapterMessageProcessor>();
        var newProtector = new Mock<IEdgeDataProtector>();
        var newHealthService = new Mock<IHealthService>();

        Assert.NotEqual(newLogger.Object, service.Logger);
        Assert.NotEqual(newProcessor.Object, service.MessageProcessor);
        Assert.NotEqual(newProtector.Object, service.DataProtector);
        Assert.NotEqual(newHealthService.Object, service.HealthService);
    }

    [Fact]
    public void AdapterCommonService_GetAdapterDataTypeId_Test()
    {
        const string typeName = "WeeklySchedule";
        var expectedTypeId = $"{AdapterType}.{typeName}";

        var typeId = _service.GetAdapterDataTypeId(typeName);

        Assert.Equal(expectedTypeId, typeId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void AdapterCommonService_GetAdapterDataTypeId_InvalidInput(string invalidTypeName)
    {
        Assert.ThrowsAny<Exception>(() => _service.GetAdapterDataTypeId(invalidTypeName));
    }

    [Theory]
    [InlineData(typeof(bool))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(byte))]
    [InlineData(typeof(sbyte))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(double))]
    [InlineData(typeof(float))]
    [InlineData(typeof(int))]
    [InlineData(typeof(uint))]
    [InlineData(typeof(long))]
    [InlineData(typeof(ulong))]
    [InlineData(typeof(short))]
    [InlineData(typeof(ushort))]
    [InlineData(typeof(string))]
    public void AdapterCommonService_GetTimeIndexedDataType_SupportedTypes(Type valueType)
    {
        var dataType = _service.GetTimeIndexedDataType(valueType);

        Assert.Equal($"{CommonConstants.TimeIndexedTypeIdPrefix}.{valueType.Name}", dataType.Id);
        Assert.Equal(2, dataType.Properties.Count);
        Assert.Equal(_service.GetTimeIndexedDataTypeId(valueType), dataType.Id);
        Assert.Equal(valueType.Name, dataType.Name);
        Assert.True(dataType.Properties[TimestampPropertyName].IsIndex);
        Assert.Null(dataType.Properties[ValuePropertyName].IsIndex);
    }

    [Theory]
    [InlineData(typeof(bool?))]
    [InlineData(typeof(DateTime?))]
    [InlineData(typeof(byte?))]
    [InlineData(typeof(sbyte?))]
    [InlineData(typeof(decimal?))]
    [InlineData(typeof(double?))]
    [InlineData(typeof(float?))]
    [InlineData(typeof(int?))]
    [InlineData(typeof(uint?))]
    [InlineData(typeof(long?))]
    [InlineData(typeof(ulong?))]
    [InlineData(typeof(short?))]
    [InlineData(typeof(ushort?))]
    public void AdapterCommonService_GetTimeIndexedDataType_SupportedNullableTypes(Type valueType)
    {
        var expectedTypeName = AdapterCommonService.GetTypeName(valueType);
        var dataType = _service.GetTimeIndexedDataType(valueType);

        Assert.Equal($"{CommonConstants.TimeIndexedTypeIdPrefix}.{expectedTypeName}", dataType.Id);
        Assert.Equal(2, dataType.Properties.Count);
        Assert.Equal(_service.GetTimeIndexedDataTypeId(valueType), dataType.Id);
        Assert.Equal(expectedTypeName, dataType.Name);
        Assert.True(dataType.Properties[TimestampPropertyName].IsIndex);
        Assert.Null(dataType.Properties[ValuePropertyName].IsIndex);
    }

    [Theory]
    [InlineData(typeof(char))]
    [InlineData(typeof(object))]
    [InlineData(typeof(object[]))]
    [InlineData(typeof(bool[]))]
    [InlineData(typeof(byte[]))]
    [InlineData(typeof(sbyte[]))]
    [InlineData(typeof(decimal[]))]
    [InlineData(typeof(double[]))]
    [InlineData(typeof(float[]))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(uint[]))]
    [InlineData(typeof(long[]))]
    [InlineData(typeof(ulong[]))]
    [InlineData(typeof(short[]))]
    [InlineData(typeof(ushort[]))]
    [InlineData(typeof(string[]))]
    [InlineData(typeof(DataSourceConfig))]
    public void AdapterCommonService_GetTimeIndexedDataType_UnsupportedTypes(Type valueType)
    {
        Assert.Throws<NotSupportedException>(() => _service.GetTimeIndexedDataType(valueType));
        Assert.Throws<NotSupportedException>(() => _service.GetTimeIndexedDataTypeId(valueType));
    }

    [Fact]
    public void AdapterCommonService_GetTimeIndexedDataType_InvalidInput()
    {
        Assert.Throws<ArgumentNullException>(() => _service.GetTimeIndexedDataType(null));
    }

    [Theory]
    [InlineData("Prefix", "Prefix")]
    [InlineData("  ", "")]
    [InlineData("", "")]
    [InlineData(null, ComponentId + ".")]
    public void AdapterCommonService_StreamPrefixProperties_Test(string streamIdPrefix, string expectedPrefix)
    {
        _service.DataSourceHandler(new DataSourceConfig(streamIdPrefix, null));
        Assert.Equal(expectedPrefix, _service.StreamIdPrefix);
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("  ")]
    [InlineData("")]
    [InlineData(null)]
    public void AdapterCommonService_DefaultStreamIdProperties_Test(string defaultStreamId)
    {
        _service.DataSourceHandler(new DataSourceConfig("Test", defaultStreamId));
        Assert.Equal(defaultStreamId, _service.DefaultStreamIdPattern);
    }

    [Fact]
    public void AdapterCommonService_DefaultStreamIdGenerator_Test()
    {
        _service.DataSourceHandler(new DataSourceConfig("Prefix", "Hello"));
        var generatedString = _service.DefaultStreamIdGenerator.GetDefaultStreamId("Orange");

        _service.DataSourceHandler(new DataSourceConfig("Prefix2", "Bye"));
        var generatedString2 = _service.DefaultStreamIdGenerator.GetDefaultStreamId("orange");
        
        Assert.NotEqual(generatedString, generatedString2);
    }

    [Theory]
    [InlineData("Enum")]
    [InlineData("Enum1")]
    [InlineData("Power")]
    [InlineData("PowerEnum")]
    public void GetEnumTypeId_Test(string enumName)
    {
        var enumTypeId = _service.GetEnumTypeId(enumName);
        
        Assert.EndsWith(EnumName, enumTypeId);
        Assert.StartsWith($"{AdapterType}.", enumTypeId);
        Assert.DoesNotMatch("EnumEnum$", enumTypeId);
    }

    [Fact]
    public void GetEnumDataType_Test()
    {
        var enumField = new TypeEnumField(new List<EnumValueItem> { new("HI", 7) });
        var dataType = _service.GetEnumDataType(enumField, EnumName);

        Assert.Equal(_service.GetEnumTypeId(EnumName), dataType.Id);
        Assert.Null(dataType.Classification);
        Assert.Equal(enumField, dataType.Enum);
    }

    [Fact]
    public void GetEnumDataType_Test_InvalidInput()
    {
        var enumField = new TypeEnumField(new List<EnumValueItem> { new("HI", 7) });

        Assert.Throws<ArgumentNullException>(() => _service.GetEnumDataType(null, EnumName));
        Assert.Throws<ArgumentNullException>(() => _service.GetEnumDataType(null, null));
        Assert.Throws<ArgumentNullException>(() => _service.GetEnumDataType(enumField, null));
    }

    [Theory]
    [InlineData("Enum")]
    [InlineData("Enum1")]
    [InlineData("Power")]
    [InlineData("PowerEnum")]
    public void GetTimeIndexedEnumDataTypeId_Test(string enumName)
    {
        var indexId = _service.GetTimeIndexedEnumDataTypeId(enumName);
        
        Assert.StartsWith(CommonConstants.TimeIndexedTypeIdPrefix, indexId);
        Assert.EndsWith(EnumName, indexId);
    }

    [Fact]
    public void GetTimeIndexedEnumDataType_Test()
    {
        var type = _service.GetTimeIndexedEnumDataType(EnumName);
        var typeId = _service.GetEnumTypeId(EnumName);
        var valueProperty = type.Properties[CommonConstants.ValuePropertyName];
        
        Assert.Equal(typeId, valueProperty.RefTypeId);
        Assert.True(string.IsNullOrEmpty(valueProperty.Type));
    }

    [Theory]
    [InlineData(typeof(bool), "TestQuality", "Boolean", null, OmfVersion.Omf12)]
    [InlineData(typeof(DateTime), "TestQuality", "string", "date-time", OmfVersion.Omf12)]
    [InlineData(typeof(byte), "TestQuality", "integer", "int16", OmfVersion.Omf12)]
    [InlineData(typeof(sbyte), "TestQuality", "integer", "int16", OmfVersion.Omf12)]
    [InlineData(typeof(decimal), "TestQuality", "number", "float64", OmfVersion.Omf12)]
    [InlineData(typeof(double), "TestQuality", "number", "float64", OmfVersion.Omf12)]
    [InlineData(typeof(float), "TestQuality", "number", "float32", OmfVersion.Omf12)]
    [InlineData(typeof(int), "TestQuality", "integer", "int32", OmfVersion.Omf12)]
    [InlineData(typeof(uint), "TestQuality", "integer", "uint32", OmfVersion.Omf12)]
    [InlineData(typeof(long), "TestQuality", "integer", "int64", OmfVersion.Omf12)]
    [InlineData(typeof(ulong), "TestQuality", "integer", "uint64", OmfVersion.Omf12)]
    [InlineData(typeof(short), "TestQuality", "integer", "int16", OmfVersion.Omf12)]
    [InlineData(typeof(ushort), "TestQuality", "integer", "uint16", OmfVersion.Omf12)]
    [InlineData(typeof(string), "TestQuality", "string", null, OmfVersion.Omf12)]
    [InlineData(typeof(bool), "TestQuality", "Boolean", null, OmfVersion.Omf20)]
    [InlineData(typeof(DateTime), "TestQuality", "string", "date-time", OmfVersion.Omf20)]
    [InlineData(typeof(byte), "TestQuality", "integer", "int16", OmfVersion.Omf20)]
    [InlineData(typeof(sbyte), "TestQuality", "integer", "int16", OmfVersion.Omf20)]
    [InlineData(typeof(decimal), "TestQuality", "number", "float64", OmfVersion.Omf20)]
    [InlineData(typeof(double), "TestQuality", "number", "float64", OmfVersion.Omf20)]
    [InlineData(typeof(float), "TestQuality", "number", "float32", OmfVersion.Omf20)]
    [InlineData(typeof(int), "TestQuality", "integer", "int32", OmfVersion.Omf20)]
    [InlineData(typeof(uint), "TestQuality", "integer", "uint32", OmfVersion.Omf20)]
    [InlineData(typeof(long), "TestQuality", "integer", "int64", OmfVersion.Omf20)]
    [InlineData(typeof(ulong), "TestQuality", "integer", "uint64", OmfVersion.Omf20)]
    [InlineData(typeof(short), "TestQuality", "integer", "int16", OmfVersion.Omf20)]
    [InlineData(typeof(ushort), "TestQuality", "integer", "uint16", OmfVersion.Omf20)]
    [InlineData(typeof(string), "TestQuality", "string", null, OmfVersion.Omf20)]
    public void GetTimeIndexedEnumDataType_WithQuality_Test(Type dataQualityType, string qualitySchema, string expectedType, string expectedFormat, OmfVersion omfVersion)
    {
        _messageProcessor.Setup(mp => mp.OmfVersion).Returns(omfVersion);

        var type = _service.GetTimeIndexedEnumDataType(EnumName, dataQualityType, qualitySchema);
        var typeId = _service.GetEnumTypeId(EnumName);

        Assert.Equal(ObjectName, type.Type, ignoreCase: true);
        Assert.Equal($"TimeIndexed.{typeId}.{qualitySchema}Quality", type.Id);
        Assert.Equal(3, type.Properties.Count);
        
        AssertTimestampProperty(type.Properties[TimestampPropertyName]);
        
        var qualityProperty = type.Properties[CommonConstants.QualityPropertyName];
        Assert.NotNull(qualityProperty);
        Assert.Null(qualityProperty.IsIndex);
        Assert.Equal(expectedType, qualityProperty.Type, ignoreCase: true);
        Assert.Equal(expectedFormat, qualityProperty.Format);
        
        AssertEnumValueProperty(type.Properties[ValuePropertyName], typeId);

        if (omfVersion == OmfVersion.Omf12)
        {
            Assert.True(qualityProperty.IsQuality);
            Assert.Single(type.Metadata);
            Assert.Equal(qualitySchema, type.Metadata[CommonConstants.DataQualitySchemaName]);
        }
        else
        {
            Assert.Equal(qualitySchema, qualityProperty.QualitySchema);
            Assert.Null(type.Metadata);
        }
    }

    [Theory]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(DateTime), "DateTime")]
    [InlineData(typeof(byte), "byte")]
    [InlineData(typeof(sbyte), "sbyte")]
    [InlineData(typeof(decimal), "decimal")]
    [InlineData(typeof(double), "double")]
    [InlineData(typeof(float), "single")]
    [InlineData(typeof(int), "int32")]
    [InlineData(typeof(uint), "uint32")]
    [InlineData(typeof(long), "int64")]
    [InlineData(typeof(ulong), "uint64")]
    [InlineData(typeof(short), "int16")]
    [InlineData(typeof(ushort), "uint16")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(bool?), "NullableBoolean")]
    [InlineData(typeof(DateTime?), "NullableDateTime")]
    [InlineData(typeof(byte?), "NullableByte")]
    [InlineData(typeof(sbyte?), "Nullablesbyte")]
    [InlineData(typeof(decimal?), "NullableDecimal")]
    [InlineData(typeof(double?), "NullableDouble")]
    [InlineData(typeof(float?), "NullableSingle")]
    [InlineData(typeof(int?), "NullableInt32")]
    [InlineData(typeof(uint?), "NullableUint32")]
    [InlineData(typeof(long?), "NullableInt64")]
    [InlineData(typeof(ulong?), "NullableUint64")]
    [InlineData(typeof(short?), "NullableInt16")]
    [InlineData(typeof(ushort?), "NullableUint16")]
    public void GetTypeName(Type type, string expectedTypeName)
    {
        var typeName = AdapterCommonService.GetTypeName(type);
        Assert.Equal(expectedTypeName, typeName, ignoreCase: true);
    }

    private static AdapterCommonService CreateAdapterCommonService(
        Mock<IAdapterMessageProcessor> processor = null,
        Mock<ILogger> logger = null,
        Mock<IEdgeDataProtector> protector = null,
        Mock<IHealthService> healthService = null)
    {
        return new AdapterCommonService(
            (logger ?? new Mock<ILogger>()).Object,
            new Mock<IConfigurationProvider>().Object,
            (processor ?? new Mock<IAdapterMessageProcessor>()).Object,
            (protector ?? new Mock<IEdgeDataProtector>()).Object,
            AdapterType,
            ComponentId,
            (healthService ?? new Mock<IHealthService>()).Object);
    }

    private static void AssertTimestampProperty(PropertyDefinition property)
    {
        Assert.NotNull(property);
        Assert.True(property.IsIndex);
        Assert.Null(property.IsQuality);
        Assert.Equal(StringName, property.Type, ignoreCase: true);
        Assert.Equal(DateTimeName, property.Format, ignoreCase: true);
    }

    private static void AssertQualityProperty(PropertyDefinition property, string expectedType, string expectedFormat, bool isQuality)
    {
        Assert.NotNull(property);
        Assert.Null(property.IsIndex);
        Assert.Equal(isQuality, property.IsQuality);
        Assert.Equal(expectedType, property.Type, ignoreCase: true);
        Assert.Equal(expectedFormat, property.Format);
    }

    private static void AssertEnumValueProperty(PropertyDefinition property, string expectedRefTypeId)
    {
        Assert.NotNull(property);
        Assert.Null(property.IsIndex);
        Assert.Null(property.IsQuality);
        Assert.Equal(expectedRefTypeId, property.RefTypeId);
        Assert.True(string.IsNullOrEmpty(property.Type));
        Assert.True(string.IsNullOrEmpty(property.Format));
    }

    private class DataSourceConfig : EdgeConfigurationBase, IDataSourceConfiguration
    {
        public DataSourceConfig(string idPrefix, string defaultStreamId)
        {
            StreamIdPrefix = idPrefix;
            DefaultStreamIdPattern = defaultStreamId;
        }

        public string StreamIdPrefix { get; }
        public string DefaultStreamIdPattern { get; }

        public override IEnumerable<ValidationResult> Validate()
        {
            yield return ValidationResult.Success;
        }
    }
}
