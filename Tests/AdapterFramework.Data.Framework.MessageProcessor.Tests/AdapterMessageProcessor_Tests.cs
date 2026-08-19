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
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.MessageProcessor.DataFilters;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.MessageProcessor.Tests;

public class AdapterMessageProcessor_Tests
{
    private const string ExistingDataFilterId = "ExistingDataFilter";
    private readonly DateTime _currentTime = DateTime.UtcNow;
    private readonly OmfVersion _omfVersion = OmfVersion.Omf12;

    [Fact]
    public void AdapterMessageProcessor_WriteDynamicValue_ExistingDataFilter()
    {
        var valueSent = false;
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 16 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = 15 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 10 };

        var dataSelectionItem = new TestDataSelectionItem("Test")
        {
            DataFilterId = ExistingDataFilterId,
            DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<int>>(),
        };

        var dataFilter = new DataFiltersConfiguration()
        {
            AbsoluteDeadband = 100,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = ExistingDataFilterId,
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });

        messageProcessor.WriteDynamicValue(dataSelectionItem, lastValue, MessageAction.Default);
        messageProcessor.WriteDynamicValue(dataSelectionItem, previousValue, MessageAction.Default);

        mockOmfMessageProcessor.Setup(mp => mp.WriteValue(It.IsAny<string>(), It.IsAny<Classification>(), It.IsAny<TimeIndexedValue<int>>(), It.IsAny<MessageAction>()))
            .Callback(() => valueSent = true);

        messageProcessor.WriteDynamicValue(dataSelectionItem, currentValue, MessageAction.Default);

        Assert.False(valueSent);
    }

    [Fact]
    public void AdapterMessageProcessor_WriteDynamicValues_ExistingDataFilter()
    {
        var valueSent = false;
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 16 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = 15 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 10 };

        var dataSelectionItem = new TestDataSelectionItem("Test")
        {
            DataFilterId = ExistingDataFilterId,
            DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<int>>(),
        };

        var dataFilter = new DataFiltersConfiguration
        {
            AbsoluteDeadband = 100,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = ExistingDataFilterId,
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });

        messageProcessor.WriteDynamicValues(dataSelectionItem, new List<TimeIndexedValue<int>> { lastValue, previousValue }, MessageAction.Default);

        mockOmfMessageProcessor.Setup(mp => mp.WriteValue(It.IsAny<string>(), It.IsAny<Classification>(), It.IsAny<TimeIndexedValue<int>>(), It.IsAny<MessageAction>()))
            .Callback(() => valueSent = true);

        messageProcessor.WriteDynamicValues(dataSelectionItem, new List<TimeIndexedValue<int>> { currentValue }, MessageAction.Default);

        Assert.False(valueSent);
    }

    [Fact]
    public void AdapterMessageProcessor_WriteDynamicValue_NonexistentDataFilter()
    {
        var valueSent = false;
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 16 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = 15 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 10 };

        var dataSelectionItem = new TestDataSelectionItem("Test")
        {
            DataFilterId = ExistingDataFilterId,
            DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<int>>(),
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.WriteDynamicValue(dataSelectionItem, lastValue, MessageAction.Default);
        messageProcessor.WriteDynamicValue(dataSelectionItem, previousValue, MessageAction.Default);

        mockOmfMessageProcessor.Setup(mp => mp.WriteDynamicValue(It.IsAny<string>(), It.IsAny<TimeIndexedValue<int>>(), It.IsAny<MessageAction>(), It.IsAny<PartitionKey?>()))
            .Callback(() => valueSent = true);

        messageProcessor.WriteDynamicValue(dataSelectionItem, currentValue, MessageAction.Default);

        Assert.True(valueSent);
    }

    [Fact]
    public void AdapterMessageProcessor_WriteStaticValue()
    {
        var valueSent = false;
        var assetInstance = new Dictionary<string, object>
        {
            ["Id"] = "Id1",
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        mockOmfMessageProcessor.Setup(mp => mp.WriteValue(It.IsAny<string>(), Classification.Static, It.IsAny<object>(), It.IsAny<MessageAction>()))
            .Callback(() => valueSent = true);

        ////TODO: edit for 2.0
        ////messageProcessor.WriteStaticValue("AssetId", assetInstance, MessageAction.Default);

        ////Assert.True(valueSent);
    }

    [Fact]
    public void AdapterMessageProcessor_WriteStaticValue_WithExtendedProperties()
    {
        var valueSent = false;
        var assetInstance = new Dictionary<string, object>
        {
            ["Id"] = "Id1",
        };

        var extendedProperty = new Dictionary<string, PropertyDefinition>
        {
            ["Description"] = new PropertyDefinition
            {
                Type = "string",
            },
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, PropertyDefinition>>(),
            It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<MessageAction>()))
            .Callback(() => valueSent = true);

        ////TODO: edit for 2.0
        ////messageProcessor.WriteStaticValue("AssetId", extendedProperty, null, assetInstance, null, MessageAction.Default);

        ////Assert.True(valueSent);
    }

    [Fact]
    public void AdapterMessageProcessor_WriteStaticValue_WithExtendedPropertiesAndDataSource()
    {
        var valueSent = false;
        var assetInstance = new Dictionary<string, object>
        {
            ["Id"] = "Id1",
        };

        var extendedProperty = new Dictionary<string, PropertyDefinition>
        {
            ["Description"] = new PropertyDefinition
            {
                Type = "string",
            },
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, OmfVersion.Omf20);

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, PropertyDefinition>>(),
            It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(),
            It.IsAny<object>(),
            It.IsAny<IReadOnlyDictionary<string, object>>(),
            It.IsAny<List<string>>(),
            It.IsAny<List<Link>>(),
            It.IsAny<MessageAction>()))
            .Callback(() => valueSent = true);

        messageProcessor.WriteStaticValue("typeId", "AssetId", "Name", "Description", "dataSource", extendedProperty, null, assetInstance, null, ["Tags"], null, MessageAction.Default);

        Assert.True(valueSent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AdapterMessageProcessor_WriteStaticValue_Delete_AllowsNullTypeId(string typeId)
    {
        var assetInstance = new Dictionary<string, object>
        {
            ["Id"] = "Id1",
        };

        var extendedProperty = new Dictionary<string, PropertyDefinition>
        {
            ["Description"] = new PropertyDefinition
            {
                Type = "string",
            },
        };

        string receivedTypeIdWithProperties = "unset";
        string receivedTypeIdWithoutProperties = "unset";

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, OmfVersion.Omf20);

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, PropertyDefinition>>(), It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(),
            It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<List<string>>(), It.IsAny<List<Link>>(), It.IsAny<MessageAction>()))
            .Callback((string typeId, string _, string _, string _, string _, IReadOnlyDictionary<string, PropertyDefinition> _,
                IReadOnlyDictionary<string, PropertyDefinitionOverride> _, object _, IReadOnlyDictionary<string, object> _, List<string> _, List<Link> _, MessageAction _) =>
            {
                receivedTypeIdWithProperties = typeId;
            });

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<List<string>>(),
            It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<MessageAction>()))
            .Callback((string typeId, string _, string _, string _, string _, object _, IReadOnlyDictionary<string, object> _,
                List<string> _, IReadOnlyDictionary<string, PropertyDefinitionOverride> _, MessageAction _) =>
            {
                receivedTypeIdWithoutProperties = typeId;
            });

        messageProcessor.WriteStaticValue(typeId, "AssetId", "Name", "Description", "dataSource", extendedProperty, null, assetInstance, null, ["Tags"], null, MessageAction.Delete);
        messageProcessor.WriteStaticValue(typeId, "AssetId", "Name", "Description", "dataSource", assetInstance, null, ["Tags"], null, MessageAction.Delete);

        // The null/empty typeId flows through unchanged for delete actions.
        Assert.Equal(typeId, receivedTypeIdWithProperties);
        Assert.Equal(typeId, receivedTypeIdWithoutProperties);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AdapterMessageProcessor_WriteStaticValue_NonDelete_NullTypeId_Throws(string typeId)
    {
        var assetInstance = new Dictionary<string, object>
        {
            ["Id"] = "Id1",
        };

        var extendedProperty = new Dictionary<string, PropertyDefinition>
        {
            ["Description"] = new PropertyDefinition
            {
                Type = "string",
            },
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, OmfVersion.Omf20);

        Assert.Throws<ArgumentException>(() => messageProcessor.WriteStaticValue(typeId, "AssetId", "Name", "Description", "dataSource", extendedProperty, null, assetInstance, null, ["Tags"], null, MessageAction.Create));
        Assert.Throws<ArgumentException>(() => messageProcessor.WriteStaticValue(typeId, "AssetId", "Name", "Description", "dataSource", assetInstance, null, ["Tags"], null, MessageAction.Update));
    }

    [Fact]
    public void AdapterMessageProcessor_WriteStaticValues()
    {
        var valuesSent = false;
        var assetInstance1 = new Dictionary<string, object>
        {
            ["Id"] = "Id1",
        };

        var assetInstance2 = new Dictionary<string, object>
        {
            ["Id"] = "Id2",
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        mockOmfMessageProcessor.Setup(mp => mp.WriteValues(It.IsAny<string>(), It.IsAny<Classification>(), It.IsAny<IReadOnlyList<object>>(), It.IsAny<MessageAction>()))
            .Callback(() => valuesSent = true);

        ////TODO: edit for 2.0
        ////messageProcessor.WriteStaticValues("AssetId",  new List<Dictionary<string, object>> { assetInstance1, assetInstance2 }, MessageAction.Default);

        ////Assert.True(valuesSent);
    }

    [Fact]
    public void AdapterMessageProcessor_CreateAssetLink()
    {
        var valueSent = false;

        var sourceNode = new DataTypeLinkNode("Id1", null);
        var targetNode = new DataTypeLinkNode("id2", null);

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        mockOmfMessageProcessor.Setup(mp => mp.WriteValue(It.IsAny<string>(), Classification.Static, It.IsAny<object>(), It.IsAny<MessageAction>()))
            .Callback(() => valueSent = true);

        ////TODO: edit for 2.0
        ////messageProcessor.WriteStaticValue(Tokens.Link, new Link(sourceNode, targetNode), MessageAction.Default);

        ////Assert.True(valueSent);
    }

    [Fact]
    public void AdapterMessageProcessor_WriteDynamicValues_NonexistentDataFilter()
    {
        var valueSent = false;
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 16 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = 15 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 10 };

        var dataSelectionItem = new TestDataSelectionItem("Test")
        {
            DataFilterId = ExistingDataFilterId,
            DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<int>>(),
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.WriteDynamicValues(dataSelectionItem, new List<TimeIndexedValue<int>> { lastValue, previousValue }, MessageAction.Default);

        mockOmfMessageProcessor.Setup(mp => mp.WriteDynamicValues(It.IsAny<string>(), It.IsAny<List<TimeIndexedValue<int>>>(), It.IsAny<MessageAction>(), It.IsAny<PartitionKey?>()))
            .Callback(() => valueSent = true);

        messageProcessor.WriteDynamicValues(dataSelectionItem, new List<TimeIndexedValue<int>> { currentValue }, MessageAction.Default);

        Assert.True(valueSent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemoveDataFilter(bool nullArray)
    {
        var dataFilter = new DataFiltersConfiguration()
        {
            AbsoluteDeadband = 100,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = ExistingDataFilterId,
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });
        messageProcessor.ProcessDataFiltersConfigurationChanges(nullArray ? null : Array.Empty<DataFiltersConfiguration>());

        var filter = messageProcessor.LookupFilterById(ExistingDataFilterId);
        Assert.Null(filter);
    }

    [Fact]
    public void UpdateAbsoluteDeadband_SameType()
    {
        var dataFilter = new DataFiltersConfiguration
        {
            AbsoluteDeadband = 100,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = ExistingDataFilterId,
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });

        dataFilter.ExpirationPeriod = new TimeSpan(1, 0, 0);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });
        var filter = messageProcessor.LookupFilterById(ExistingDataFilterId);
        Assert.NotNull(filter);

        Assert.Equal(dataFilter.ExpirationPeriod, filter.ExpirationPeriod);

        dataFilter.AbsoluteDeadband = 10;

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });
        filter = messageProcessor.LookupFilterById(ExistingDataFilterId);
        Assert.NotNull(filter);

        Assert.Equal(dataFilter.AbsoluteDeadband, filter.FilterValue);
        Assert.IsType<AbsoluteDeadbandDataFilter>(filter);
    }

    [Fact]
    public void UpdateAbsoluteDeadband_DifferentType()
    {
        var dataFilter = new DataFiltersConfiguration
        {
            AbsoluteDeadband = 100,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = ExistingDataFilterId,
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });

        dataFilter.AbsoluteDeadband = null;
        dataFilter.PercentChange = 10;

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });

        var filter = messageProcessor.LookupFilterById(ExistingDataFilterId);
        Assert.NotNull(filter);

        Assert.Equal(0.1, filter.FilterValue);
        Assert.IsType<PercentChangeDataFilter>(filter);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClearDataFilters(bool nullArray)
    {
        var dataFilter = new DataFiltersConfiguration
        {
            AbsoluteDeadband = 10,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = ExistingDataFilterId,
        };
        var dataFilter2 = new DataFiltersConfiguration
        {
            AbsoluteDeadband = null,
            PercentChange = 100,
            ExpirationPeriod = new TimeSpan(1, 0, 0),
            Id = "SecondDataFilter",
        };
        var dataFilter3 = new DataFiltersConfiguration
        {
            AbsoluteDeadband = 50,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = "ThirdDataFilter",
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter, dataFilter2, dataFilter3 });

        Assert.NotNull(messageProcessor.LookupFilterById(dataFilter.Id));
        Assert.NotNull(messageProcessor.LookupFilterById(dataFilter2.Id));
        Assert.NotNull(messageProcessor.LookupFilterById(dataFilter3.Id));

        messageProcessor.ProcessDataFiltersConfigurationChanges(nullArray ? null : Array.Empty<DataFiltersConfiguration>());

        Assert.Null(messageProcessor.LookupFilterById(dataFilter.Id));
        Assert.Null(messageProcessor.LookupFilterById(dataFilter2.Id));
        Assert.Null(messageProcessor.LookupFilterById(dataFilter3.Id));
    }

    [Theory]
    [InlineData(1, 0, 0, true)]
    [InlineData(0, 0, 0, false)]
    [InlineData(1U, 0U, 0U, true)]
    [InlineData(0U, 0U, 0U, false)]
    [InlineData(1L, 0L, 0L, true)]
    [InlineData(0L, 0L, 0L, false)]
    [InlineData(1UL, 0UL, 0UL, true)]
    [InlineData(0UL, 0UL, 0UL, false)]
    [InlineData((byte)1, (byte)0, (byte)0, true)]
    [InlineData((byte)0, (byte)0, (byte)0, false)]
    [InlineData((sbyte)1, (sbyte)0, (sbyte)0, true)]
    [InlineData((sbyte)0, (sbyte)0, (sbyte)0, false)]
    [InlineData(1D, 0D, 0D, true)]
    [InlineData(0D, 0D, 0D, false)]
    [InlineData(1F, 0F, 0F, true)]
    [InlineData(0F, 0F, 0F, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData("1", "0", "0", true)]
    [InlineData("0", "0", "0", false)]
    public void AdapterMessageProcessor_WriteDynamicValue_With_Type_Quality_Test<TQuality>(TQuality currentQuality, TQuality previousQuality, TQuality lastQuality, bool expectValueToBeSent)
    {
        var valueSent = false;
        var currentValue = new TimeIndexedQualityValue<int, TQuality> { Timestamp = _currentTime, Value = 5, Quality = currentQuality };
        var previousValue = new TimeIndexedQualityValue<int, TQuality> { Timestamp = _currentTime.AddSeconds(-1), Value = 5, Quality = previousQuality };
        var lastValue = new TimeIndexedQualityValue<int, TQuality> { Timestamp = _currentTime.AddSeconds(-2), Value = 5, Quality = lastQuality };

        var dataSelectionItem = new TestDataSelectionItem("QualityTypeTest")
        {
            DataFilterId = ExistingDataFilterId,
            DataFilterCache = new DataFilterCachedValues<TimeIndexedQualityValue<int, TQuality>>(),
        };

        var dataFilter = new DataFiltersConfiguration()
        {
            AbsoluteDeadband = 100,
            PercentChange = null,
            ExpirationPeriod = null,
            Id = ExistingDataFilterId,
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var messageProcessor = new AdapterMessageProcessor(mockOmfMessageProcessor.Object, _omfVersion);

        messageProcessor.ProcessDataFiltersConfigurationChanges(new[] { dataFilter });

        messageProcessor.WriteDynamicValue(dataSelectionItem, lastValue, MessageAction.Default);
        messageProcessor.WriteDynamicValue(dataSelectionItem, previousValue, MessageAction.Default);

        mockOmfMessageProcessor.Setup(mp =>
                mp.WriteDynamicValue(It.IsAny<string>(), It.IsAny<TimeIndexedQualityValue<int, TQuality>>(), It.IsAny<MessageAction>(), It.IsAny<PartitionKey?>()))
            .Callback(() => valueSent = true);

        messageProcessor.WriteDynamicValue(dataSelectionItem, currentValue, MessageAction.Default);

        Assert.Equal(expectValueToBeSent, valueSent);
    }
}
