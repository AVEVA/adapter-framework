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
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;
using AdapterFramework.Data.Framework.Serialization;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

public class SerializationBlock_Tests
{
    private const int WaitTime = 5000;
    private readonly DataType _dataType;
    private readonly DataStream _stream;
    private readonly DynamicStreamData _dynamicStreamData;
    private readonly StreamingDataInstance _streamingDataInstance;
    private readonly StaticStreamData _staticStreamData;
    private readonly Event _event;
    private readonly string _dataItem = "DummyDataItem";
    private readonly int _typeByteCount;
    private readonly int _containerByteCount;
    private readonly int _dynamicDataByteCount;
    private readonly int _staticDataByteCount;
    private int _receivedDataCount;
    private int _messageActionTriggerCount;
    private OmfResourceCounts _receivedResourceCounts;
    
    public SerializationBlock_Tests()
    {
        _receivedDataCount = 0;

        _dataType = new StaticDataType
        {
            Id = "TestID",
            Name = "TestType",
            Properties = new Dictionary<string, PropertyDefinition>(),
        };

        _stream = new DataStream
        {
            Id = "TestID",
            DataSource = "TestDataSource",
            Description = "TestDescription",
            TypeId = "TypeID",
        };

        _dynamicStreamData = new DynamicStreamData { Id = "12345", Values = new List<object> { _dataItem } };
        _streamingDataInstance = new StreamingDataInstance { Id = "12345", Values = new List<object> { _dataItem } };
        _staticStreamData = new StaticStreamData { Id = "TestId", Values = new List<object> { _dataItem } };
        _event = new Event { TypeId = "EventTypeId", Id = "EventId" };

        var serializer = new OmfJsonSerializer();

        _containerByteCount = serializer.Serialize(new[] { _stream }).Length;
        _typeByteCount = serializer.Serialize(new[] { _dataType }).Length;
        _dynamicDataByteCount = serializer.Serialize(new[] { _dynamicStreamData }).Length;
        _staticDataByteCount = serializer.Serialize(new[] { _staticStreamData }).Length;
    }

