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
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.DataModel.Extensions;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.MessageProcessor.Tests;

public class InstrumentedMessageProcessor_Tests
{
    private const string InvalidTypeId = "T*'?;{}[]|`estType";
    private const string TestComponentId = "TestComponentId";
    private const string TestComponentType = "TestComponentType";
    private const string TestStreamIdBase = "TestStream";
    private const string TestTypeIdBase = "TestType";

    private readonly Mock<ILogger> _mLogger = new();

    public static IEnumerable<object[]> DataSelectionConfigsRemovedItems =>
     new List<object[]>
     {
        new object[] { Array.Empty<IDataSelectionConfiguration>() },
        new object[] { new IDataSelectionConfiguration[] { new TestDataSelectionItem(1.ToString(CultureInfo.InvariantCulture)) } },
        new object[]
        {
            new IDataSelectionConfiguration[]
            {
                new TestDataSelectionItem(1.ToString(CultureInfo.InvariantCulture)), new TestDataSelectionItem(3.ToString(CultureInfo.InvariantCulture)), new TestDataSelectionItem(5.ToString(CultureInfo.InvariantCulture)),
                new TestDataSelectionItem(7.ToString(CultureInfo.InvariantCulture)), new TestDataSelectionItem(9.ToString(CultureInfo.InvariantCulture)), new TestDataSelectionItem("B"),
            },
        },
        new object[] { new IDataSelectionConfiguration[] { new TestDataSelectionItem(15.ToString(CultureInfo.InvariantCulture)), new TestDataSelectionItem("E") } },
        new object[] { new IDataSelectionConfiguration[] { new TestDataSelectionItem(3.ToString(CultureInfo.InvariantCulture)), new TestDataSelectionItem(15.ToString(CultureInfo.InvariantCulture)) } },
     };
    public static IEnumerable<object[]> DataSelectionConfigsUnselectedItems =>
    new List<object[]>
    {
        new object[]
        {
            new IDataSelectionConfiguration[]
            {
                new TestDataSelectionItem(1.ToString(CultureInfo.InvariantCulture)) { Selected = false },
                new TestDataSelectionItem(3.ToString(CultureInfo.InvariantCulture)) { Selected = false }, new TestDataSelectionItem(5.ToString(CultureInfo.InvariantCulture)) { Selected = false },
                new TestDataSelectionItem(7.ToString(CultureInfo.InvariantCulture)), new TestDataSelectionItem("B"), new TestDataSelectionItem(9.ToString(CultureInfo.InvariantCulture)),
            },
        },
        new object[] { new IDataSelectionConfiguration[] { new TestDataSelectionItem(3.ToString(CultureInfo.InvariantCulture)) { Selected = false }, new TestDataSelectionItem(15.ToString(CultureInfo.InvariantCulture)) } },
    };

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteType_Test(int itemCount)
    {
        var methodCallCounter = 0;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback(() => methodCallCounter++);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        for (var i = 0; i < itemCount; i++)
        {
            instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, MessageAction.Default);
        }

        Assert.True(instrumentedMessageProcessor.GetTypeCount() == itemCount);
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.Equal(itemCount, methodCallCounter);

