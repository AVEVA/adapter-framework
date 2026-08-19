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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;
using static AdapterFramework.Data.Framework.Tests.Helper.TestUtilities;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>")]
public class TypesStreamsGroupingBlock_Tests
{
    private const int WaitTime = 1000;
    private const string CurrentTypeCountFieldName = "_currentTypeCount";
    private const string CurrentStreamCountFieldName = "_currentStreamCount";
    private const string StreamsMaximumCountFieldName = "_maxStreamsBatchCount";
    private const string TestTypeId = "TestTypeId";
    private const string TestTypeName = "TestTypeName";
    private const string TestStreamId = "TestStreamId";
    private const string TestStreamName = "TestStreamName";

    private readonly DynamicDataType _testDynamicDataType = new DynamicDataType(TestTypeId, TestTypeName, 
        new Dictionary<string, (Type, bool, bool, string)>
        {
            { "Timestamp", (typeof(DateTime), true, false, null) },
            { "Value", (typeof(int), false, false, null) },
        });
    private readonly DataStream _testDataStream = new DataStream(TestTypeId, TestStreamId, TestStreamName);
    private readonly OmfMessage<DataType> _testDynamicDataTypeMessageCreate;
    private readonly OmfMessage<DataType> _testDynamicDataTypeMessageUpdate;
    private readonly OmfMessage<DataType> _testDynamicDataTypeMessageDelete;
    private readonly OmfMessage<DataStream> _testDataStreamMessageCreate;
    private readonly OmfMessage<DataStream> _testDataStreamMessageUpdate;
    private readonly OmfMessage<DataStream> _testDataStreamMessageDelete;

    private bool _flushCalled;
    private int _flushCalledCounter;
    private MessageAction _flushMessageAction;

    public TypesStreamsGroupingBlock_Tests()
    {
        _testDynamicDataTypeMessageCreate = new OmfMessage<DataType>(1, new DataType[] { _testDynamicDataType }, MessageAction.Create);
        _testDynamicDataTypeMessageUpdate = new OmfMessage<DataType>(1, new DataType[] { _testDynamicDataType }, MessageAction.Update);
        _testDynamicDataTypeMessageDelete = new OmfMessage<DataType>(1, new DataType[] { _testDynamicDataType }, MessageAction.Delete);

        _testDataStreamMessageCreate = new OmfMessage<DataStream>(1, new[] { new DataStream(TestTypeId, TestStreamId, TestStreamName) }, MessageAction.Create);
        _testDataStreamMessageUpdate = new OmfMessage<DataStream>(1, new[] { new DataStream(TestTypeId, TestStreamId, TestStreamName) }, MessageAction.Update);
        _testDataStreamMessageDelete = new OmfMessage<DataStream>(1, new[] { new DataStream(TestTypeId, TestStreamId, TestStreamName) }, MessageAction.Delete);
    }

