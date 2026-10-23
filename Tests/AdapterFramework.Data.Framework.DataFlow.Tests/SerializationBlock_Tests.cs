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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(entitiesArray.Length, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
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

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(eventsArray.Length, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
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

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(linksArray.Length, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
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

        using var serializationBlock = new SerializationBlock(null, null, instanceMessageByteCount + (isMessageOversize ? -1 : 1), testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(dataItems.Count, _receivedDataCount);
        Assert.Equal(isMessageOversize ? 2 : 1, _messageActionTriggerCount);
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

        using var serializationBlock = new SerializationBlock(null, null, maxByteCount, testLogger, -1, new OmfJsonSerializer(), null, DummyAction, new CancellationToken());

        serializationBlock.Post(instanceMessage);

        Assert.False(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), WaitTime));
        Assert.Equal(eventsArray.Length + entitiesArray.Length + linksArray.Length + dataItems.Count, _receivedDataCount);
        Assert.Equal(isNonStreamingDataOversize ? 3 : (isMessageOversize ? 2 : 1), _messageActionTriggerCount);
    }

    [Fact]
    public void SerializationBlock_Post_InstanceMessage_OverSize_RelationshipsCollocatedWithInstances()
    {
        const int GroupCount = 4;
        var testLogger = new TestLogger();
        var events = new Event[GroupCount];
        var entities = new StaticStreamData[GroupCount];
        var links = new Link[GroupCount];

        for (var i = 0; i < GroupCount; i++)
        {
            events[i] = new Event { TypeId = "EventType", Id = $"ev-{i}", Value = new { V = i } };
            entities[i] = new StaticStreamData { Id = "EntityType", InstanceId = $"en-{i}", Value = new { V = i } };
        }

        // Reverse order so a naive positional split would separate links from their instances.
        for (var i = 0; i < GroupCount; i++)
        {
            var n = GroupCount - 1 - i;
            links[i] = new Link(
                new RelationshipLinkNode($"ev-{n}", "EventType", collection: Tokens.EventsCollection),
                new RelationshipLinkNode($"en-{n}", "EntityType", collection: Tokens.EntitiesCollection));
        }

        var rentedEvents = ArrayPool<Event>.Shared.Rent(GroupCount);
        var rentedEntities = ArrayPool<StaticStreamData>.Shared.Rent(GroupCount);
        var rentedLinks = ArrayPool<Link>.Shared.Rent(GroupCount);
        events.CopyTo(rentedEvents, 0);
        entities.CopyTo(rentedEntities, 0);
        links.CopyTo(rentedLinks, 0);

        var totalByteCount = new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            Events = new ArraySegment<Event>(rentedEvents, 0, GroupCount),
            Entities = new ArraySegment<StaticStreamData>(rentedEntities, 0, GroupCount),
            Relationships = new ArraySegment<Link>(rentedLinks, 0, GroupCount),
        }).Length;

        var flushed = new ConcurrentQueue<ISerializedOmfMessage>();
        using var instanceMessage = new InstanceMessage(null, rentedEntities, rentedEvents, rentedLinks, 0, 0, GroupCount, GroupCount, GroupCount, MessageAction.Default, true);
        using var serializationBlock = new SerializationBlock(null, null, totalByteCount / 2, testLogger, -1, new OmfJsonSerializer(), null, flushed.Enqueue, new CancellationToken());

        serializationBlock.Post(instanceMessage);

        Assert.True(SpinWait.SpinUntil(() => flushed.Sum(m => m.ItemCount) == GroupCount * 3, WaitTime));
        Assert.False(testLogger.AreErrorsWarningsInLog());
        Assert.True(flushed.Count > 1);

        foreach (var message in flushed)
        {
            using var document = JsonDocument.Parse(message.MessageBody);
            var instanceIds = new HashSet<string>();

            foreach (var collection in new[] { Tokens.EventsCollection, Tokens.EntitiesCollection })
            {
                if (document.RootElement.TryGetProperty(collection, out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        instanceIds.Add(item.GetProperty(Tokens.Id).GetString());
                    }
                }
            }

            if (document.RootElement.TryGetProperty("relationships", out var relationships))
            {
                foreach (var relationship in relationships.EnumerateArray())
                {
                    Assert.Contains(relationship.GetProperty(Tokens.Source).GetProperty(Tokens.Id).GetString(), instanceIds);
                }
            }
        }
    }

    #region Instance message splitting

    private const string NullId = "(null)";
    private const string EndMarkerId = "end-of-test-marker";

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_LinksStayWithTheirSourceOnly()
    {
        // Each event is the source of two links, whose targets are entities elsewhere in the message.
        // Items are interleaved so a positional split would separate links from their source.
        var events = new[] { NewEvent("e3"), NewEvent("e1"), NewEvent("e2"), NewEvent("e0") };
        var entities = new[] { NewEntity("n3"), NewEntity("n1"), NewEntity("n2"), NewEntity("n0") };
        var links = new[]
        {
            NewLink("e3", "n0"), NewLink("e1", "n2"), NewLink("e2", "n3"), NewLink("e0", "n1"),
            NewLink("e3", "n1"), NewLink("e1", "n3"), NewLink("e2", "n0"), NewLink("e0", "n2"),
        };

        var maxByteCount = SerializedSize(events.Take(1), [], links.Take(2)) + 10;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage(events, entities, links), maxByteCount, 16, logger);

        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.True(flushed.Count > 1);
        AssertFlushedExactlyOnce(flushed, events, entities, links);
        AssertWithinLimit(flushed, maxByteCount);
        AssertLinksCollocated(flushed, "e0", "e1", "e2", "e3");

        // Targets are not considered: entities are never pulled into the message of the link which points at them.
        var e0Message = Assert.Single(flushed, m => m.EventIds.Contains("e0"));
        Assert.Equal(["e0->n1", "e0->n2"], e0Message.Links.Select(l => $"{l.Source}->{l.Target}").Order(StringComparer.Ordinal));
        Assert.Empty(e0Message.EntityIds.Intersect(["n1", "n2"]));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_GroupLargerThanLimit_SplitsWithWarning()
    {
        // One hub event which is the source of many links forms a single group that cannot fit in one message.
        var events = new[] { NewEvent("e1", 100), NewEvent("e0", 100) };
        var entities = Enumerable.Range(0, 11).Select(i => NewEntity($"n{i}", 100)).ToArray();
        var links = new[] { NewLink("e1", "n10") }.Concat(Enumerable.Range(0, 10).Select(i => NewLink("e0", $"n{i}"))).ToArray();

        var hubSize = SerializedSize(events.Where(e => e.Id == "e0"), [], links.Where(l => l.Source.Index == "e0"));
        var maxByteCount = hubSize / 3;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage(events, entities, links, MessageAction.Delete), maxByteCount, events.Length + entities.Length + links.Length, logger);

        var logs = logger.GetLogMessages();
        var warning = Assert.Single(logs, l => l.LogLevel == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("related events, entities and relationships", warning.LogMessage);
        Assert.DoesNotContain(logs, l => l.LogLevel >= Microsoft.Extensions.Logging.LogLevel.Error);
        AssertFlushedExactlyOnce(flushed, events, entities, links);
        AssertWithinLimit(flushed, maxByteCount);
        Assert.All(flushed, m => Assert.Equal(MessageAction.Delete, m.Action));

        // A group which had to be split is flushed as events, then relationships.
        var firstEventMessage = flushed.FindIndex(m => m.EventIds.Contains("e0"));
        var firstLinkMessage = flushed.FindIndex(m => m.Links.Any(l => l.Source == "e0"));
        Assert.True(firstEventMessage >= 0 && firstEventMessage < firstLinkMessage);

        // The other event is still collocated with its link even though the hub group had to be split.
        var pairMessage = Assert.Single(flushed, m => m.EventIds.Contains("e1"));
        Assert.Contains(pairMessage.Links, l => l.Source == "e1" && l.Target == "n10");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_SeveralGroupsLargerThanLimit_LogsOneWarning()
    {
        var events = new[] { NewEvent("hubA", 100), NewEvent("hubB", 100) };
        var entities = Enumerable.Range(0, 8).Select(i => NewEntity($"a{i}", 100))
            .Concat(Enumerable.Range(0, 8).Select(i => NewEntity($"b{i}", 100)))
            .ToArray();
        var links = Enumerable.Range(0, 8).Select(i => NewLink("hubA", $"a{i}"))
            .Concat(Enumerable.Range(0, 8).Select(i => NewLink("hubB", $"b{i}")))
            .ToArray();

        var maxByteCount = SerializedSize(events.Take(1), [], links.Take(8)) / 3;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage(events, entities, links), maxByteCount, events.Length + entities.Length + links.Length, logger);

        var warning = Assert.Single(logger.GetLogMessages(), l => l.LogLevel >= Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, warning.LogLevel);
        AssertFlushedExactlyOnce(flushed, events, entities, links);
        AssertWithinLimit(flushed, maxByteCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_UnevenGroupSizes_MessagesAreWellFilled()
    {
        // Each event is the source of a link, so the first groups are much heavier per item than the lone entities that follow.
        // Chunks cut by item count would overshoot the limit and be halved, leaving most messages about half full.
        const int Count = 3000;
        var events = Enumerable.Range(0, Count).Select(i => NewEvent($"e{i}", 20)).ToArray();
        var entities = Enumerable.Range(0, Count).Select(i => NewEntity($"n{i}", 20)).ToArray();
        var links = Enumerable.Range(0, Count).Select(i => NewLink($"e{(i * 7) % Count}", $"n{i}")).ToArray();

        var total = SerializedSize(events, entities, links);
        var maxByteCount = total / 25;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage(events, entities, links), maxByteCount, 3 * Count, logger);

        Assert.False(logger.AreErrorsWarningsInLog());
        AssertFlushedExactlyOnce(flushed, events, entities, links);
        AssertWithinLimit(flushed, maxByteCount);
        AssertLinksCollocated(flushed, [.. events.Select(e => e.Id)]);

        // Aiming at 90% of the limit, all but the last message should be close to it.
        var fill = flushed.Sum(m => (double)m.ByteLength) / (flushed.Count * (double)maxByteCount);
        Assert.True(fill > 0.8, $"Messages are only {fill:P0} full on average ({flushed.Count} messages).");
        Assert.True(flushed.Count <= 30, $"Expected about 28 messages, got {flushed.Count}.");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_UnevenItemSizes_AreSplitIntoMultipleMessages()
    {
        // A few very large items among many small ones: no single item count per message fits both kinds.
        var entities = Enumerable.Range(0, 4).Select(i => NewEntity($"big{i}", 2000))
            .Concat(Enumerable.Range(0, 12).Select(i => NewEntity($"small{i}", 5)))
            .ToArray();
        const int MaxByteCount = 5000;
        Assert.True(SerializedSize([], entities, []) > MaxByteCount);
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage([], entities, []), MaxByteCount, entities.Length, logger);

        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.True(flushed.Count >= 3);
        AssertFlushedExactlyOnce(flushed, [], entities, []);
        AssertWithinLimit(flushed, MaxByteCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_GroupContainingItemLargerThanLimit_LogsErrorAndFlushesTheRest()
    {
        // The oversize entity is the source of a link, so they form one group which has to be split item by item.
        // Splitting a single item which is still too big must stop with an error instead of recursing forever.
        var entities = new[] { NewEntity("huge", 3000), NewEntity("s0"), NewEntity("s1") };
        var links = new[] { NewLink("huge", "s0") };
        const int MaxByteCount = 1000;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage([], entities, links), MaxByteCount, 3, logger);

        Assert.Contains(logger.GetLogMessages(), l => l.LogLevel == Microsoft.Extensions.Logging.LogLevel.Error && l.LogMessage.Contains("too big"));
        AssertFlushedExactlyOnce(flushed, [], entities.Where(e => e.InstanceId != "huge"), links);
        AssertWithinLimit(flushed, MaxByteCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_SingleItemLargerThanLimit_LogsErrorAndFlushesTheRest()
    {
        var entities = new[] { NewEntity("s0"), NewEntity("s1"), NewEntity("huge", 3000), NewEntity("s2") };
        const int MaxByteCount = 1000;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage([], entities, []), MaxByteCount, 3, logger);

        Assert.Contains(logger.GetLogMessages(), l => l.LogLevel == Microsoft.Extensions.Logging.LogLevel.Error && l.LogMessage.Contains("too big"));
        AssertFlushedExactlyOnce(flushed, [], entities.Where(e => e.InstanceId != "huge"), []);
        AssertWithinLimit(flushed, MaxByteCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_UnresolvableNullAndDuplicateIds_AreHandled()
    {
        var events = new[] { NewEvent("e0"), NewEvent(null), NewEvent("d"), NewEvent("d") };
        var entities = new[] { NewEntity("n0"), NewEntity("d"), NewEntity(null) }
            .Concat(Enumerable.Range(0, 20).Select(i => NewEntity($"f{i}", 50)))
            .ToArray();
        var links = new[]
        {
            NewLink("e0", "n0"),
            NewLink("unknownA", "unknownB"), // source references nothing in the message
            NewLink("e0", "unknownC"),       // the target is not in the message
            NewLink("unknownD", "n0"),       // only the target is in the message, which does not matter
            NewLink("d", "n0"),              // the source id is shared by two events and an entity
        };

        var total = SerializedSize(events, entities, links);
        var maxByteCount = total / 3;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage(events, entities, links), maxByteCount, events.Length + entities.Length + links.Length, logger);

        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.True(flushed.Count > 1);
        AssertFlushedExactlyOnce(flushed, events, entities, links);
        AssertWithinLimit(flushed, maxByteCount);

        var linkedMessage = Assert.Single(flushed, m => m.Links.Any(l => l.Source == "e0" && l.Target == "n0"));
        Assert.Contains("e0", linkedMessage.EventIds);
        Assert.Contains(linkedMessage.Links, l => l.Source == "e0" && l.Target == "unknownC");

        // Instances sharing an id (including across events and entities) are kept together with the links they are the source of.
        var duplicateMessage = Assert.Single(flushed, m => m.EventIds.Contains("d"));
        Assert.Equal(2, duplicateMessage.EventIds.Count(id => id == "d"));
        Assert.Contains("d", duplicateMessage.EntityIds);
        Assert.Contains(duplicateMessage.Links, l => l.Source == "d" && l.Target == "n0");
    }

    [Theory]
    [InlineData(60, 0, 0)]
    [InlineData(0, 60, 0)]
    [InlineData(0, 0, 60)]
    [InlineData(30, 30, 0)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_WithoutLinksOrWithoutInstances_Success(int eventCount, int entityCount, int linkCount)
    {
        var events = Enumerable.Range(0, eventCount).Select(i => NewEvent($"e{i}", 30)).ToArray();
        var entities = Enumerable.Range(0, entityCount).Select(i => NewEntity($"n{i}", 30)).ToArray();
        var links = Enumerable.Range(0, linkCount).Select(i => NewLink($"n{i}", $"e{i}")).ToArray();

        var maxByteCount = SerializedSize(events, entities, links) / 4;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage(events, entities, links), maxByteCount, eventCount + entityCount + linkCount, logger);

        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.True(flushed.Count > 1);
        AssertFlushedExactlyOnce(flushed, events, entities, links);
        AssertWithinLimit(flushed, maxByteCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_WithStreamingData_StreamingDataKeepsPartitionKeyAndAction()
    {
        var events = Enumerable.Range(0, 15).Select(i => NewEvent($"e{i}", 50)).ToArray();
        var entities = Enumerable.Range(0, 15).Select(i => NewEntity($"n{i}", 50)).ToArray();
        var links = Enumerable.Range(0, 15).Select(i => NewLink($"n{i}", $"e{i}")).ToArray();
        var streamingData = new[]
        {
            new StreamingDataInstance { Id = "stream-1", Values = new List<object> { new { V = 1 }, new { V = 2 }, new { V = 3 } } },
        };

        var maxByteCount = SerializedSize(events, entities, links) / 3;
        var logger = new TestLogger();
        var message = NewInstanceMessage(events, entities, links, MessageAction.Update, streamingData, 3, PartitionKey.Key5);

        var flushed = FlushInstanceMessage(message, maxByteCount, 48, logger);

        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.All(flushed, m => Assert.Equal(MessageAction.Update, m.Action));
        var streamingMessage = Assert.Single(flushed, m => m.HasStreamingData);
        Assert.Equal(3, streamingMessage.ItemCount);
        Assert.Equal(PartitionKey.Key5, streamingMessage.PartitionKey);

        var instanceMessages = flushed.Where(m => !m.HasStreamingData).ToList();
        Assert.True(instanceMessages.Count > 1);
        AssertFlushedExactlyOnce(instanceMessages, events, entities, links);
        AssertWithinLimit(instanceMessages, maxByteCount);
        AssertLinksCollocated(instanceMessages, [.. events.Select(e => e.Id), .. entities.Select(e => e.InstanceId)]);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_WithCompressor_CompressesEveryMessage()
    {
        var events = Enumerable.Range(0, 20).Select(i => NewEvent($"e{i}", 40)).ToArray();
        var entities = Enumerable.Range(0, 20).Select(i => NewEntity($"n{i}", 40)).ToArray();
        var links = Enumerable.Range(0, 20).Select(i => NewLink($"n{i}", $"e{i}")).ToArray();

        var compressor = new Mock<ICompressor>();
        compressor.Setup(c => c.Compress(It.IsAny<byte[]>())).Returns((byte[] data) => data);

        var maxByteCount = SerializedSize(events, entities, links) / 3;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(NewInstanceMessage(events, entities, links), maxByteCount, 60, logger, compressor.Object);

        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.True(flushed.Count > 1);
        AssertFlushedExactlyOnce(flushed, events, entities, links);
        compressor.Verify(c => c.Compress(It.IsAny<byte[]>()), Times.AtLeast(flushed.Count));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    public void SerializationBlock_Post_InstanceMessage_OverSize_ArraysLargerThanCounts_OnlyCountedItemsAreFlushed()
    {
        var events = new[] { NewEvent("e0"), NewEvent("e1"), NewEvent("ghost-e") };
        var entities = new[] { NewEntity("n0"), NewEntity("n1"), NewEntity("ghost-n") };
        var links = new[] { NewLink("n0", "e0"), NewLink("n1", "e1"), NewLink("ghost-n", "ghost-e") };
        var message = new InstanceMessage(null, entities, events, links, 0, 0, 2, 2, 2, MessageAction.Default);

        var maxByteCount = SerializedSize(events.Take(2), entities.Take(2), links.Take(2)) * 3 / 4;
        var logger = new TestLogger();

        var flushed = FlushInstanceMessage(message, maxByteCount, 6, logger);

        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.True(flushed.Count > 1);
        AssertFlushedExactlyOnce(flushed, events.Take(2), entities.Take(2), links.Take(2));
        AssertWithinLimit(flushed, maxByteCount);
    }

    private sealed record FlushedInstanceMessage(
        int ItemCount,
        int ByteLength,
        MessageAction Action,
        PartitionKey? PartitionKey,
        bool HasStreamingData,
        List<string> EventIds,
        List<string> EntityIds,
        List<(string Source, string Target)> Links)
    {
        public bool ContainsInstance(string id) => EventIds.Contains(id) || EntityIds.Contains(id);
    }

    private static Event NewEvent(string id, int payloadLength = 10)
    {
        return new Event { TypeId = "EventType", Id = id, Value = new { Payload = new string('e', payloadLength) } };
    }

    private static StaticStreamData NewEntity(string id, int payloadLength = 10)
    {
        return new StaticStreamData { Id = "EntityType", InstanceId = id, Value = new { Payload = new string('n', payloadLength) } };
    }

    private static Link NewLink(string sourceId, string targetId)
    {
        return new Link(
            new RelationshipLinkNode(sourceId, "EventType", collection: Tokens.EventsCollection),
            new RelationshipLinkNode(targetId, "EntityType", collection: Tokens.EntitiesCollection));
    }

    private static InstanceMessage NewInstanceMessage(
        Event[] events,
        StaticStreamData[] entities,
        Link[] links,
        MessageAction messageAction = MessageAction.Default,
        StreamingDataInstance[] streamingData = null,
        int streamingDataTotalCount = 0,
        PartitionKey? partitionKey = null)
    {
        return new InstanceMessage(
            streamingData,
            entities,
            events,
            links,
            streamingData?.Length ?? 0,
            streamingDataTotalCount,
            entities.Length,
            events.Length,
            links.Length,
            messageAction,
            false,
            partitionKey);
    }

    private static int SerializedSize(IEnumerable<Event> events, IEnumerable<StaticStreamData> entities, IEnumerable<Link> links)
    {
        var eventArray = events.ToArray();
        var entityArray = entities.ToArray();
        var linkArray = links.ToArray();

        return new OmfJsonSerializer().Serialize(new InstanceMessageWrapper
        {
            Events = eventArray.Length == 0 ? default : new ArraySegment<Event>(eventArray),
            Entities = entityArray.Length == 0 ? default : new ArraySegment<StaticStreamData>(entityArray),
            Relationships = linkArray.Length == 0 ? default : new ArraySegment<Link>(linkArray),
        }).Length;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Disposed by SerializationBlock handler")]
    private static List<FlushedInstanceMessage> FlushInstanceMessage(
        InstanceMessage message,
        int maxByteCount,
        int expectedItemCount,
        TestLogger logger,
        ICompressor compressor = null)
    {
        var flushed = new ConcurrentQueue<ISerializedOmfMessage>();
        using var endMarkerFlushed = new ManualResetEventSlim(false);

        void OnFlush(ISerializedOmfMessage flushedMessage)
        {
            flushed.Enqueue(flushedMessage);

            if (ParseInstanceMessage(flushedMessage).EventIds.Contains(EndMarkerId))
            {
                endMarkerFlushed.Set();
            }
        }

        using var serializationBlock = new SerializationBlock(null, null, maxByteCount, logger, -1, new OmfJsonSerializer(), compressor, OnFlush, new CancellationToken());

        serializationBlock.Post(message);

        // The block handles messages in order, so once the marker has been flushed everything posted before it has been flushed too.
        serializationBlock.Post(NewInstanceMessage([NewEvent(EndMarkerId, 1)], [], []));

        Assert.True(endMarkerFlushed.Wait(WaitTime), "The messages were not flushed in time.");

        var result = flushed.Select(ParseInstanceMessage).Where(m => !m.EventIds.Contains(EndMarkerId)).ToList();
        Assert.Equal(expectedItemCount, result.Sum(m => m.ItemCount));

        return result;
    }

    private static FlushedInstanceMessage ParseInstanceMessage(ISerializedOmfMessage message)
    {
        using var document = JsonDocument.Parse(message.MessageBody);
        var root = document.RootElement;

        var links = new List<(string, string)>();
        if (root.TryGetProperty("relationships", out var relationships))
        {
            foreach (var relationship in relationships.EnumerateArray())
            {
                links.Add((
                    relationship.GetProperty(Tokens.Source).GetProperty(Tokens.Id).GetString(),
                    relationship.GetProperty(Tokens.Target).GetProperty(Tokens.Id).GetString()));
            }
        }

        return new FlushedInstanceMessage(
            message.ItemCount,
            message.MessageBody.Length,
            message.MessageAction,
            message.PartitionKey,
            root.TryGetProperty("streamingdata", out _),
            ReadIds(root, Tokens.EventsCollection),
            ReadIds(root, Tokens.EntitiesCollection),
            links);
    }

    private static List<string> ReadIds(JsonElement root, string collection)
    {
        if (!root.TryGetProperty(collection, out var items))
        {
            return [];
        }

        return [.. items.EnumerateArray().Select(item =>
            item.TryGetProperty(Tokens.Id, out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : NullId)];
    }

    private static void AssertFlushedExactlyOnce(
        List<FlushedInstanceMessage> flushed,
        IEnumerable<Event> events,
        IEnumerable<StaticStreamData> entities,
        IEnumerable<Link> links)
    {
        static IEnumerable<string> Sorted(IEnumerable<string> values) => values.OrderBy(v => v, StringComparer.Ordinal);

        Assert.Equal(Sorted(events.Select(e => e.Id ?? NullId)), Sorted(flushed.SelectMany(m => m.EventIds)));
        Assert.Equal(Sorted(entities.Select(e => e.InstanceId ?? NullId)), Sorted(flushed.SelectMany(m => m.EntityIds)));
        Assert.Equal(Sorted(links.Select(l => $"{l.Source.Index}->{l.Target.Index}")), Sorted(flushed.SelectMany(m => m.Links).Select(l => $"{l.Source}->{l.Target}")));
        Assert.All(flushed, m => Assert.Equal(m.EventIds.Count + m.EntityIds.Count + m.Links.Count, m.ItemCount));
    }

    private static void AssertWithinLimit(List<FlushedInstanceMessage> flushed, int maxByteCount)
    {
        Assert.All(flushed, m => Assert.True(m.ByteLength <= maxByteCount, $"Message of {m.ByteLength} bytes exceeds the limit of {maxByteCount}."));
    }

    // Every link must be in the same message as the instance its source references, if that instance is part of the batch.
    private static void AssertLinksCollocated(List<FlushedInstanceMessage> flushed, params string[] instanceIds)
    {
        var known = new HashSet<string>(instanceIds);

        foreach (var message in flushed)
        {
            foreach (var (source, _) in message.Links)
            {
                Assert.True(!known.Contains(source) || message.ContainsInstance(source), $"Source {source} is not in the same message as its link.");
            }
        }
    }

    #endregion

    private void DummyAction(ISerializedOmfMessage m)
    {
        _messageActionTriggerCount++;
        _receivedDataCount += m.ItemCount;        
    }
}
