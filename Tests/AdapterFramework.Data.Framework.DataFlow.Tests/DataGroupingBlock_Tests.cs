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

public class DataGroupingBlock_Tests
{
    private const int WaitTime = 1500;
    private const string CurrentCountFieldName = "_currentCount";
    private const string BatchCountFieldName = "_maxBatchCount";
    private const MessageAction DeleteMessageAction = MessageAction.Delete;
    private readonly DataMessage _dataMessage = new("Hello World", Classification.Dynamic, new Dictionary<string, string>(), DeleteMessageAction);
    private bool _flushDataCalled;
    private bool _flushStreamsCalled;
    private int _flushDataCalledCounter;
    private MessageAction _flushMessageAction;
    private DateTime _flushTypesStreamsCalledTimestamp;
    private DateTime _flushDataCalledTimestamp;

    public DataGroupingBlock_Tests()
    {
        _flushStreamsCalled = false;
        _flushDataCalled = false;
    }

    [Theory]
    [InlineData(0, 1000, 1000)]
    [InlineData(1000, 0, 1000)]
    [InlineData(1000, 1000, 0)]

    public void DataGroupingBlock_InvalidInput_Throws_Test(int capacity, int batchCount, int maxFlushTime)
    {
        var mockLogger = new Mock<ILogger>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, batchCount, maxFlushTime, new CancellationToken()));
    }

    [Fact]
    public void DataGroupingBlock_NullActions_Throws_Test()
    {
        var mockLogger = new Mock<ILogger>();

        Assert.Throws<ArgumentNullException>(() => new DataGroupingBlock(mockLogger.Object, 100, null,
            DummyFlushTypesStreamsAction, 1000, 10000, new CancellationToken()));
        Assert.Throws<ArgumentNullException>(() => new DataGroupingBlock(mockLogger.Object, 100,
            DummyFlushDataAction, null, 1000, 10000, new CancellationToken()));
    }

    [Fact]
    public async Task DataGroupingBlock_Post_ValidateGroupingCount_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 1000;
        var capacity = 1000;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        var count = 500;
        for (var i = 0; i < count; i++)
        {
            dataGroupingBlock.Post(_dataMessage);
        }

        await Task.Delay(WaitTime);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(count, groupedValuesCount);
        Assert.False(_flushStreamsCalled);
        Assert.False(_flushDataCalled);
    }

    [Fact]
    public async Task DataGroupingBlock_PostAsync_ValidateGroupingCount_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 1000;
        var capacity = 1000;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        var count = 500;
        for (var i = 0; i < count; i++)
        {
            await dataGroupingBlock.PostAsync(_dataMessage);
        }

        await Task.Delay(WaitTime);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(count, groupedValuesCount);
        Assert.False(_flushStreamsCalled);
        Assert.False(_flushDataCalled);
    }

    [Fact]
    public async Task DataGroupingBlock_Post_FlushDueToSizeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 1000;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        var count = 500;
        for (var i = 0; i < count; i++)
        {
            dataGroupingBlock.Post(_dataMessage);
        }

        await Task.Delay(WaitTime);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(0, groupedValuesCount);
        Assert.True(_flushStreamsCalled);
        Assert.True(_flushDataCalled);
    }

    [Fact]
    public async Task DataGroupingBlock_PostAsync_FlushDueToSizeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 1000;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        var count = 500;
        for (var i = 0; i < count; i++)
        {
            await dataGroupingBlock.PostAsync(_dataMessage);
        }

        await Task.Delay(WaitTime);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(0, groupedValuesCount);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushDataCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushDataCalledTimestamp);
    }

    [Fact]
    public async Task DataGroupingBlock_Post_FlushDueToSizeLimit_BulkMessage_LimitExceeded_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 100;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        var count = 1001;
        var values = new List<object>();
        for (var i = 0; i < count; i++)
        {
            values.Add(i);
        }

        var bulkedDataMessage = new BulkDataMessage("TestStream", Classification.Dynamic, values, MessageAction.Create);

        dataGroupingBlock.Post(bulkedDataMessage);

        await Task.Delay(WaitTime);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(1, groupedValuesCount);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushDataCalled);
        Assert.Equal(2, _flushDataCalledCounter);
    }

    [Fact]
    public async Task DataGroupingBlock_Post_FlushDueToSizeLimit_BulkMessage_LimitReached_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 1000;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        var count = 500;
        var values = new List<object>();
        for (var i = 0; i < count; i++)
        {
            values.Add(i);
        }

        var bulkedDataMessage = new BulkDataMessage("TestStream", Classification.Dynamic, values, MessageAction.Create);

        dataGroupingBlock.Post(bulkedDataMessage);

        await Task.Delay(WaitTime);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(0, groupedValuesCount);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushDataCalled);
        Assert.Equal(1, _flushDataCalledCounter);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushDataCalledTimestamp);
    }

    [Fact]
    public async Task DataGroupingBlock_Post_FlushDueToTimeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var maxFlushTime = 1000;
        var capacity = 1000;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, maxFlushTime, new CancellationToken());

        for (var i = 0; i < 42; i++)
        {
            dataGroupingBlock.Post(_dataMessage);
        }

        await Task.Delay(WaitTime);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(0, groupedValuesCount);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushDataCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushDataCalledTimestamp);
    }

    [Fact]
    public async Task DataGroupingBlock_Post_FlushDueToMessageActionChange_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var maxFlushTime = 1000;
        var capacity = 1000;

        using var dataGroupingBlock = new DataGroupingBlock(mockLogger.Object, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, maxFlushTime, new CancellationToken());

        for (var i = 0; i < 42; i++)
        {
            dataGroupingBlock.Post(_dataMessage);
        }

        Assert.False(_flushDataCalled);

        dataGroupingBlock.Post(new DataMessage(_dataMessage.Id, _dataMessage.Classification, _dataMessage.Instance, MessageAction.Update));

        await Task.Delay(200);
        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(1, groupedValuesCount);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushDataCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushDataCalledTimestamp);
        Assert.Equal(DeleteMessageAction, _flushMessageAction);

        dataGroupingBlock.Post(new DataMessage(_dataMessage.Id, _dataMessage.Classification, _dataMessage.Instance, DeleteMessageAction));
        await Task.Delay(200);
        Assert.Equal(MessageAction.Update, _flushMessageAction);

        dataGroupingBlock.Post(new DataMessage(_dataMessage.Id, _dataMessage.Classification, _dataMessage.Instance, MessageAction.Update));
        await Task.Delay(200);
        Assert.Equal(DeleteMessageAction, _flushMessageAction);
    }

    [Fact]
    public async Task DataGroupingBlock_Post_StateMessage_Test()
    {
        var maxBatchCount = 50_000;
        var maxFlushTime = 1000;
        var capacity = 1000;
        var desiredBatchCount = 40_000;
        var testLogger = new TestLogger();

        using var dataGroupingBlock = new DataGroupingBlock(testLogger, capacity, DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, maxFlushTime, new CancellationToken());

        var currentBatchCount = GetFieldValueFromObject(BatchCountFieldName, dataGroupingBlock);

        Assert.Equal(maxBatchCount, currentBatchCount);

        dataGroupingBlock.Post(new StateMessage(desiredBatchCount));

        await Task.Delay(WaitTime);

        currentBatchCount = GetFieldValueFromObject(BatchCountFieldName, dataGroupingBlock);

        Assert.Equal(desiredBatchCount, currentBatchCount);
        Assert.Single(testLogger.GetLogMessages());
    }

    [Fact]
    public async Task DataGroupingBlock_Post_CommandMessage_Test()
    {
        var maxBatchCount = 50_000;
        var capacity = 1000;
        var testLogger = new TestLogger();

        using var dataGroupingBlock = new DataGroupingBlock(testLogger, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        dataGroupingBlock.Post(_dataMessage);

        dataGroupingBlock.Post(new CommandMessage(false));

        await Task.Delay(WaitTime);

        Assert.False(_flushDataCalled);
        Assert.False(_flushStreamsCalled);

        var groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(1, groupedValuesCount);

        dataGroupingBlock.Post(new CommandMessage(true));

        await Task.Delay(WaitTime);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushStreamsCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushDataCalledTimestamp);

        groupedValuesCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(0, groupedValuesCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(1000)]
    public async Task DataGroupingBlock_Post_StaticMessages_Test(int staticMessageCount)
    {
        var maxBatchCount = 50_000;
        var capacity = 1000;
        var testLogger = new TestLogger();
        var extendedStaticDataBatchFieldName = "_staticMessages";
        var currentDataBatchFieldName = "_staticAndDynamicMessages";

        using var dataGroupingBlock = new DataGroupingBlock(testLogger, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        for (int i = 0; i < staticMessageCount; i++)
        {
            var extendedPropertyDefinitions = new Dictionary<string, PropertyDefinition> { { "ModelVersion", new PropertyDefinition { Name = $"Model{i}" } } };
            var staticDataMessage = new StaticDataMessage(_dataMessage.Id, "InstanceId", "Instance name", "Description", "DataSource", null, extendedPropertyDefinitions, null, null, _dataMessage.Instance, DeleteMessageAction);
            await dataGroupingBlock.PostAsync(staticDataMessage);
        }

        await Task.Delay(WaitTime);

        Assert.False(_flushDataCalled);
        Assert.False(_flushStreamsCalled);

        var currentCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);
        var staticDataBatch = GetFieldValueFromObject(extendedStaticDataBatchFieldName, dataGroupingBlock) as List<StaticDataMessage>;
        var currentBatch = GetFieldValueFromObject(currentDataBatchFieldName, dataGroupingBlock) as Dictionary<string, (Classification, List<object>)>;

        Assert.NotNull(staticDataBatch);
        Assert.Empty(currentBatch);
        Assert.Equal(staticMessageCount, staticDataBatch.Count);

        Assert.Equal(staticMessageCount, (int)currentCount);

        await dataGroupingBlock.PostAsync(new CommandMessage(true));

        await Task.Delay(WaitTime);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushStreamsCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushDataCalledTimestamp);
        Assert.Equal(DeleteMessageAction, _flushMessageAction);

        currentCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(0, currentCount);
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(10, 10)]
    [InlineData(1000, 1)]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    public async Task DataGroupingBlock_Post_StaticMessagesWithDynamic_Test(int staticMessageCount, int dynamicMessageCount)
    {
        var maxBatchCount = 50_000;
        var capacity = 1001;
        var testLogger = new TestLogger();
        var extendedStaticDataBatchFieldName = "_staticMessages";
        var currentBatchFieldName = "_staticAndDynamicMessages";

        using var dataGroupingBlock = new DataGroupingBlock(testLogger, capacity,
            DummyFlushDataAction, DummyFlushTypesStreamsAction, maxBatchCount, int.MaxValue, new CancellationToken());

        for (int i = 0; i < staticMessageCount; i++)
        {
            var extendedPropertyDefinitions = new Dictionary<string, PropertyDefinition> { { "ModelVersion", new PropertyDefinition { Name = $"Model{i}" } } };
            var staticDataMessage = new StaticDataMessage(_dataMessage.Id, "InstanceId", "Instance name", "Description", null, null, extendedPropertyDefinitions, null, null, _dataMessage.Instance, DeleteMessageAction);
            await dataGroupingBlock.PostAsync(staticDataMessage);
        }

        for (int i = 0; i < dynamicMessageCount; i++)
        {
            await dataGroupingBlock.PostAsync(new DataMessage($"Hello World{i}", Classification.Dynamic, i, DeleteMessageAction));
        }

        await Task.Delay(WaitTime);

        Assert.False(_flushDataCalled);
        Assert.False(_flushStreamsCalled);

        var currentCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);
        var staticDataBatch = GetFieldValueFromObject(extendedStaticDataBatchFieldName, dataGroupingBlock) as List<StaticDataMessage>;
        var currentBatch = GetFieldValueFromObject(currentBatchFieldName, dataGroupingBlock) as Dictionary<string, (Classification, List<object>)>;

        if (staticMessageCount != 0)
        {
            Assert.NotNull(staticDataBatch);
            Assert.Equal(staticMessageCount, staticDataBatch.Count);
        }
        else
        {
            Assert.Null(staticDataBatch);
        }

        if (dynamicMessageCount != 0)
        {
            Assert.NotNull(currentBatch);
        }

        Assert.Equal(dynamicMessageCount, currentBatch.Count);

        Assert.Equal(staticMessageCount + dynamicMessageCount, (int)currentCount);

        dataGroupingBlock.Post(new CommandMessage(true));

        await Task.Delay(WaitTime);

        Assert.True(_flushStreamsCalled);
        Assert.True(_flushStreamsCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushDataCalledTimestamp);
        Assert.Equal(DeleteMessageAction, _flushMessageAction);

        currentCount = GetFieldValueFromObject(CurrentCountFieldName, dataGroupingBlock);

        Assert.Equal(0, currentCount);
    }

    private void DummyFlushDataAction(Message m)
    {
        _flushDataCalledTimestamp = DateTime.UtcNow;
        _flushDataCalled = true;
        _flushDataCalledCounter++;

        if (m is GroupedDataMessage groupedMessage)
        {
            _flushMessageAction = groupedMessage.MessageAction;
        }
    }

    private void DummyFlushTypesStreamsAction(Message m)
    {
        _flushTypesStreamsCalledTimestamp = DateTime.UtcNow;
        _flushStreamsCalled = true;
    }
}