    [Theory]
    [InlineData(0, 1000, 1000, 1000)]
    [InlineData(1000, 0, 1000, 1000)]
    [InlineData(1000, 1000, 0, 1000)]
    [InlineData(1000, 1000, 1000, 0)]
    public void TypesStreamsGroupingBlock_Constructor_InvalidInput_Throws_Test(int capacity, int typesBatchCount, int streamsBatchCount, int maxFlushTime)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TypesStreamsGroupingBlock(new Mock<ILogger>().Object,
            capacity, DummyFlushAction, streamsBatchCount, typesBatchCount, maxFlushTime,
            CancellationToken.None));
    }

    [Fact]
    public void TypesStreamsGroupingBlock_Constructor_NullActions_Throws_Test()
    {
        var baseValue = 1000;
        Assert.Throws<ArgumentNullException>(() =>
            new TypesStreamsGroupingBlock(null, baseValue, null, baseValue, baseValue, baseValue,
                CancellationToken.None));
    }

    [Fact]
    public async Task TypesStreamsGroupingBlock_Post_Single_ValidateGroupingCount_Test()
    {
        var maximumTypesStreamsCount = 1000;
        var blockCapacity = 1000;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, int.MaxValue, CancellationToken.None);

        var count = 500;
        for (var i = 0; i < count; i++)
        {
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageCreate);
        }

        await Task.Delay(WaitTime);

        var currentTypeCountField = GetFieldValueFromObject(CurrentTypeCountFieldName, typesStreamsGroupingBlock);

        Assert.Equal(count, currentTypeCountField);
        Assert.False(_flushCalled);

        for (var i = 0; i < count; i++)
        {
            typesStreamsGroupingBlock.Post(_testDataStreamMessageCreate);
        }

        await Task.Delay(WaitTime);

        var currentStreamCountField = GetFieldValueFromObject(CurrentStreamCountFieldName, typesStreamsGroupingBlock);

        Assert.Equal(count, currentStreamCountField);
        Assert.True(_flushCalled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task TypesStreamsGroupingBlock_Post_Single_FlushDueToSizeLimit_SizeLimits_Test(int excessCount)
    {
        var maximumTypesStreamsCount = 500;
        var blockCapacity = 1000;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, int.MaxValue, CancellationToken.None);

        for (int i = 0; i < maximumTypesStreamsCount + excessCount; i++)
        {
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageCreate);
        }

        await Task.Delay(WaitTime);

        var currentTypeCountField = GetFieldValueFromObject(CurrentTypeCountFieldName, typesStreamsGroupingBlock);
        Assert.True(_flushCalled);
        Assert.Equal(excessCount, currentTypeCountField);
        Assert.True(_flushCalledCounter == 1);
        Assert.Equal(_testDynamicDataTypeMessageCreate.MessageAction, _flushMessageAction);
        _flushCalled = false;

        for (int i = 0; i < maximumTypesStreamsCount + excessCount; i++)
        {
            typesStreamsGroupingBlock.Post(_testDataStreamMessageCreate);
        }

        await Task.Delay(WaitTime);

        var currentStreamCountField = GetFieldValueFromObject(CurrentStreamCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(excessCount, currentStreamCountField);
        Assert.True(_flushCalledCounter == (excessCount == 0 ? 2 : 3));
        Assert.Equal(_testDynamicDataTypeMessageCreate.MessageAction, _flushMessageAction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task TypesStreamsGroupingBlock_Post_Multiple_FlushDueToSizeLimit_SizeLimits_Test(int excessCount)
    {
        var maximumTypesStreamsCount = 500;
        var blockCapacity = 1000;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, int.MaxValue, CancellationToken.None);

        var typesArray = new DynamicDataType[maximumTypesStreamsCount + excessCount];
        for (int i = 0; i < maximumTypesStreamsCount + excessCount; i++)
        {
            typesArray[i] = _testDynamicDataType;
        }

        typesStreamsGroupingBlock.Post(new OmfMessage<DataType>(typesArray.Length, typesArray, MessageAction.Create));

        await Task.Delay(WaitTime);

        var currentTypeCountField = GetFieldValueFromObject(CurrentTypeCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(excessCount, currentTypeCountField);
        Assert.True(_flushCalledCounter == 1);

        _flushCalled = false;

        var streamsArray = new DataStream[maximumTypesStreamsCount + excessCount];
        for (int i = 0; i < maximumTypesStreamsCount + excessCount; i++)
        {
            streamsArray[i] = _testDataStream;
        }

        typesStreamsGroupingBlock.Post(new OmfMessage<DataStream>(streamsArray.Length, streamsArray, MessageAction.Create));

        await Task.Delay(WaitTime);

        var currentStreamCountField = GetFieldValueFromObject(CurrentStreamCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(excessCount, currentStreamCountField);
        Assert.True(_flushCalledCounter == (excessCount == 0 ? 2 : 3));
        Assert.Equal(MessageAction.Create, _flushMessageAction);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task TypesStreamsGroupingBlock_PostAsync_Multiple_FlushDueToSizeLimit_SizeLimits_Test(int excessCount)
    {
        var maximumTypesStreamsCount = 500;
        var blockCapacity = 1000;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, int.MaxValue, CancellationToken.None);

        var typesArray = new DynamicDataType[maximumTypesStreamsCount + excessCount];
        for (int i = 0; i < maximumTypesStreamsCount + excessCount; i++)
        {
            typesArray[i] = _testDynamicDataType;
        }

        await typesStreamsGroupingBlock.PostAsync(new OmfMessage<DataType>(typesArray.Length, typesArray, MessageAction.Create));

        await Task.Delay(WaitTime);

        var currentTypeCountField = GetFieldValueFromObject(CurrentTypeCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(excessCount, currentTypeCountField);
        Assert.True(_flushCalledCounter == 1);

        _flushCalled = false;

        var streamsArray = new DataStream[maximumTypesStreamsCount + excessCount];
        for (int i = 0; i < maximumTypesStreamsCount + excessCount; i++)
        {
            streamsArray[i] = _testDataStream;
        }

        await typesStreamsGroupingBlock.PostAsync(new OmfMessage<DataStream>(streamsArray.Length, streamsArray, MessageAction.Create));

        await Task.Delay(WaitTime);

        var currentStreamCountField = GetFieldValueFromObject(CurrentStreamCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(excessCount, currentStreamCountField);
        Assert.True(_flushCalledCounter == (excessCount == 0 ? 2 : 3));
        Assert.Equal(MessageAction.Create, _flushMessageAction);
    }

    [Fact]
    public async Task TypesStreamsGroupingBlock_Post_Multiple_FlushDueToTimeLimit_Test()
    {
        var maximumTypesStreamsCount = 500;
        var typesStreamsToPostCount = 42;
        var blockCapacity = 1000;
        var maxFlushTime = 1000;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, maxFlushTime, CancellationToken.None);

        for (int i = 0; i < typesStreamsToPostCount; i++)
        {
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageCreate);
        }

        await Task.Delay(maxFlushTime * 2);

        var currentTypeCountField = GetFieldValueFromObject(CurrentTypeCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(0, currentTypeCountField);
        Assert.True(_flushCalledCounter == 1);

        _flushCalled = false;

        for (int i = 0; i < typesStreamsToPostCount; i++)
        {
            typesStreamsGroupingBlock.Post(_testDataStreamMessageCreate);
        }

        await Task.Delay(maxFlushTime * 2);

        var currentStreamCountField = GetFieldValueFromObject(CurrentStreamCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(0, currentStreamCountField);
        Assert.True(_flushCalledCounter == 2);
        Assert.Equal(_testDynamicDataTypeMessageCreate.MessageAction, _flushMessageAction);
    }

    [Fact]
    public async Task TypesStreamsGroupingBlock_Post_Multiple_FlushMultiplePipelinesDueToTimeLimit_Test()
    {
        var maximumTypesStreamsCount = 500;
        var typesStreamsToPostCount = 42;
        var blockCapacity = 1000;
        var maxFlushTime = 1000;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, maxFlushTime, CancellationToken.None);

        for (int i = 0; i < typesStreamsToPostCount; i++)
        {
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageCreate);
        }

        await Task.Delay(maxFlushTime * 2);

        var currentTypeCountField = GetFieldValueFromObject(CurrentTypeCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(0, currentTypeCountField);
        Assert.Equal(1, _flushCalledCounter);

        _flushCalled = false;

        for (int i = 0; i < typesStreamsToPostCount; i++)
        {
            typesStreamsGroupingBlock.Post(_testDataStreamMessageCreate);
        }

        await Task.Delay(maxFlushTime * 2);

        var currentStreamCountField = GetFieldValueFromObject(CurrentStreamCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(0, currentStreamCountField);
        Assert.True(_flushCalledCounter == 2);
        Assert.Equal(_testDynamicDataTypeMessageCreate.MessageAction, _flushMessageAction);
    }

    [Fact]
    public async Task TypesStreamsGroupingBlock_FlushRequiredPipelines_Test()
    {
        var blockCapacity = 1000;
        var maxTypesAndStreams = 2;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            2, 2, int.MaxValue, CancellationToken.None);

        for (int i = 0; i < 1; i++)
        {
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageCreate);
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageUpdate);
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageDelete);
        }

        for (int i = 0; i < maxTypesAndStreams; i++)
        {
            typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageUpdate);
        }

        await Task.Delay(500);

        var currentTypeCountField = GetFieldValueFromObject(CurrentTypeCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(0, currentTypeCountField);
        Assert.Equal(4, _flushCalledCounter);
        Assert.Equal(_testDynamicDataTypeMessageUpdate.MessageAction, _flushMessageAction);

        _flushCalled = false;

        for (int i = 0; i < 1; i++)
        {
            typesStreamsGroupingBlock.Post(_testDataStreamMessageCreate);
            typesStreamsGroupingBlock.Post(_testDataStreamMessageUpdate);
            typesStreamsGroupingBlock.Post(_testDataStreamMessageDelete);
        }

        for (int i = 0; i < maxTypesAndStreams; i++)
        {
            typesStreamsGroupingBlock.Post(_testDataStreamMessageCreate);
        }

        await Task.Delay(500);

        var currentStreamCountField = GetFieldValueFromObject(CurrentStreamCountFieldName, typesStreamsGroupingBlock);

        Assert.True(_flushCalled);
        Assert.Equal(0, currentStreamCountField);
        Assert.Equal(8, _flushCalledCounter);
        Assert.Equal(_testDataStreamMessageCreate.MessageAction, _flushMessageAction);
    }

    [Fact]
    public async Task TypesStreamsGroupingBlock_Post_StateMessage_Test()
    {
        var maximumTypesStreamsCount = 50_000;
        var desiredMaximumBatchCount = 35_000;
        var blockCapacity = 1000;
        var testLogger = new TestLogger();

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(testLogger, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, int.MaxValue, CancellationToken.None);

        var initialValue = GetFieldValueFromObject(StreamsMaximumCountFieldName, typesStreamsGroupingBlock);

        Assert.Equal(maximumTypesStreamsCount, initialValue);

        typesStreamsGroupingBlock.Post(new StateMessage(desiredMaximumBatchCount));

        await Task.Delay(WaitTime);

        var changedMaxValue = GetFieldValueFromObject(StreamsMaximumCountFieldName, typesStreamsGroupingBlock);

        Assert.Equal(desiredMaximumBatchCount, changedMaxValue);
        Assert.Single(testLogger.GetLogMessages());
    }

    [Fact]
    public async Task TypesStreamsGroupingBlock_Post_CommandMessage_Test()
    {
        var maximumTypesStreamsCount = 50_000;
        var blockCapacity = 1_000;

        using var typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(null, blockCapacity, DummyFlushAction,
            maximumTypesStreamsCount, maximumTypesStreamsCount, int.MaxValue, CancellationToken.None);

        // post one type message so we have something to flush
        typesStreamsGroupingBlock.Post(_testDynamicDataTypeMessageCreate);

        // post command message, but do not force flush
        typesStreamsGroupingBlock.Post(new CommandMessage(false));

        await Task.Delay(WaitTime);

        Assert.False(_flushCalled);

        // post command message and force flush operation
        typesStreamsGroupingBlock.Post(new CommandMessage(true));

        // Delay a little longer here to prevent intermittent test failures.
        await Task.Delay(WaitTime + 1000);

        Assert.True(_flushCalled);
        Assert.True(_flushCalledCounter == 1);
    }

    private void DummyFlushAction(Message message)
    {
        _flushCalled = true;
        _flushCalledCounter++;
        if (message is OmfMessage<DataType> dataMessage)
        {
            _flushMessageAction = dataMessage.MessageAction;
        }
        else if (message is OmfMessage<DataStream> streamMessage)
        {
            _flushMessageAction = streamMessage.MessageAction;
        }
    }
}