        for (var i = 0; i < itemCount; i++)
        {
            instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, MessageAction.Default);
        }

        Assert.True(instrumentedMessageProcessor.GetTypeCount() == itemCount);
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.Equal(itemCount * 2, methodCallCounter);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteType_RefType_InvalidCharacters_Test()
    {
        ICollection<PropertyDefinition> receivedTypeProperties = null;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback((DataType dataType, MessageAction messageAction) => receivedTypeProperties = dataType.Properties.Values);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var timeStampProperty = typeof(DateTime).ToPropertyDefinition();
        timeStampProperty.IsIndex = true;

        var properties = new Dictionary<string, PropertyDefinition>
        {
            ["a"] = timeStampProperty,
            ["b"] = new PropertyDefinition { RefTypeId = InvalidTypeId },
            ["c"] = new PropertyDefinition { RefTypeId = InvalidTypeId },
        };

        instrumentedMessageProcessor.WriteType(new DynamicDataType(TestTypeIdBase, TestTypeIdBase, properties), MessageAction.Default);

        foreach (var property in receivedTypeProperties)
        {
            if (string.IsNullOrEmpty(property.RefTypeId))
            {
                continue;
            }

            Assert.NotEqual(InvalidTypeId, property.RefTypeId);
            Assert.Equal(InvalidTypeId.ToOmfIdentifier(), property.RefTypeId);
        }
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteType_InvalidCharacters_Test()
    {
        var receivedTypeId = string.Empty;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback((DataType dataType, MessageAction messageAction) => receivedTypeId = dataType.Id);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = InvalidTypeId }, MessageAction.Default);

        Assert.NotEqual(InvalidTypeId, receivedTypeId);
        Assert.Equal(InvalidTypeId.ToOmfIdentifier(), receivedTypeId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteTypes_Test(int itemCount)
    {
        var methodCallCounter = 0;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteTypes(It.IsAny<DataType[]>(), It.IsAny<MessageAction>()))
            .Callback(() => methodCallCounter++);

        var typesArray = new DataType[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            typesArray[i] = new DynamicDataType { Id = TestTypeIdBase + i };
        }

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteTypes(typesArray, MessageAction.Default);

        Assert.True(instrumentedMessageProcessor.GetTypeCount() == itemCount);
        Assert.True(instrumentedMessageProcessor.GetStreamCount() == 0);
        Assert.True(instrumentedMessageProcessor.GetAndResetEventsCounter() == 0);

        Assert.Equal(1, methodCallCounter);

        instrumentedMessageProcessor.WriteTypes(typesArray, MessageAction.Default);

        Assert.True(instrumentedMessageProcessor.GetTypeCount() == itemCount);
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.Equal(2, methodCallCounter);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteTypes_InvalidCharacters_Test(int itemCount)
    {
        var createdTypes = new List<DataType>();
        var originalIds = new string[itemCount];
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteTypes(It.IsAny<DataType[]>(), It.IsAny<MessageAction>()))
            .Callback((DataType[] dataTypes, MessageAction messageAction) => createdTypes.AddRange(dataTypes));

        var typesArray = new DataType[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            originalIds[i] = InvalidTypeId + i;
            typesArray[i] = new DynamicDataType { Id = originalIds[i] };
        }

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteTypes(typesArray, MessageAction.Default);

        for (int i = 0; i < originalIds.Length; i++)
        {
            Assert.Equal(originalIds[i].ToOmfIdentifier(), createdTypes[i].Id);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteTypes_RefType_InvalidCharacters_Test(int itemCount)
    {
        var createdTypes = new List<DataType>();
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteTypes(It.IsAny<DataType[]>(), It.IsAny<MessageAction>()))
            .Callback((DataType[] dataTypes, MessageAction messageAction) => createdTypes.AddRange(dataTypes));

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);
        
        var typesArray = new DataType[itemCount];
        var timeStampProperty = typeof(DateTime).ToPropertyDefinition();
        timeStampProperty.IsIndex = true;

        var properties = new Dictionary<string, PropertyDefinition>
        {
            ["a"] = timeStampProperty,
            ["b"] = new PropertyDefinition { RefTypeId = InvalidTypeId },
            ["c"] = new PropertyDefinition { RefTypeId = InvalidTypeId },
        };

        for (var i = 0; i < itemCount; i++) 
        {
            typesArray[i] = new DynamicDataType(InvalidTypeId + i, TestTypeIdBase, properties);
        }

        instrumentedMessageProcessor.WriteTypes(typesArray, MessageAction.Default);

        for (var i = 0; i < itemCount; i++)
        {
            Assert.Equal((InvalidTypeId + i).ToOmfIdentifier(), createdTypes[i].Id);
            foreach (var property in createdTypes[i].Properties.Values)
            {
                if (string.IsNullOrEmpty(property.RefTypeId))
                {
                    continue;
                }

                Assert.NotEqual(InvalidTypeId, property.RefTypeId);
                Assert.Equal(InvalidTypeId.ToOmfIdentifier(), property.RefTypeId);
            }
        }
    }

    [Theory]
    [InlineData(MetadataInfo.None)]
    [InlineData(MetadataInfo.Low)]
    [InlineData(MetadataInfo.Medium)]
    [InlineData(MetadataInfo.High)]
    public void InstrumentedMessageProcessor_WriteStreamMetadata_Test(MetadataInfo metadataLevel)
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType)
        {
            StreamMetadataLevel = metadataLevel,
        };

        var data = new DataStream { Id = TestTypeIdBase };
        instrumentedMessageProcessor.WriteStream(data, MessageAction.Default);

        Assert.Equal(metadataLevel == MetadataInfo.None, data.Metadata.IsEmpty());
    }

    [Theory]
    [InlineData(MetadataInfo.None)]
    [InlineData(MetadataInfo.Low)]
    [InlineData(MetadataInfo.Medium)]
    [InlineData(MetadataInfo.High)]
    public void InstrumentedMessageProcessor_WriteStreamsMetadata_Test(MetadataInfo metadataLevel)
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType)
        {
            StreamMetadataLevel = metadataLevel,
        };

        var data = new DataStream { Id = TestTypeIdBase };
        instrumentedMessageProcessor.WriteStreams([data], MessageAction.Default);

        Assert.Equal(metadataLevel == MetadataInfo.None, data.Metadata.IsEmpty());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteStream_Test(int itemCount)
    {
        var methodCallCounter = 0;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback(() => methodCallCounter++);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        for (var i = 0; i < itemCount; i++)
        {
            instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, MessageAction.Default);
        }

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(itemCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.Equal(itemCount, methodCallCounter);

        for (var i = 0; i < itemCount; i++)
        {
            instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, MessageAction.Default);
        }

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(itemCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.Equal(itemCount * 2, methodCallCounter);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStream_InvalidCharacters_Test()
    {
        var invalidId = "T*'?;{}[]|`estStream";
        var invalidPrefix = "Pr*?e[]fix";
        DataStream receivedStream = null;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedStream = dataStream);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);
        instrumentedMessageProcessor.SetStreamIdPrefix(invalidPrefix);

        instrumentedMessageProcessor.WriteStream(new DataStream { Id = invalidId, TypeId = invalidId }, MessageAction.Default);

        Assert.NotEqual(invalidId, receivedStream.Id);
        Assert.NotEqual(invalidId, receivedStream.TypeId);
        Assert.Equal((invalidPrefix + invalidId).ToOmfIdentifier(), receivedStream.Id);
        Assert.Equal(invalidId.ToOmfIdentifier(), receivedStream.TypeId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteStreams_Test(int itemCount)
    {
        var methodCallCounter = 0;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStreams(It.IsAny<DataStream[]>(), It.IsAny<MessageAction>()))
            .Callback(() => methodCallCounter++);

        var containersArray = new DataStream[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            containersArray[i] = new DataStream { Id = TestStreamIdBase + i };
        }

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteStreams(containersArray, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.True(instrumentedMessageProcessor.GetStreamCount() == itemCount);
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.Equal(1, methodCallCounter);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteStreams_InvalidCharacters_Test(int itemCount)
    {
        var invalidPrefix = "Pr*?e[]fix";
        var createdStreams = new List<DataStream>();
        var originalIds = new string[itemCount];
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStreams(It.IsAny<DataStream[]>(), It.IsAny<MessageAction>()))
            .Callback((DataStream[] dataStreams, MessageAction messageAction) => createdStreams.AddRange(dataStreams));

        var streamsArray = new DataStream[itemCount];
        for (var i = 0; i < itemCount; i++)
        {
            originalIds[i] = "T*'?;{}[]|`estStream" + i;
            streamsArray[i] = new DataStream
            {
                Id = originalIds[i],
                TypeId = originalIds[i],
            };
        }

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);
        instrumentedMessageProcessor.SetStreamIdPrefix(invalidPrefix);

        instrumentedMessageProcessor.WriteStreams(streamsArray, MessageAction.Default);

        for (int i = 0; i < originalIds.Length; i++)
        {
            Assert.NotEqual(originalIds[i], createdStreams[i].Id);
            Assert.NotEqual(originalIds[i], createdStreams[i].TypeId);
            Assert.Equal((invalidPrefix + originalIds[i]).ToOmfIdentifier(), createdStreams[i].Id);
            Assert.Equal(originalIds[i].ToOmfIdentifier(), createdStreams[i].TypeId);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteValue_Test(int itemCount)
    {
        var postedStreamIds = new List<string>();
        var streamIdPrefix = "TestPrefix.";
        var methodCallCounter = 0;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteValue(It.IsAny<string>(), It.IsAny<Classification>(), It.IsAny<object>(), It.IsAny<MessageAction>()))
            .Callback((string streamId, Classification classification, object instance, MessageAction messageAction) =>
            {
                methodCallCounter++;
                postedStreamIds.Add(streamId);
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);
        instrumentedMessageProcessor.SetStreamIdPrefix(streamIdPrefix);

        for (var i = 0; i < itemCount; i++)
        {
            instrumentedMessageProcessor.WriteValue(TestStreamIdBase, Classification.Dynamic, new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 }, MessageAction.Default);
        }

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(itemCount, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(itemCount, methodCallCounter);

        foreach (var streamId in postedStreamIds)
        {
            Assert.StartsWith(streamIdPrefix, streamId, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(42, 4)]
    [InlineData(4, 0)]
    [InlineData(0, 5)]
    public void InstrumentedMessageProcessor_WriteStaticValue_Test(int assetsCount, int linksCount)
    {
        var postedIds = new List<string>();
        var methodCallCounter = 0;

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, PropertyDefinition>>(),
            It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(),
            It.IsAny<MessageAction>())).Callback((string id, IReadOnlyDictionary<string, PropertyDefinition> ext,
            IReadOnlyDictionary<string, PropertyDefinitionOverride> overrides, object instance, IReadOnlyDictionary<string, object> _, MessageAction messageAction) =>
            {
                methodCallCounter++;
                postedIds.Add(id);
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        for (var i = 0; i < assetsCount; i++)
        {
            var assetInstance = new Dictionary<string, object>
            {
                ["Id"] = i.ToString(CultureInfo.InvariantCulture),
            };

            instrumentedMessageProcessor.WriteStaticValue(TestStreamIdBase, null, null, assetInstance, null, MessageAction.Default);
        }

        for (var i = 0; i < linksCount; i++)
        {
            var sourceNode = new DataTypeLinkNode(i.ToString(CultureInfo.InvariantCulture), null);
            var targetNode = new DataTypeLinkNode(i.ToString(CultureInfo.InvariantCulture) + i, null);

            var link = new Link(sourceNode, targetNode);
            instrumentedMessageProcessor.WriteStaticValue(Tokens.Link, null, null, link, null, MessageAction.Default);
        }

        Assert.True(instrumentedMessageProcessor.GetTypeCount() == 0);
        Assert.True(instrumentedMessageProcessor.GetStreamCount() == 0);
        Assert.True(instrumentedMessageProcessor.GetAndResetEventsCounter() == assetsCount + linksCount);
        Assert.Equal(assetsCount + linksCount, methodCallCounter);
        Assert.Equal(assetsCount + linksCount, postedIds.Count);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStaticValue_InvalidCharacters_Test()
    {
        var receivedId = string.Empty;
        var invalidId = "T*'?;{}[]|`Asset";

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, PropertyDefinition>>(),
            It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<MessageAction>()))
            .Callback((string id, IReadOnlyDictionary<string, PropertyDefinition> _,
            IReadOnlyDictionary<string, PropertyDefinitionOverride> _, object instance, IReadOnlyDictionary<string, object> _, MessageAction messageAction) =>
            {
                receivedId = id;
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);
        instrumentedMessageProcessor.SetStreamIdPrefix("Hello");

        instrumentedMessageProcessor.WriteStaticValue(invalidId, null, null, new object(), null, MessageAction.Default);

        Assert.NotEqual(invalidId, receivedId);
        Assert.Equal(invalidId.ToOmfIdentifier(), receivedId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void InstrumentedMessageProcessor_WriteStaticValues_Test(int assetsCount)
    {
        var receivedId = string.Empty;

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteValues(It.IsAny<string>(), Classification.Static, It.IsAny<IReadOnlyList<object>>(), It.IsAny<MessageAction>()))
            .Callback((string id, Classification classification, object value, MessageAction messageAction) =>
            {
                receivedId = id;
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var assetInstances = new List<Dictionary<string, object>>(assetsCount);

        for (var i = 0; i < assetsCount; i++)
        {
            var assetInstance = new Dictionary<string, object>
            {
                ["Id"] = i.ToString(CultureInfo.InvariantCulture),
            };

            assetInstances.Add(assetInstance);
        }

        instrumentedMessageProcessor.WriteValues(TestStreamIdBase, Classification.Static, assetInstances, MessageAction.Default);
        instrumentedMessageProcessor.SetStreamIdPrefix("Hello");

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(assetsCount, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(TestStreamIdBase, receivedId);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStaticValues_DataSource_Test()
    {
        var dataSource1 = string.Empty;
        var dataSource2 = string.Empty;

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();

        // set up both WriteStaticValue with data source
        // one with extended Property definitions
        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, PropertyDefinition>>(), It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<object>(),
                It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<List<string>>(), It.IsAny<List<Link>>(), It.IsAny<MessageAction>()))
            .Callback((string _, string id, string _, string _, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> _,
                IReadOnlyDictionary<string, PropertyDefinitionOverride> _, object _, IReadOnlyDictionary<string, object> _, List<string> _, List<Link> _, MessageAction _) =>
            {
                dataSource1 = dataSource;
            });

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<List<string>>(),
                It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<MessageAction>()))
            .Callback((string _, string id, string _, string _, string dataSource, object _, IReadOnlyDictionary<string, object> _,
                List<string> _, IReadOnlyDictionary<string, PropertyDefinitionOverride> _, MessageAction _) =>
            {
                dataSource2 = dataSource;
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var instance = new Dictionary<string, string>() { { "prop1", "prop1Value" } };

        instrumentedMessageProcessor.WriteStaticValue(TestTypeIdBase, TestStreamIdBase, "name", "description", "wrongDataSource", null, null,
            instance, null, null, null, MessageAction.Default);
        instrumentedMessageProcessor.WriteStaticValue(TestTypeIdBase, TestStreamIdBase, "name2", "description2", "wrongDataSource2",
            instance, null, null, null, MessageAction.Default);
        
        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(2, instrumentedMessageProcessor.GetAndResetEventsCounter());

        // data source should be replaced with component id
        Assert.Equal(TestComponentId, dataSource1);
        Assert.Equal(TestComponentId, dataSource2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InstrumentedMessageProcessor_WriteStaticValue_Delete_AllowsNullTypeId_Test(string typeId)
    {
        string receivedTypeIdWithProperties = "unset";
        string receivedTypeIdWithoutProperties = "unset";

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyDictionary<string, PropertyDefinition>>(), It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<object>(),
                It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<List<string>>(), It.IsAny<List<Link>>(), It.IsAny<MessageAction>()))
            .Callback((string typeId, string _, string _, string _, string _, IReadOnlyDictionary<string, PropertyDefinition> _,
                IReadOnlyDictionary<string, PropertyDefinitionOverride> _, object _, IReadOnlyDictionary<string, object> _, List<string> _, List<Link> _, MessageAction _) =>
            {
                receivedTypeIdWithProperties = typeId;
            });

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<List<string>>(),
                It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<MessageAction>()))
            .Callback((string typeId, string _, string _, string _, string _, object _, IReadOnlyDictionary<string, object> _,
                List<string> _, IReadOnlyDictionary<string, PropertyDefinitionOverride> _, MessageAction _) =>
            {
                receivedTypeIdWithoutProperties = typeId;
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var instance = new Dictionary<string, string> { { "prop1", "prop1Value" } };

        instrumentedMessageProcessor.WriteStaticValue(typeId, TestStreamIdBase, "name", "description", "dataSource", null, null,
            instance, null, null, null, MessageAction.Delete);
        instrumentedMessageProcessor.WriteStaticValue(typeId, TestStreamIdBase, "name2", "description2", "dataSource2",
            instance, null, null, null, MessageAction.Delete);

        // A delete payload must not carry a typeid, so the encoded typeId is null.
        Assert.Null(receivedTypeIdWithProperties);
        Assert.Null(receivedTypeIdWithoutProperties);
        Assert.Equal(2, instrumentedMessageProcessor.GetAndResetEventsCounter());
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStaticValue_Delete_EncodesProvidedTypeId_Test()
    {
        string receivedTypeId = null;

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();

        mockOmfMessageProcessor.Setup(mp => mp.WriteStaticValue(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<List<string>>(),
                It.IsAny<IReadOnlyDictionary<string, PropertyDefinitionOverride>>(), It.IsAny<MessageAction>()))
            .Callback((string typeId, string _, string _, string _, string _, object _, IReadOnlyDictionary<string, object> _,
                List<string> _, IReadOnlyDictionary<string, PropertyDefinitionOverride> _, MessageAction _) =>
            {
                receivedTypeId = typeId;
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var instance = new Dictionary<string, string> { { "prop1", "prop1Value" } };

        // A delete that still provides a typeId encodes it like any other identifier.
        instrumentedMessageProcessor.WriteStaticValue(InvalidTypeId, TestStreamIdBase, "name", "description", "dataSource",
            instance, null, null, null, MessageAction.Delete);

        Assert.Equal(InvalidTypeId.ToOmfIdentifier(), receivedTypeId);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStaticValue_NonDelete_NullTypeId_Throws_Test()
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var instance = new Dictionary<string, string> { { "prop1", "prop1Value" } };

        Assert.ThrowsAny<ArgumentException>(() => instrumentedMessageProcessor.WriteStaticValue(null, TestStreamIdBase, "name", "description", "dataSource",
            instance, null, null, null, MessageAction.Create));

        Assert.ThrowsAny<ArgumentException>(() => instrumentedMessageProcessor.WriteStaticValue(null, TestStreamIdBase, "name", "description", "dataSource", null, null,
            instance, null, null, null, MessageAction.Update));
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteValue_InvalidCharacters_Test()
    {
        var receivedStreamId = string.Empty;
        var invalidStreamId = "T*'?;{}[]|`Stream";
        var invalidPrefix = "Pr*?e[]fix";
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteValue(It.IsAny<string>(), It.IsAny<Classification>(), It.IsAny<object>(), It.IsAny<MessageAction>()))
            .Callback((string id, Classification classification, object value, MessageAction messageAction) => receivedStreamId = id);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);
        instrumentedMessageProcessor.SetStreamIdPrefix(invalidPrefix);

        instrumentedMessageProcessor.WriteValue(invalidStreamId, Classification.Dynamic, new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 }, MessageAction.Default);

        Assert.NotEqual(invalidStreamId, receivedStreamId);
        Assert.Equal((invalidPrefix + invalidStreamId).ToOmfIdentifier(), receivedStreamId);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteValues_InvalidCharacters_Test()
    {
        var receivedStreamId = string.Empty;
        var invalidStreamId = "T*'?;{}[]|`Stream";
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteValues(It.IsAny<string>(), It.IsAny<Classification>(), It.IsAny<List<TimeIndexedValue<int>>>(), It.IsAny<MessageAction>()))
            .Callback((string id, Classification classification, object value, MessageAction messageAction) => receivedStreamId = id);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteValues(invalidStreamId, Classification.Dynamic, new List<TimeIndexedValue<int>>
        {
            new() { Timestamp = DateTime.UtcNow, Value = 42 },
        }, MessageAction.Default);

        Assert.NotEqual(invalidStreamId, receivedStreamId);
        Assert.Equal(invalidStreamId.ToOmfIdentifier(), receivedStreamId);
    }

    [Fact]
    public void InstrumentedMessageProcessor_SetStreamIdPrefix_Test()
    {
        DataStream receivedStream = null;
        var testStreamId = "TestStreamId";
        var prefix = "Prefix1.";

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedStream = dataStream);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.SetStreamIdPrefix(prefix);
        instrumentedMessageProcessor.WriteStream(new DataStream { Id = testStreamId }, MessageAction.Default);

        Assert.StartsWith(prefix, receivedStream.Id, StringComparison.OrdinalIgnoreCase);

        instrumentedMessageProcessor.ClearStreamsCollection();

        prefix = "NewPrefix.";

        instrumentedMessageProcessor.SetStreamIdPrefix(prefix);
        instrumentedMessageProcessor.WriteStream(new DataStream { Id = testStreamId }, MessageAction.Default);

        Assert.StartsWith(prefix, receivedStream.Id, StringComparison.OrdinalIgnoreCase);

        instrumentedMessageProcessor.ClearStreamsCollection();
        instrumentedMessageProcessor.SetStreamIdPrefix(string.Empty);

        instrumentedMessageProcessor.WriteStream(new DataStream { Id = testStreamId }, MessageAction.Default);

        Assert.Equal(testStreamId, receivedStream.Id);
    }

    [Fact]
    public void InstrumentedMessageProcessor_GetStreamCount_Test()
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        const int ExpectedStreamCount = 500;

        Parallel.For(0, ExpectedStreamCount, i => instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, MessageAction.Default));

        Assert.Equal(ExpectedStreamCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Parallel.For(0, ExpectedStreamCount, i => instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, MessageAction.Default));

        Assert.Equal(ExpectedStreamCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());
    }

    [Fact]
    public void InstrumentedMessageProcessor_GetTypeCount_Test()
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        const int ExpectedTypeCount = 500;

        Parallel.For(0, ExpectedTypeCount, i => instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, MessageAction.Default));

        Assert.Equal(ExpectedTypeCount, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Parallel.For(0, ExpectedTypeCount, i => instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, MessageAction.Default));

        Assert.Equal(ExpectedTypeCount, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());
    }

    [Fact]
    public void InstrumentedMessageProcessor_GetAndResetEventsCounter_WriteValue_Test()
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        const int ExpectedEventsCount = 500;

        Parallel.For(0, ExpectedEventsCount,
            i => instrumentedMessageProcessor.WriteValue(TestStreamIdBase, Classification.Dynamic, new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 }, MessageAction.Default));

        Assert.Equal(ExpectedEventsCount, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
    }

    [Fact]
    public void InstrumentedMessageProcessor_GetAndResetEventsCounter_WriteValues_Test()
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var data = new List<TimeIndexedValue<int>>
        {
            new() { Timestamp = DateTime.UtcNow, Value = 41 },
            new() { Timestamp = DateTime.UtcNow, Value = 42 },
            new() { Timestamp = DateTime.UtcNow, Value = 43 },
        };

        var iterationCount = 10;
        var expectedEventsCount = iterationCount * data.Count;

        Parallel.For(0, iterationCount,
            i => instrumentedMessageProcessor.WriteValues(TestStreamIdBase, Classification.Dynamic, data, MessageAction.Default));

        Assert.Equal(expectedEventsCount, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(0, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
    }

    [Fact]
    public void InstrumentedMessageProcessor_ClearCounters_Test()
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var expectedEventsCount = 500;

        Parallel.For(0, expectedEventsCount, i =>
        {
            instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, MessageAction.Default);
            instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, MessageAction.Default);
            instrumentedMessageProcessor.WriteValue(TestStreamIdBase, Classification.Dynamic, new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 }, MessageAction.Default);
        });

        Assert.Equal(expectedEventsCount, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(expectedEventsCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(expectedEventsCount, instrumentedMessageProcessor.GetTypeCount());

        instrumentedMessageProcessor.ClearCounters();
        expectedEventsCount = 0;

        Assert.Equal(expectedEventsCount, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(expectedEventsCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(expectedEventsCount, instrumentedMessageProcessor.GetTypeCount());
    }

    [Fact]
    public void InstrumentedMessageProcessor_ResendMetadata_Test()
    {
        var sentTypes = new List<DataType>();
        var sentStreams = new List<DataStream>();

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(messageProcessor => messageProcessor.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback((DataType type, MessageAction messageAction) => sentTypes.Add(type));

        mockOmfMessageProcessor.Setup(messageProcessor => messageProcessor.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream stream, MessageAction messageAction) => sentStreams.Add(stream));

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        const int ExpectedTypesStreamsCount = 500;

        Parallel.For(0, ExpectedTypesStreamsCount, i =>
        {
            instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, MessageAction.Default);
            instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, MessageAction.Default);
        });

        Assert.Equal(ExpectedTypesStreamsCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(ExpectedTypesStreamsCount, instrumentedMessageProcessor.GetTypeCount());

        // Isolate the resend output from the initial writes captured by the same singular mocks.
        sentTypes.Clear();
        sentStreams.Clear();

        instrumentedMessageProcessor.ResendTypesAndStreams();

        // instrumented numbers were not updated
        Assert.Equal(ExpectedTypesStreamsCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(ExpectedTypesStreamsCount, instrumentedMessageProcessor.GetTypeCount());

        Assert.Equal(ExpectedTypesStreamsCount, sentTypes.Count);
        Assert.Equal(ExpectedTypesStreamsCount, sentStreams.Count);
    }

    [Fact]
    public void InstrumentedMessageProcessor_ClearMetadataCollections_Test()
    {
        var sentTypes = new List<DataType>();
        var sentStreams = new List<DataStream>();

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(messageProcessor => messageProcessor.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback((DataType type, MessageAction messageAction) => sentTypes.Add(type));

        mockOmfMessageProcessor.Setup(messageProcessor => messageProcessor.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream stream, MessageAction messageAction) => sentStreams.Add(stream));

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        const int ExpectedTypesStreamsCount = 500;

        Parallel.For(0, ExpectedTypesStreamsCount, i =>
        {
            instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, MessageAction.Default);
            instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, MessageAction.Default);
        });

        Assert.Equal(ExpectedTypesStreamsCount, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(ExpectedTypesStreamsCount, instrumentedMessageProcessor.GetTypeCount());

        // Isolate the resend output from the initial writes captured by the same singular mocks.
        sentTypes.Clear();
        sentStreams.Clear();

        instrumentedMessageProcessor.ClearStreamsCollection();
        instrumentedMessageProcessor.ResendTypesAndStreams();

        Assert.Equal(ExpectedTypesStreamsCount, instrumentedMessageProcessor.GetTypeCount());
        Assert.Empty(sentStreams);
        Assert.NotEmpty(sentTypes);
    }

    [Fact]
    public void InstrumentedMessageProcessor_ResendTypesAndStreams_PreservesOrderAndMessageAction_Test()
    {
        var sentTypes = new List<(string Id, MessageAction Action)>();
        var sentStreams = new List<(string Id, MessageAction Action)>();

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback((DataType type, MessageAction action) => sentTypes.Add((type.Id, action)));
        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream stream, MessageAction action) => sentStreams.Add((stream.Id, action)));

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var actions = new[] { MessageAction.Create, MessageAction.Update, MessageAction.Delete };
        var expectedTypes = new List<(string, MessageAction)>();
        var expectedStreams = new List<(string, MessageAction)>();

        for (var i = 0; i < 6; i++)
        {
            var action = actions[i % actions.Length];
            instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + i }, action);
            instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase + i }, action);
            expectedTypes.Add(((TestTypeIdBase + i).ToOmfIdentifier(), action));
            expectedStreams.Add(((TestStreamIdBase + i).ToPrefixedOmfIdentifier(null), action));
        }

        // Isolate the resend output from the initial writes captured by the same singular mocks.
        sentTypes.Clear();
        sentStreams.Clear();

        instrumentedMessageProcessor.ResendTypesAndStreams();

        // Both the original receive order and the per-record MessageAction are preserved.
        Assert.Equal(expectedTypes, sentTypes);
        Assert.Equal(expectedStreams, sentStreams);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteType_ReWrite_PreservesOriginalOrderAndUpdatesAction_Test()
    {
        var sentTypes = new List<(string Id, MessageAction Action)>();

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback((DataType type, MessageAction action) => sentTypes.Add((type.Id, action)));

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + "A" }, MessageAction.Create);
        instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + "B" }, MessageAction.Create);

        // Re-write the first type with a new action; it should keep its original position but update the action.
        instrumentedMessageProcessor.WriteType(new DynamicDataType { Id = TestTypeIdBase + "A" }, MessageAction.Update);

        sentTypes.Clear();
        instrumentedMessageProcessor.ResendTypesAndStreams();

        Assert.Equal(
            new List<(string, MessageAction)>
            {
                ((TestTypeIdBase + "A").ToOmfIdentifier(), MessageAction.Update),
                ((TestTypeIdBase + "B").ToOmfIdentifier(), MessageAction.Create),
            },
            sentTypes);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteSchemaRelationship_CachesAndResends_Test()
    {
        var writtenRelationships = new List<(Link Link, MessageAction Action)>();
        var resentRelationships = new List<(Link Link, MessageAction Action)>();
        var resending = false;

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteSchemaRelationship(It.IsAny<Link>(), It.IsAny<MessageAction>()))
            .Callback((Link link, MessageAction action) =>
            {
                if (resending)
                {
                    resentRelationships.Add((link, action));
                }
                else
                {
                    writtenRelationships.Add((link, action));
                }
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var link1 = new Link(new DataTypeLinkNode("E1", null) { Property = "p1" }, new DataTypeLinkNode("int", null));
        var link2 = new Link(new DataTypeLinkNode("E1", null) { Property = "p2" }, new DataTypeLinkNode("string", null));

        instrumentedMessageProcessor.WriteSchemaRelationship(link1, MessageAction.Create);
        instrumentedMessageProcessor.WriteSchemaRelationship(link2, MessageAction.Update);

        // The relationship is still forwarded to the underlying processor on write.
        Assert.Equal(2, writtenRelationships.Count);

        resending = true;
        instrumentedMessageProcessor.ResendTypesAndStreams();

        // Both cached relationships are resent in order, each with its preserved MessageAction.
        Assert.Equal(2, resentRelationships.Count);
        Assert.Same(link1, resentRelationships[0].Link);
        Assert.Equal(MessageAction.Create, resentRelationships[0].Action);
        Assert.Same(link2, resentRelationships[1].Link);
        Assert.Equal(MessageAction.Update, resentRelationships[1].Action);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteSchemaRelationship_DedupsByKeyAndClears_Test()
    {
        var resentRelationships = new List<(Link Link, MessageAction Action)>();
        var resending = false;

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteSchemaRelationship(It.IsAny<Link>(), It.IsAny<MessageAction>()))
            .Callback((Link link, MessageAction action) =>
            {
                if (resending)
                {
                    resentRelationships.Add((link, action));
                }
            });

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        var source = new DataTypeLinkNode("E1", null) { Property = "p1" };
        var target = new DataTypeLinkNode("int", null);
        var link = new Link(source, target);
        var linkUpdated = new Link(source, target);

        // Same relationship key written twice should dedup to a single cached entry with the latest content and action.
        instrumentedMessageProcessor.WriteSchemaRelationship(link, MessageAction.Create);
        instrumentedMessageProcessor.WriteSchemaRelationship(linkUpdated, MessageAction.Update);

        resending = true;
        instrumentedMessageProcessor.ResendTypesAndStreams();

        var single = Assert.Single(resentRelationships);
        Assert.Same(linkUpdated, single.Link);
        Assert.Equal(MessageAction.Update, single.Action);

        // ClearStreamsCollection only clears the streams cache; the relationships cache should be preserved.
        resentRelationships.Clear();
        instrumentedMessageProcessor.ClearStreamsCollection();
        instrumentedMessageProcessor.ResendTypesAndStreams();

        var stillCached = Assert.Single(resentRelationships);
        Assert.Same(linkUpdated, stillCached.Link);
        Assert.Equal(MessageAction.Update, stillCached.Action);
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStream_Metadata_Test()
    {
        DataStream receivedMessage = null;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();

        var metaDataDict = new Dictionary<string, object>
        {
            { EdgeSystemConstants.AdapterTypeString, TestComponentType },
        };

        var existingMetaDataDict = new Dictionary<string, object>
        {
            { "TestString", "TestObject" },
        };

        var expectedMessage = new DataStream { Id = TestStreamIdBase, Metadata = metaDataDict, DataSource = TestComponentId };

        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedMessage = dataStream);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType)
        {
            StreamMetadataLevel = MetadataInfo.Low,
        };

        instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase, Metadata = existingMetaDataDict }, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(1, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedMessage.Id, receivedMessage.Id);
        Assert.Equal(expectedMessage.Metadata.Count + 1, receivedMessage.Metadata.Count);
        Assert.Equal(expectedMessage.Metadata[EdgeSystemConstants.AdapterTypeString], receivedMessage.Metadata[EdgeSystemConstants.AdapterTypeString]);
        Assert.Equal(expectedMessage.DataSource, receivedMessage.DataSource);
        Assert.Equal("TestObject", receivedMessage.Metadata["TestString"]);

        instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType)
        {
            StreamMetadataLevel = MetadataInfo.None,
        };

        instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase, Metadata = existingMetaDataDict }, MessageAction.Default);

        Assert.Empty(receivedMessage.Metadata);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void InstrumentedMessageProcessor_WriteStreams_Metadata_Test(int itemCount)
    {
        DataStream[] receivedMessages = null;
        var streamMessages = new DataStream[itemCount];
        var expectedMessages = new DataStream[itemCount];
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();

        var metaDataDict = new Dictionary<string, object>
        {
            { EdgeSystemConstants.AdapterTypeString, TestComponentType },
        };

        var existingMetaDataDict = new Dictionary<string, object>
        {
            { "TestString", "TestObject" },
        };

        mockOmfMessageProcessor.Setup(mp => mp.WriteStreams(It.IsAny<DataStream[]>(), It.IsAny<MessageAction>()))
            .Callback((DataStream[] dataStreams, MessageAction messageAction) => receivedMessages = dataStreams);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType)
        {
            StreamMetadataLevel = MetadataInfo.Low,
        };

        for (int i = 0; i < itemCount; i++)
        {
            streamMessages[i] = new DataStream { Id = TestStreamIdBase + i, Metadata = existingMetaDataDict };
            expectedMessages[i] = new DataStream { Id = TestStreamIdBase + i, Metadata = metaDataDict, DataSource = TestComponentId, };
        }

        instrumentedMessageProcessor.WriteStreams(streamMessages, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.True(instrumentedMessageProcessor.GetStreamCount() == itemCount);
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(itemCount, receivedMessages.Length);

        for (int j = 0; j < itemCount; j++)
        {
            Assert.Equal(expectedMessages[j].Metadata.Count + 1, receivedMessages[j].Metadata.Count);
            Assert.Equal(expectedMessages[j].Metadata[EdgeSystemConstants.AdapterTypeString], receivedMessages[j].Metadata[EdgeSystemConstants.AdapterTypeString]);
            Assert.Equal(expectedMessages[j].DataSource, receivedMessages[j].DataSource);
            Assert.Equal("TestObject", receivedMessages[j].Metadata["TestString"]);
        }

        instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType)
        {
            StreamMetadataLevel = MetadataInfo.None,
        };

        instrumentedMessageProcessor.WriteStreams(streamMessages, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.True(instrumentedMessageProcessor.GetStreamCount() == itemCount);
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());
        Assert.Equal(itemCount, receivedMessages.Length);

        for (int j = 0; j < itemCount; j++)
        {
            Assert.Empty(receivedMessages[j].Metadata);
        }
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStream_DataSource_Test()
    {
        DataStream receivedMessage = null;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var testLogger = new TestLogger();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedMessage = dataStream);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, testLogger, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase }, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(1, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.NotNull(receivedMessage);
        Assert.Equal(TestComponentId, receivedMessage.DataSource);
        Assert.Empty(testLogger.GetLogMessages());
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStream_DataSource_CustomValue_Test()
    {
        const string DataSourceName = "Datasource1";
        DataStream receivedMessage = null;
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var testLogger = new TestLogger();

        var expectedLogMessage = $"Overwriting message '{TestStreamIdBase}' with datasource property '{DataSourceName}' to match Component Id '{TestComponentId}'.";
        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedMessage = dataStream);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, testLogger, TestComponentId, TestComponentType);

        instrumentedMessageProcessor.WriteStream(new DataStream { Id = TestStreamIdBase, DataSource = DataSourceName }, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(1, instrumentedMessageProcessor.GetStreamCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.NotNull(receivedMessage);
        Assert.Equal(TestComponentId, receivedMessage.DataSource);

        var logMessages = testLogger.GetLogMessages();
        Assert.Single(logMessages);
        Assert.Equal(expectedLogMessage, logMessages.First().LogMessage);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    public void InstrumentedMessageProcessor_WriteStreams_DataSource_Test(int itemCount)
    {
        DataStream[] receivedMessages = null;
        var streamMessages = new DataStream[itemCount];
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var testLogger = new TestLogger();

        mockOmfMessageProcessor.Setup(mp => mp.WriteStreams(It.IsAny<DataStream[]>(), It.IsAny<MessageAction>()))
            .Callback((DataStream[] dataStreams, MessageAction messageAction) => receivedMessages = dataStreams);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, testLogger, TestComponentId, TestComponentType);

        for (int i = 0; i < itemCount; i++)
        {
            streamMessages[i] = new DataStream { Id = TestStreamIdBase + i };
        }

        instrumentedMessageProcessor.WriteStreams(streamMessages, MessageAction.Default);

        Assert.True(instrumentedMessageProcessor.GetTypeCount() == 0);
        Assert.True(instrumentedMessageProcessor.GetStreamCount() == itemCount);
        Assert.True(instrumentedMessageProcessor.GetAndResetEventsCounter() == 0);
        Assert.Equal(itemCount, receivedMessages.Length);

        for (int j = 0; j < itemCount; j++)
        {
            Assert.Equal(TestComponentId, receivedMessages[j].DataSource);
        }

        Assert.Empty(testLogger.GetLogMessages());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    public void InstrumentedMessageProcessor_WriteStreams_DataSource_CustomValue_Test(int itemCount)
    {
        const string DataSourceName = "Datasource1";
        DataStream[] receivedMessages = null;
        var streamMessages = new DataStream[itemCount];
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var testLogger = new TestLogger();

        mockOmfMessageProcessor.Setup(mp => mp.WriteStreams(It.IsAny<DataStream[]>(), It.IsAny<MessageAction>()))
            .Callback((DataStream[] dataStreams, MessageAction messageAction) => receivedMessages = dataStreams);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, testLogger, TestComponentId, TestComponentType);

        for (int i = 0; i < itemCount; i++)
        {
            streamMessages[i] = new DataStream { Id = TestStreamIdBase + i, DataSource = DataSourceName + i };
        }

        instrumentedMessageProcessor.WriteStreams(streamMessages, MessageAction.Default);

        Assert.True(instrumentedMessageProcessor.GetTypeCount() == 0);
        Assert.True(instrumentedMessageProcessor.GetStreamCount() == itemCount);
        Assert.True(instrumentedMessageProcessor.GetAndResetEventsCounter() == 0);
        Assert.Equal(itemCount, receivedMessages.Length);

        for (int j = 0; j < itemCount; j++)
        {
            Assert.Equal(TestComponentId, receivedMessages[j].DataSource);
        }

        var logMessages = testLogger.GetLogMessages();
        Assert.Equal(itemCount, logMessages.Count);

        for (int j = 0; j < itemCount; j++)
        {
            var expectedLogMessage = $"Overwriting message '{TestStreamIdBase + j}' with datasource property '{DataSourceName + j}' to match Component Id '{TestComponentId}'.";
            Assert.Equal(expectedLogMessage, logMessages[j].LogMessage);
        }
    }

    [Theory]
    [MemberData(nameof(DataSelectionConfigsRemovedItems))]
    [MemberData(nameof(DataSelectionConfigsUnselectedItems))]
    public void InstrumentedMessageProcessor_ProcessDataSelectionConfigurationChanges_Test(IDataSelectionConfiguration[] configs)
    {
        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var receivedStreams = new List<DataStream>();
        var testLogger = new TestLogger();
        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, testLogger, TestComponentId, TestComponentType);
        var defaultConfig = new DataStream[13];

        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedStreams.Add(dataStream));

        for (int i = 0; i < 10; i++)
        {
            defaultConfig[i] = new DataStream("Hello", i.ToString(CultureInfo.InvariantCulture), "Hello");
        }

        for (char i = 'a'; i < 'd'; i++)
        {
            defaultConfig[i - 'a' + 10] = new DataStream("Hello", i.ToString(CultureInfo.InvariantCulture), "Hello");
        }

        instrumentedMessageProcessor.WriteStreams(defaultConfig, MessageAction.Default);
        instrumentedMessageProcessor.ProcessDataSelectionConfigurationChanges(configs);

        int expectedStreams = 0;
        var configStreamIds = configs.Where(x => x.Selected).Select(x => x.StreamId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in defaultConfig)
        {
            if (configStreamIds.Contains(item.Id))
            {
                expectedStreams++;
            }
        }

        Assert.Equal(expectedStreams, instrumentedMessageProcessor.GetStreamCount());
        instrumentedMessageProcessor.ResendTypesAndStreams();
        Assert.Equal(expectedStreams, receivedStreams.Count);

        foreach (var item in receivedStreams)
        {
            Assert.Contains(item.Id, configStreamIds);
        }
    }

    [Fact]
    public void InstrumentedMessageProcessor_ProcessDataSelectionConfigurationChanges_PreservesOrderAfterRemoval_Test()
    {
        var resentStreamIds = new List<string>();

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => resentStreamIds.Add(dataStream.Id));

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, _mLogger.Object, TestComponentId, TestComponentType);

        // Cache streams 0..9 in order.
        for (var i = 0; i < 10; i++)
        {
            instrumentedMessageProcessor.WriteStream(new DataStream("Hello", i.ToString(CultureInfo.InvariantCulture), "Hello"), MessageAction.Default);
        }

        // Remove the even-numbered streams from the selection, keeping the odd ones.
        var configs = Enumerable.Range(0, 10)
            .Where(i => i % 2 == 1)
            .Select(i => (IDataSelectionConfiguration)new TestDataSelectionItem(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        instrumentedMessageProcessor.ProcessDataSelectionConfigurationChanges(configs);

        // Isolate the resend output from the initial writes captured by the same singular mock.
        resentStreamIds.Clear();
        instrumentedMessageProcessor.ResendTypesAndStreams();

        // Survivors are resent in their original relative order (evens removed, odds keep their sequence).
        var survivingIds = new[] { "1", "3", "5", "7", "9" };
        var expectedOrder = survivingIds
            .Select(id => id.ToPrefixedOmfIdentifier(null))
            .ToList();

        Assert.Equal(expectedOrder, resentStreamIds);
    }

    [Theory]
    [InlineData(StreamProperties.All)]
    [InlineData(StreamProperties.Interpolation)]
    [InlineData(StreamProperties.Description)]
    [InlineData(StreamProperties.Minimum)]
    [InlineData(StreamProperties.Maximum)]
    [InlineData(StreamProperties.Uom)]
    [InlineData(StreamProperties.Uom | StreamProperties.Interpolation)]
    [InlineData(StreamProperties.Uom | StreamProperties.Interpolation | StreamProperties.Maximum)]
    public void InstrumentedMessageProcessor_WriteStreams_StreamProperties_Test(StreamProperties properties)
    {
        var maximum = 100.0;
        var minimum = 1.0;
        var description = "hi";
        var interpolation = Interpolation.Discrete;
        var uom = "ft";

        DataStream receivedMessage = null;
        var streamMessage = new DataStream("Hello", "hi", "hey")
        {
            Description = description,
        };
        
        var propertyOverrides = new PropertyDefinitionOverride
        {
            Maximum = maximum,
            Minimum = minimum,
            Description = description,
            Interpolation = interpolation,
            Uom = uom,
        };

        streamMessage.PropertyOverrides = new Dictionary<string, PropertyDefinitionOverride>
        {
            { "value", propertyOverrides },
        };

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var testLogger = new TestLogger();

        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedMessage = dataStream);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, testLogger, TestComponentId, TestComponentType)
        {
            IncludeSourceProperties = properties,
        };

        instrumentedMessageProcessor.WriteStream(streamMessage, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        var receivedProperties = receivedMessage.PropertyOverrides["value"];

        AssertProperty(receivedProperties.Maximum, properties.HasFlag(StreamProperties.Maximum), maximum);
        AssertProperty(receivedProperties.Minimum, properties.HasFlag(StreamProperties.Minimum), minimum);
        AssertProperty(receivedProperties.Description, properties.HasFlag(StreamProperties.Description), description);
        AssertProperty(receivedProperties.Interpolation, properties.HasFlag(StreamProperties.Interpolation), interpolation);
        AssertProperty(receivedProperties.Uom, properties.HasFlag(StreamProperties.Uom), uom);
        if (properties.HasFlag(StreamProperties.Description))
        {
            Assert.Equal(description, receivedMessage.Description);
        }
        else
        {
            Assert.Null(receivedMessage.Description);
        }

        Assert.Empty(testLogger.GetLogMessages());
    }

    [Fact]
    public void InstrumentedMessageProcessor_WriteStreams_StreamProperties_NoProperties_Test()
    {
        DataStream receivedMessage = null;
        var streamMessage = new DataStream("Hello", "hi", "hey");

        var mockOmfMessageProcessor = new Mock<IMessageProcessor>();
        var testLogger = new TestLogger();

        mockOmfMessageProcessor.Setup(mp => mp.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()))
            .Callback((DataStream dataStream, MessageAction messageAction) => receivedMessage = dataStream);

        var instrumentedMessageProcessor = new InstrumentedMessageProcessor(mockOmfMessageProcessor.Object, testLogger, TestComponentId, TestComponentType);
        instrumentedMessageProcessor.IncludeSourceProperties = StreamProperties.Uom;

        instrumentedMessageProcessor.WriteStream(streamMessage, MessageAction.Default);

        Assert.Equal(0, instrumentedMessageProcessor.GetTypeCount());
        Assert.Equal(0, instrumentedMessageProcessor.GetAndResetEventsCounter());

        Assert.NotNull(receivedMessage);
        Assert.ThrowsAny<NullReferenceException>(() => receivedMessage.PropertyOverrides["value"]);

        receivedMessage = null;
        streamMessage.PropertyOverrides = [];
        
        instrumentedMessageProcessor.WriteStream(streamMessage, MessageAction.Default);

        Assert.NotNull(receivedMessage);
        Assert.ThrowsAny<KeyNotFoundException>(() => receivedMessage.PropertyOverrides["value"]);

        Assert.Empty(testLogger.GetLogMessages());
    }

    private static void AssertProperty<T>(T actual, bool condition, T expected)
    {
        if (condition)
        {
            Assert.Equal(expected, actual);
        }
        else
        {
            Assert.Null(actual);
        }
    }
}