    [Theory]
    [InlineData(-1, -1, false)]
    [InlineData(2, -1, true)]
    public async Task SerializationBlock_Post_PostAsync_TypeMessages(int byteCount, int capacity, bool success)
    {
        var testLogger = new TestLogger();
        using var serializationBlock = new SerializationBlock(null, null, _typeByteCount + byteCount, testLogger, capacity,
            new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        var typeMessage = new OmfMessage<DataType>(2, new[] { _dataType, _dataType }, MessageAction.Default);

        serializationBlock.Post(typeMessage);

        Assert.True(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime) != success);
        Assert.Equal(success ? 2 : 0, _messageActionTriggerCount);

        testLogger.ClearLog();

        await serializationBlock.PostAsync(typeMessage);

        Assert.True(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime) != success);
        Assert.Equal(0, _receivedDataCount);
        Assert.Equal(success ? 4 : 0, _messageActionTriggerCount);
    }

    [Theory]
    [InlineData(-1, -1, false)]
    [InlineData(2, -1, true)]
    public async Task SerializationBlock_Post_PostAsync_ContainerMessages(int byteCount, int capacity, bool success)
    {
        var testLogger = new TestLogger();
        using var serializationBlock = new SerializationBlock(null, null, _containerByteCount + byteCount, testLogger, capacity,
            new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        var typeMessage = new OmfMessage<DataStream>(2, new[] { _stream, _stream }, MessageAction.Default);

        serializationBlock.Post(typeMessage);

        Assert.True(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime) != success);
        Assert.Equal(success ? 2 : 0, _messageActionTriggerCount);

        testLogger.ClearLog();

        await serializationBlock.PostAsync(typeMessage);

        Assert.True(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime) != success);
        Assert.Equal(0, _receivedDataCount);
        Assert.Equal(success ? 4 : 0, _messageActionTriggerCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SerializationBlock_Post_PostAsync_DataMessageTooBig_Error(bool dynamicData)
    {
        var testLogger = new TestLogger();
        using var serializationBlock = new SerializationBlock(null, null, dynamicData ? _dynamicDataByteCount - 1 : _staticDataByteCount - 1, testLogger, -1,
            new OmfJsonSerializer(), null, DummyAction, new CancellationToken());
        var dynamicOmfMessage = new OmfMessage<DynamicStreamData>(1, new[] { _dynamicStreamData }, MessageAction.Default);
        var staticOmfMessage = new OmfMessage<StaticStreamData>(1, new[] { _staticStreamData }, MessageAction.Default);

        if (dynamicData)
        {
            serializationBlock.Post(dynamicOmfMessage);
        }
        else
        {
            serializationBlock.Post(staticOmfMessage);
        }

        Assert.True(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime * 10));

        testLogger.ClearLog();

        if (dynamicData)
        {
            await serializationBlock.PostAsync(dynamicOmfMessage);
        }
        else
        {
            await serializationBlock.PostAsync(staticOmfMessage);
        }

        Assert.True(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SerializationBlock_Post_PostAsync_TwoDataMessagesTooBig_OneMessage_Success(bool dynamicData)
    {
        var testLogger = new TestLogger();
        using var serializationBlock = new SerializationBlock(null, null, dynamicData ? _dynamicDataByteCount + 2 : _staticDataByteCount + 2,
            testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        var dynamicOmfMessage = new OmfMessage<DynamicStreamData>(2, new[] { _dynamicStreamData, _dynamicStreamData }, MessageAction.Default);
        var staticOmfMessage = new OmfMessage<StaticStreamData>(2, new[] { _staticStreamData, _staticStreamData }, MessageAction.Default);

        var expectedCount = dynamicData
            ? _dynamicStreamData.Values.Count() * dynamicOmfMessage.Values.Length
            : _staticStreamData.Values.Count() * staticOmfMessage.Values.Length;

        if (dynamicData)
        {
            serializationBlock.Post(dynamicOmfMessage);
        }
        else
        {
            serializationBlock.Post(staticOmfMessage);
        }

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(2, _messageActionTriggerCount);
        Assert.Equal(expectedCount, _receivedDataCount);

        _receivedDataCount = 0;
        testLogger.ClearLog();

        if (dynamicData)
        {
            await serializationBlock.PostAsync(dynamicOmfMessage);
        }
        else
        {
            await serializationBlock.PostAsync(staticOmfMessage);
        }

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(4, _messageActionTriggerCount);
        Assert.Equal(expectedCount, _receivedDataCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>")]
    public async Task SerializationBlock_Post_PostAsync_OneDataMessage_Success(bool dynamicData)
    {
        var testLogger = new TestLogger();
        using var serializationBlock = new SerializationBlock(null, null, dynamicData ? _dynamicDataByteCount + 1 : _staticDataByteCount + 1,
            testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        var dataItems = new List<object> { _dataItem, _dataItem, _dataItem, _dataItem };

        _dynamicStreamData.Values = new List<object>(dataItems);
        _staticStreamData.Values = new List<object>(dataItems);

        var expectedCount = dataItems.Count;
        var dynamicOmfMessage = new OmfMessage<DynamicStreamData>(4, new[] { _dynamicStreamData }, MessageAction.Default);
        var staticOmfMessage = new OmfMessage<StaticStreamData>(4, new[] { _staticStreamData }, MessageAction.Default);

        if (dynamicData)
        {
            serializationBlock.Post(dynamicOmfMessage);
        }
        else
        {
            serializationBlock.Post(staticOmfMessage);
        }

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(expectedCount, _receivedDataCount);
        Assert.Equal(4, _messageActionTriggerCount);

        _receivedDataCount = 0;

        _dynamicStreamData.Values = new List<object>(dataItems);
        _staticStreamData.Values = new List<object>(dataItems);
        dynamicOmfMessage = new OmfMessage<DynamicStreamData>(4, new[] { _dynamicStreamData }, MessageAction.Default);
        staticOmfMessage = new OmfMessage<StaticStreamData>(4, new[] { _staticStreamData }, MessageAction.Default);

        if (dynamicData)
        {
            await serializationBlock.PostAsync(dynamicOmfMessage);
        }
        else
        {
            await serializationBlock.PostAsync(staticOmfMessage);
        }

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(expectedCount, _receivedDataCount);
        Assert.Equal(8, _messageActionTriggerCount);

        // OMF 1.2 data messages (including chunked ones) never carry OMF 2.0 resource counts.
        Assert.True(_receivedResourceCounts.IsEmpty);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_WithStreamingData_UpdatesDataOptimizer_Omf20()
    {
        using var callbackReceived = new ManualResetEventSlim(false);

        var dataOptimizer = new Mock<BatchingStrategyOptimizer>();

        var receivedItemCount = -1;
        var receivedByteCount = -1;
        var receivedOver = true;

        dataOptimizer.Setup(x => x.Update(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Callback<int, int, bool>((itemCount, byteCount, over) =>
            {
                receivedItemCount = itemCount;
                receivedByteCount = byteCount;
                receivedOver = over;
                callbackReceived.Set();
            });

        var serializer = new OmfJsonSerializer();
        var testLogger = new TestLogger();

        using var serializationBlock = new SerializationBlock(dataOptimizer.Object, null, int.MaxValue, testLogger, -1,
            serializer, null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        var message = new InstanceMessage(
            new[]
            {
                new StreamingDataInstance
                {
                    Id = "stream-1",
                    Values = new List<object>
                    {
                        new
                        {
                            Timestamp = System.DateTime.UtcNow,
                            Value = 42,
                        },
                    },
                },
            },
            System.Array.Empty<StaticStreamData>(),
            System.Array.Empty<Event>(),
            System.Array.Empty<Link>(),
            streamingDataObjectCount: 1,
            streamingDataTotalCount: 1,
            entitiesCount: 0,
            eventsCount: 0,
            relationshipCount: 0,
            messageAction: MessageAction.Default);

        serializationBlock.Post(message);

        Assert.True(callbackReceived.Wait(WaitTime), "Did not receive optimizer update callback.");
        Assert.Equal(1, receivedItemCount);
        Assert.True(receivedByteCount > 0);
        Assert.False(receivedOver);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_WithoutStreamingData_UpdatesDataOptimizer_Omf20()
    {
        using var callbackReceived = new ManualResetEventSlim(false);

        var dataOptimizer = new Mock<BatchingStrategyOptimizer>();

        var receivedItemCount = -1;
        var receivedByteCount = -1;
        var receivedOver = true;

        dataOptimizer.Setup(x => x.Update(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Callback<int, int, bool>((itemCount, byteCount, over) =>
            {
                receivedItemCount = itemCount;
                receivedByteCount = byteCount;
                receivedOver = over;
                callbackReceived.Set();
            });

        var serializer = new OmfJsonSerializer();
        var testLogger = new TestLogger();

        using var serializationBlock = new SerializationBlock(dataOptimizer.Object, null, int.MaxValue, testLogger, -1,
            serializer, null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        var message = new InstanceMessage(
            System.Array.Empty<StreamingDataInstance>(),
            new[]
            {
                new StaticStreamData
                {
                    Id = "entity-1",
                    Values = new List<object> { new { Name = "Sample" } },
                },
            },
            System.Array.Empty<Event>(),
            System.Array.Empty<Link>(),
            streamingDataObjectCount: 0,
            streamingDataTotalCount: 0,
            entitiesCount: 1,
            eventsCount: 0,
            relationshipCount: 0,
            messageAction: MessageAction.Default);

        serializationBlock.Post(message);

        Assert.True(callbackReceived.Wait(WaitTime), "Did not receive optimizer update callback.");
        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), 500));
        Assert.Equal(1, receivedItemCount);
        Assert.True(receivedByteCount > 0);
        Assert.False(receivedOver);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_SchemaMessage_UpdatesSchemaOptimizer_Omf20()
    {
        using var callbackReceived = new ManualResetEventSlim(false);

        var schemaOptimizer = new Mock<BatchingStrategyOptimizer>();

        var receivedItemCount = -1;
        var receivedByteCount = -1;
        var receivedOver = true;

        schemaOptimizer.Setup(x => x.Update(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Callback<int, int, bool>((itemCount, byteCount, over) =>
            {
                receivedItemCount = itemCount;
                receivedByteCount = byteCount;
                receivedOver = over;
                callbackReceived.Set();
            });

        var serializer = new OmfJsonSerializer();
        var testLogger = new TestLogger();

        using var serializationBlock = new SerializationBlock(null, schemaOptimizer.Object, int.MaxValue, testLogger, -1,
            serializer, null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        var schemaMessage = new SchemaMessage(
            new DataType[]
            {
                _dataType,
            },
            new[]
            {
                _stream,
            },
            System.Array.Empty<Link>(),
            typeCount: 1,
            containerCount: 1,
            relationshipCount: 0,
            messageAction: MessageAction.Create);

        serializationBlock.Post(schemaMessage);

        Assert.True(callbackReceived.Wait(WaitTime), "Did not receive schema optimizer update callback.");
        Assert.Equal(2, receivedItemCount);
        Assert.True(receivedByteCount > 0);
        Assert.False(receivedOver);
    }

    [Fact]
    public void SerializationBlock_Post_SchemaMessage_FitsInOneMessage_Types_Success()
    {
        var testLogger = new TestLogger();
        var typesArray = new[] { _dataType, _dataType, };

        var rentedArray = ArrayPool<DataType>.Shared.Rent(typesArray.Length);
        typesArray.CopyTo(rentedArray, 0);

        var schemaMessageByteCount = new OmfJsonSerializer().Serialize(new SchemaMessageWrapper
        {
            Types = new ArraySegment<DataType>(rentedArray, 0, typesArray.Length),
        }).Length;

        using var schemaMessage = new SchemaMessage(rentedArray, null, null, typesArray.Length, 0, 0, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, schemaMessageByteCount + 1, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(schemaMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(typesArray.Length, _receivedDataCount);
        Assert.Equal(1, _messageActionTriggerCount);
    }

    [Fact]
    public void SerializationBlock_Post_SchemaMessage_OverSize_Types_Success()
    {
        var testLogger = new TestLogger();
        var typesArray = new[] { _dataType, _dataType, };

        var rentedArray = ArrayPool<DataType>.Shared.Rent(typesArray.Length);
        typesArray.CopyTo(rentedArray, 0);

        var schemaMessageByteCount = new OmfJsonSerializer().Serialize(new SchemaMessageWrapper
        {
            Types = new ArraySegment<DataType>(rentedArray, 0, typesArray.Length),
        }).Length;

        using var schemaMessage = new SchemaMessage(rentedArray, null, null, typesArray.Length, 0, 0, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, schemaMessageByteCount - 1, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(schemaMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(typesArray.Length, _receivedDataCount);
        Assert.Equal(2, _messageActionTriggerCount);
    }

    [Fact]
    public void SerializationBlock_Post_SchemaMessage_FitsInOneMessage_Streams_Success()
    {
        var testLogger = new TestLogger();
        var streamsArray = new[] { _stream, _stream, };

        var rentedArray = ArrayPool<DataStream>.Shared.Rent(streamsArray.Length);
        streamsArray.CopyTo(rentedArray, 0);

        var schemaMessageByteCount = new OmfJsonSerializer().Serialize(new SchemaMessageWrapper
        {
            Streams = new ArraySegment<DataStream>(rentedArray, 0, streamsArray.Length),
        }).Length;

        using var schemaMessage = new SchemaMessage(null, rentedArray, null, 0, streamsArray.Length, 0, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, schemaMessageByteCount + 1, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(schemaMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(streamsArray.Length, _receivedDataCount);
        Assert.Equal(1, _messageActionTriggerCount);
    }

    [Fact]
    public void SerializationBlock_Post_SchemaMessage_OverSize_Streams_Success()
    {
        var testLogger = new TestLogger();
        var streamsArray = new[] { _stream, _stream, };

        var rentedArray = ArrayPool<DataStream>.Shared.Rent(streamsArray.Length);
        streamsArray.CopyTo(rentedArray, 0);

        var schemaMessageByteCount = new OmfJsonSerializer().Serialize(new SchemaMessageWrapper
        {
            Streams = new ArraySegment<DataStream>(rentedArray, 0, streamsArray.Length),
        }).Length;

        using var schemaMessage = new SchemaMessage(null, rentedArray, null, 0, streamsArray.Length, 0, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, schemaMessageByteCount - 1, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(schemaMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(streamsArray.Length, _receivedDataCount);
        Assert.Equal(2, _messageActionTriggerCount);
    }

    [Fact]
    public void SerializationBlock_Post_SchemaMessage_FitsInOneMessage_Relationships_Success()
    {
        var testLogger = new TestLogger();
        var link = new Link(new DataTypeLinkNode("sourceId", "sourceIndex"), new DataTypeLinkNode("targetId", "targetIndex"));
        var linksArray = new[] { link, link, };

        var rentedArray = ArrayPool<Link>.Shared.Rent(linksArray.Length);
        linksArray.CopyTo(rentedArray, 0);

        var schemaMessageByteCount = new OmfJsonSerializer().Serialize(new SchemaMessageWrapper
        {
            Relationships = new ArraySegment<Link>(rentedArray, 0, linksArray.Length),
        }).Length;

        using var schemaMessage = new SchemaMessage(null, null, rentedArray, 0, 0, linksArray.Length, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, schemaMessageByteCount + 1, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(schemaMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(linksArray.Length, _receivedDataCount);
        Assert.Equal(1, _messageActionTriggerCount);
    }

    [Fact]
    public void SerializationBlock_Post_SchemaMessage_OverSize_Relationships_Success()
    {
        var testLogger = new TestLogger();
        var link = new Link(new DataTypeLinkNode("sourceId", "sourceIndex"), new DataTypeLinkNode("targetId", "targetIndex"));
        var linksArray = new[] { link, link, };

        var rentedArray = ArrayPool<Link>.Shared.Rent(linksArray.Length);
        linksArray.CopyTo(rentedArray, 0);

        var schemaMessageByteCount = new OmfJsonSerializer().Serialize(new SchemaMessageWrapper
        {
            Relationships = new ArraySegment<Link>(rentedArray, 0, linksArray.Length),
        }).Length;

        using var schemaMessage = new SchemaMessage(null, null, rentedArray, 0, 0, linksArray.Length, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, schemaMessageByteCount - 1, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(schemaMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(linksArray.Length, _receivedDataCount);
        Assert.Equal(2, _messageActionTriggerCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializationBlock_Post_InstanceMessage_Entities_Success(bool isMessageOversize)
    {
        var testLogger = new TestLogger();
        var entitiesArray = new[] { _staticStreamData, _staticStreamData, };

        var rentedArray = ArrayPool<StaticStreamData>.Shared.Rent(entitiesArray.Length);
        entitiesArray.CopyTo(rentedArray, 0);

        var instanceMessageByteCount = new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            Entities = new ArraySegment<StaticStreamData>(rentedArray, 0, entitiesArray.Length),
        }).Length;

        using var instanceMessage = new InstanceMessage(null, rentedArray, null, null, 0, 0, entitiesArray.Length, 0, 0, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(entitiesArray.Length, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
        Assert.Equal(new OmfResourceCounts(0, entitiesArray.Length, 0), _receivedResourceCounts);
    }

    [Theory]
    [InlineData(OmfVersion.Omf12)]
    [InlineData(OmfVersion.Omf13)]
    public void SerializationBlock_Post_InstanceMessage_NonOmf20_NoResourceCounts(OmfVersion omfVersion)
    {
        var testLogger = new TestLogger();
        var entitiesArray = new[] { _staticStreamData, _staticStreamData, };

        var rentedArray = ArrayPool<StaticStreamData>.Shared.Rent(entitiesArray.Length);
        entitiesArray.CopyTo(rentedArray, 0);

        using var instanceMessage = new InstanceMessage(null, rentedArray, null, null, 0, 0, entitiesArray.Length, 0, 0, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, int.MaxValue, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken(), omfVersion);

        serializationBlock.Post(instanceMessage);

        Assert.True(SpinWait.SpinUntil(() => _messageActionTriggerCount == 1, WaitTime));
        Assert.Equal(entitiesArray.Length, _receivedDataCount);
        Assert.True(_receivedResourceCounts.IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializationBlock_Post_InstanceMessage_Events_Success(bool isMessageOversize)
    {
        var testLogger = new TestLogger();
        var eventsArray = new[] { _event, _event, };

        var rentedArray = ArrayPool<Event>.Shared.Rent(eventsArray.Length);
        eventsArray.CopyTo(rentedArray, 0);

        var instanceMessageByteCount = new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            Events = new ArraySegment<Event>(rentedArray, 0, eventsArray.Length),
        }).Length;

        using var instanceMessage = new InstanceMessage(null, null, rentedArray, null, 0, 0, 0, eventsArray.Length, 0, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(eventsArray.Length, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
        Assert.Equal(new OmfResourceCounts(0, 0, eventsArray.Length), _receivedResourceCounts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializationBlock_Post_InstanceMessage_Relationships_Success(bool isMessageOversize)
    {
        var testLogger = new TestLogger();
        var link = new Link(new DataTypeLinkNode("sourceId", "sourceIndex"), new DataTypeLinkNode("targetId", "targetIndex"));
        var linksArray = new[] { link, link, };

        var rentedArray = ArrayPool<Link>.Shared.Rent(linksArray.Length);
        linksArray.CopyTo(rentedArray, 0);

        var instanceMessageByteCount = new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            Relationships = new ArraySegment<Link>(rentedArray, 0, linksArray.Length),
        }).Length;

        using var instanceMessage = new InstanceMessage(null, null, null, rentedArray, 0, 0, 0, 0, linksArray.Length, MessageAction.Default, true);

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(linksArray.Length, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
        Assert.True(_receivedResourceCounts.IsEmpty);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, PartitionKey.Key5)]
    [InlineData(true, null)]
    [InlineData(true, PartitionKey.Key5)]
    public void SerializationBlock_Post_InstanceMessage_StreamingData_Success(bool isMessageOversize, PartitionKey? partitionKey)
    {
        var testLogger = new TestLogger();
        var dataItems = new List<object> { _dataItem, _dataItem, };

        _streamingDataInstance.Values = new List<object>(dataItems);
        var streamingDataArray = new[] { _streamingDataInstance };

        var rentedArray = ArrayPool<StreamingDataInstance>.Shared.Rent(streamingDataArray.Length);
        streamingDataArray.CopyTo(rentedArray, 0);

        var instanceMessageByteCount = new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            StreamingData = new ArraySegment<StreamData>(rentedArray, 0, streamingDataArray.Length),
        }).Length;

        using var instanceMessage = new InstanceMessage(rentedArray, null, null, null, streamingDataArray.Length, dataItems.Count, 0, 0, 0, MessageAction.Default, true, partitionKey);

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(dataItems.Count, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
        Assert.Equal(new OmfResourceCounts(dataItems.Count, 0, 0), _receivedResourceCounts);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, PartitionKey.Key5)]
    [InlineData(true, null)]
    [InlineData(true, PartitionKey.Key5)]
    [InlineData(true, PartitionKey.Key5, MessageAction.Default, true)]
    [InlineData(true, PartitionKey.Key5, MessageAction.Delete)]
    public void SerializationBlock_Post_InstanceMessage_AllParts_Success(bool isMessageOversize, PartitionKey? partitionKey, MessageAction messageAction = MessageAction.Default, bool isNonStreamingDataOversize = false)
    {
        var testLogger = new TestLogger();

        var eventsArray = new[] { _event, _event, };
        var rentedEventsArray = ArrayPool<Event>.Shared.Rent(eventsArray.Length);
        eventsArray.CopyTo(rentedEventsArray, 0);

        var entitiesArray = new[] { _staticStreamData, _staticStreamData, };
        var rentedEntitiesArray = ArrayPool<StaticStreamData>.Shared.Rent(entitiesArray.Length);
        entitiesArray.CopyTo(rentedEntitiesArray, 0);

        var link = new Link(new DataTypeLinkNode("sourceId", "sourceIndex"), new DataTypeLinkNode("targetId", "targetIndex"));
        var linksArray = new[] { link, link, };
        var rentedLinksArray = ArrayPool<Link>.Shared.Rent(linksArray.Length);
        linksArray.CopyTo(rentedLinksArray, 0);

        var dataItems = new List<object> { _dataItem, _dataItem, };
        _streamingDataInstance.Values = new List<object>(dataItems);
        var streamingDataArray = new[] { _streamingDataInstance };

        var rentedStreamingDataArray = ArrayPool<StreamingDataInstance>.Shared.Rent(streamingDataArray.Length);
        streamingDataArray.CopyTo(rentedStreamingDataArray, 0);

        var instanceMessageByteCount = new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            Entities = new ArraySegment<StaticStreamData>(rentedEntitiesArray, 0, entitiesArray.Length),
            Events = new ArraySegment<Event>(rentedEventsArray, 0, eventsArray.Length),
            Relationships = new ArraySegment<Link>(rentedLinksArray, 0, linksArray.Length),
            StreamingData = new ArraySegment<StreamData>(rentedStreamingDataArray, 0, streamingDataArray.Length),
        }).Length;

        var nonStreamingDataByteCount = new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            Entities = new ArraySegment<StaticStreamData>(rentedEntitiesArray, 0, entitiesArray.Length),
            Events = new ArraySegment<Event>(rentedEventsArray, 0, eventsArray.Length),
            Relationships = new ArraySegment<Link>(rentedLinksArray, 0, linksArray.Length),            
        }).Length;

        using var instanceMessage = new InstanceMessage(rentedStreamingDataArray, rentedEntitiesArray, rentedEventsArray, rentedLinksArray, streamingDataArray.Length, dataItems.Count, entitiesArray.Length, eventsArray.Length, linksArray.Length, messageAction, true, partitionKey);

        var maxByteCount = isNonStreamingDataOversize ? nonStreamingDataByteCount - 1 : (isMessageOversize ? instanceMessageByteCount - 1 : instanceMessageByteCount + 1);

        using var serializationBlock = new SerializationBlock(null, null, maxByteCount, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken(), OmfVersion.Omf20);

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(eventsArray.Length + entitiesArray.Length + linksArray.Length + dataItems.Count, _receivedDataCount);
        Assert.Equal(isNonStreamingDataOversize ? 3 : (isMessageOversize ? 2 : 1), _messageActionTriggerCount);

        // Relationships are excluded from resource counts; every other instance is counted exactly once however the message is split.
        Assert.Equal(new OmfResourceCounts(dataItems.Count, entitiesArray.Length, eventsArray.Length), _receivedResourceCounts);
    }

    private void DummyAction(ISerializedOmfMessage m)
    {
        _messageActionTriggerCount++;
        _receivedDataCount += m.ItemCount;
        _receivedResourceCounts = new OmfResourceCounts(
            _receivedResourceCounts.StreamingValues + m.ResourceCounts.StreamingValues,
            _receivedResourceCounts.Assets + m.ResourceCounts.Assets,
            _receivedResourceCounts.Events + m.ResourceCounts.Events);
    }
}
