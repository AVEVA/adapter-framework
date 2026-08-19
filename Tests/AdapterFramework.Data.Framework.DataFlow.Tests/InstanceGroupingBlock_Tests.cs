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
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;
using static AdapterFramework.Data.Framework.Tests.Helper.TestUtilities;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

public class InstanceGroupingBlock_Tests
{
    private const int WaitTime = 5000;
    private const int SpinWaitTimeout = 5000;
    private const string TotalInstanceCountFieldName = "_totalInstanceCount";
    private const string BatchCountFieldName = "_maxBatchCount";
    private const MessageAction DeleteMessageAction = MessageAction.Delete;

    private readonly DataMessage _dataMessage = new("StreamId", Classification.Dynamic, new Dictionary<string, string>(), DeleteMessageAction);

    private bool _flushCalled;
    private bool _flushTypesStreamsCalled;
    private int _flushCalledCounter;
    private MessageAction _flushMessageAction;
    private DateTime _flushTypesStreamsCalledTimestamp;
    private DateTime _flushCalledTimestamp;
    private InstanceMessage _lastFlushedInstanceMessage;

    public InstanceGroupingBlock_Tests()
    {
        _flushTypesStreamsCalled = false;
        _flushCalled = false;
        _flushCalledCounter = 0;
        _lastFlushedInstanceMessage = null;
    }

    #region Constructor Tests

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1000, 14)]
    public void InstanceGroupingBlock_InvalidInput_Throws_Test(int batchCount, int maxFlushTime)
    {
        var mockLogger = new Mock<ILogger>();

        Assert.Throws<ArgumentOutOfRangeException>(() => new InstanceGroupingBlock(
            mockLogger.Object,
            100,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            batchCount,
            maxFlushTime,
            CancellationToken.None));
    }

    [Fact]
    public void InstanceGroupingBlock_NullActions_Throws_Test()
    {
        var mockLogger = new Mock<ILogger>();

        Assert.Throws<ArgumentNullException>(() => new InstanceGroupingBlock(
            mockLogger.Object,
            100,
            null,
            DummyFlushTypesStreamsAction,
            1000,
            10000,
            CancellationToken.None));

        Assert.Throws<ArgumentNullException>(() => new InstanceGroupingBlock(
            mockLogger.Object,
            100,
            DummyFlushAction,
            null,
            1000,
            10000,
            CancellationToken.None));
    }

    #endregion

    #region DataMessage Tests

    [Fact]
    public void InstanceGroupingBlock_Post_DataMessage_ValidateGroupingCount_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 1000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var count = 500;
        for (var i = 0; i < count; i++)
        {
            block.Post(_dataMessage);
        }

        Thread.Sleep(WaitTime);

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);

        Assert.Equal(count, totalInstanceCount);
        Assert.False(_flushTypesStreamsCalled);
        Assert.False(_flushCalled);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_DataMessage_FlushDueToSizeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var count = 500;
        for (var i = 0; i < count; i++)
        {
            block.Post(_dataMessage);
        }

        Assert.True(SpinWait.SpinUntil(() => _flushCalled, SpinWaitTimeout));

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);

        Assert.Equal(0, totalInstanceCount);
        Assert.True(_flushTypesStreamsCalled);
        Assert.True(_flushCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushCalledTimestamp);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_DataMessage_FlushDueToTimeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var maxFlushTime = 500;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            maxFlushTime,
            CancellationToken.None);

        for (var i = 0; i < 42; i++)
        {
            block.Post(_dataMessage);
        }

        Assert.True(SpinWait.SpinUntil(() => _flushCalled, SpinWaitTimeout));

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);

        Assert.Equal(0, totalInstanceCount);
        Assert.True(_flushTypesStreamsCalled);
        Assert.True(_flushCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushCalledTimestamp);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_DataMessage_FlushDueToMessageActionChange_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (var i = 0; i < 42; i++)
        {
            block.Post(_dataMessage);
        }

        Assert.False(_flushCalled);

        block.Post(new DataMessage(_dataMessage.Id, _dataMessage.Classification, _dataMessage.Instance, MessageAction.Update));

        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _flushMessageAction == MessageAction.Delete, SpinWaitTimeout));
        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);

        Assert.Equal(1, totalInstanceCount);
        Assert.True(_flushTypesStreamsCalled);
        Assert.True(_flushCalled);
        Assert.Equal(MessageAction.Delete, _flushMessageAction);

        block.Post(_dataMessage);
        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _flushMessageAction == MessageAction.Update, SpinWaitTimeout));
    }

    #endregion

    #region BulkDataMessage Tests

    [Fact]
    public void InstanceGroupingBlock_Post_BulkDataMessage_UnderLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var values = new List<object>();
        for (var i = 0; i < 100; i++)
        {
            values.Add(i);
        }

        var bulkedDataMessage = new BulkDataMessage("TestStream", Classification.Dynamic, values, MessageAction.Create);
        block.Post(bulkedDataMessage);

        Thread.Sleep(WaitTime);

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);

        Assert.Equal(100, totalInstanceCount);
        Assert.False(_flushCalled);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_BulkDataMessage_ExceedsLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 100;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var values = new List<object>();
        for (var i = 0; i < 1001; i++)
        {
            values.Add(i);
        }

        var bulkedDataMessage = new BulkDataMessage("TestStream", Classification.Dynamic, values, MessageAction.Create);
        block.Post(bulkedDataMessage);

        Assert.True(SpinWait.SpinUntil(() => _flushCalledCounter == 2, SpinWaitTimeout));

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);

        Assert.Equal(1, totalInstanceCount);
        Assert.True(_flushCalled);
        Assert.Equal(2, _flushCalledCounter);
    }

    #endregion

    #region StaticDataMessage Tests

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void InstanceGroupingBlock_Post_StaticDataMessage_Test(int staticMessageCount)
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < staticMessageCount; i++)
        {
            var extendedPropertyDefinitions = new Dictionary<string, PropertyDefinition>
            {
                { "ModelVersion", new PropertyDefinition { Name = $"Model{i}" } }
            };
            var staticDataMessage = new StaticDataMessage(
                "TypeId",
                $"InstanceId{i}",
                "Instance name",
                "Description",
                "DataSource",
                null,
                extendedPropertyDefinitions,
                null,
                null,
                new { Value = i },
                DeleteMessageAction);

            block.Post(staticDataMessage);
        }

        Thread.Sleep(WaitTime);

        Assert.False(_flushCalled);
        Assert.False(_flushTypesStreamsCalled);

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);
        Assert.Equal(staticMessageCount, (int)totalInstanceCount);

        block.Post(new CommandMessage(true));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.True(_flushTypesStreamsCalled);
        Assert.True(_flushCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(staticMessageCount, _lastFlushedInstanceMessage.EntitiesCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_StaticDataMessage_FlushDueToSizeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < 50; i++)
        {
            var staticDataMessage = new StaticDataMessage(
                "TypeId",
                $"InstanceId{i}",
                "Instance name",
                "Description",
                "DataSource",
                null,
                null,
                null,
                null,
                new { Value = i },
                MessageAction.Create);

            block.Post(staticDataMessage);
        }

        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.True(_flushCalled);
        Assert.True(_flushTypesStreamsCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(50, _lastFlushedInstanceMessage.EntitiesCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_StaticDataMessage_WithRelationships_RelationshipsIncludedInFlush_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var source = new DataTypeLinkNode("SourceType", "SourceIndex");
        var target = new DataTypeLinkNode("TargetType", "TargetIndex");
        var link = new Link(source, target);

        var staticDataMessage = new StaticDataMessage(
            "TypeId",
            "InstanceId",
            "Name",
            "Description",
            "DataSource",
            null,
            null,
            null,
            null,
            new { Value = 1 },
            MessageAction.Create)
        {
            Relationships = new List<Link> { link }
        };

        block.Post(staticDataMessage);
        block.Post(new CommandMessage(true));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.Equal(1, _lastFlushedInstanceMessage.EntitiesCount);
        Assert.Equal(1, _lastFlushedInstanceMessage.RelationshipCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_StaticDataMessage_FlushDueToMessageActionChange_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 500;
        var capacity = 1000;
        var maxFlushTime = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            maxFlushTime,
            CancellationToken.None);

        for (var i = 0; i < 5; i++)
        {
            block.Post(new StaticDataMessage(
                "TypeId",
                $"InstanceId{i}",
                "Name",
                "Description",
                "DataSource",
                null,
                null,
                null,
                null,
                new { Value = i },
                MessageAction.Delete));
        }

        Assert.False(_flushCalled);

        block.Post(new StaticDataMessage(
            "TypeId",
            "InstanceId_New",
            "Name",
            "Description",
            "DataSource",
            null,
            null,
            null,
            null,
            new { Value = 99 },
            MessageAction.Create));

        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _flushMessageAction == MessageAction.Create, SpinWaitTimeout));

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);

        Assert.True(_flushTypesStreamsCalled);
        Assert.Equal(MessageAction.Create, _flushMessageAction);
        Assert.Equal(0, totalInstanceCount);
        Assert.Equal(2, _flushCalledCounter);
    }

    #endregion

    #region EventMessage Tests

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void InstanceGroupingBlock_Post_EventMessage_Test(int eventMessageCount)
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < eventMessageCount; i++)
        {
            var eventMessage = new EventMessage(
                "EventTypeId",
                $"EventId{i}",
                $"Event {i}",
                $"Description {i}",
                "DataSource",
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                null,
                new { Value = i },
                null,
                MessageAction.Create);

            block.Post(eventMessage);
        }

        Thread.Sleep(WaitTime);

        Assert.False(_flushCalled);
        Assert.False(_flushTypesStreamsCalled);

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);
        Assert.Equal(eventMessageCount, (int)totalInstanceCount);

        block.Post(new CommandMessage(true));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null && eventMessageCount == _lastFlushedInstanceMessage.EventsCount, SpinWaitTimeout));

        Assert.True(_flushTypesStreamsCalled);
        Assert.True(_flushCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(eventMessageCount, _lastFlushedInstanceMessage.EventsCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_EventMessage_FlushDueToSizeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < 50; i++)
        {
            block.Post(new EventMessage(
                "EventTypeId",
                $"EventId{i}",
                $"Event {i}",
                "Description",
                "DataSource",
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                null,
                new { Value = i },
                null,
                MessageAction.Create));
        }

        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.True(_flushTypesStreamsCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(50, _lastFlushedInstanceMessage.EventsCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_EventMessage_WithRelationships_RelationshipsExtractedAndIncludedInFlush_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var source = new DataTypeLinkNode("SourceType", "SourceIndex");
        var target = new DataTypeLinkNode("TargetType", "TargetIndex");
        var link = new Link(source, target);

        var eventMessage = new EventMessage(
            "EventTypeId",
            "EventId",
            "Event",
            "Description",
            "DataSource",
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null,
            new { Value = 1 },
            new List<Link> { link },
            MessageAction.Create);

        block.Post(eventMessage);
        block.Post(new CommandMessage(true));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.Equal(1, _lastFlushedInstanceMessage.EventsCount);
        Assert.Equal(1, _lastFlushedInstanceMessage.RelationshipCount);
        Assert.Null(eventMessage.Relationships);
    }

    #endregion

    #region RelationshipMessage Tests

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void InstanceGroupingBlock_Post_RelationshipMessage_Test(int relationshipCount)
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < relationshipCount; i++)
        {
            var source = new DataTypeLinkNode($"SourceType{i}", $"SourceIndex{i}");
            var target = new DataTypeLinkNode($"TargetType{i}", $"TargetIndex{i}");
            var link = new Link(source, target);
            var relationshipMessage = new RelationshipMessage(link, MessageAction.Create);

            block.Post(relationshipMessage);
        }

        Thread.Sleep(WaitTime);

        Assert.False(_flushCalled);
        Assert.False(_flushTypesStreamsCalled);

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);
        Assert.Equal(relationshipCount, (int)totalInstanceCount);

        block.Post(new CommandMessage(true));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.True(_flushTypesStreamsCalled);
        Assert.True(_flushCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(relationshipCount, _lastFlushedInstanceMessage.RelationshipCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_RelationshipMessage_FlushDueToSizeLimit_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < 50; i++)
        {
            var source = new DataTypeLinkNode($"Source{i}", $"Index{i}");
            var target = new DataTypeLinkNode($"Target{i}", $"Index{i}");
            var link = new Link(source, target);
            block.Post(new RelationshipMessage(link, MessageAction.Create));
        }

        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.True(_flushTypesStreamsCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(50, _lastFlushedInstanceMessage.RelationshipCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_RelationshipMessage_LongChain_FlushesAllLinks_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var relationshipCount = 2000;
        var maxBatchCount = relationshipCount + 1;
        var capacity = 4000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < relationshipCount; i++)
        {
            var source = new RelationshipLinkNode($"Node{i}");
            var target = new RelationshipLinkNode($"Node{i + 1}");
            block.Post(new RelationshipMessage(new Link(source, target), MessageAction.Create));
        }

        block.Post(new CommandMessage(true));

        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));
        Assert.Equal(relationshipCount, _lastFlushedInstanceMessage.RelationshipCount);
        Assert.Equal("Node0", _lastFlushedInstanceMessage.Relationships[0].Source.Index);
        Assert.Equal($"Node{relationshipCount}", _lastFlushedInstanceMessage.Relationships[relationshipCount - 1].Target.Index);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_MixedRelationshipInputs_AllRelationshipsAreIncluded_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var reusableLink = new Link(new RelationshipLinkNode("EntityA"), new RelationshipLinkNode("EntityB"));
        var eventOnlyLink = new Link(new RelationshipLinkNode("EventA"), new RelationshipLinkNode("EventB"));

        var staticDataMessage = new StaticDataMessage(
            "TypeId",
            "InstanceId",
            "Name",
            "Description",
            "DataSource",
            null,
            null,
            null,
            null,
            new { Value = 1 },
            MessageAction.Create)
        {
            Relationships = new List<Link> { reusableLink },
        };

        var eventMessage = new EventMessage(
            "EventTypeId",
            "EventId",
            "Event",
            "Description",
            "DataSource",
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            null,
            new { Value = 1 },
            new List<Link> { reusableLink, eventOnlyLink },
            MessageAction.Create);

        block.Post(staticDataMessage);
        block.Post(eventMessage);
        block.Post(new RelationshipMessage(eventOnlyLink, MessageAction.Create));
        block.Post(new CommandMessage(true));

        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.Equal(1, _lastFlushedInstanceMessage.EntitiesCount);
        Assert.Equal(1, _lastFlushedInstanceMessage.EventsCount);
        Assert.Equal(4, _lastFlushedInstanceMessage.RelationshipCount);
    }

    #endregion

    #region Mixed Message Tests

    [Fact]
    public void InstanceGroupingBlock_Post_MixedMessages_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < 10; i++)
        {
            block.Post(new DataMessage($"Stream{i}", Classification.Dynamic, new { Value = i }, MessageAction.Create));
        }

        for (int i = 0; i < 5; i++)
        {
            var staticDataMessage = new StaticDataMessage(
                "TypeId",
                $"InstanceId{i}",
                "Name",
                "Description",
                "DataSource",
                null,
                null,
                null,
                null,
                new { Value = i },
                MessageAction.Create);
            block.Post(staticDataMessage);
        }

        for (int i = 0; i < 3; i++)
        {
            var eventMessage = new EventMessage(
                "EventTypeId",
                $"EventId{i}",
                "Event",
                "Description",
                "DataSource",
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                null,
                new { Value = i },
                null,
                MessageAction.Create);
            block.Post(eventMessage);
        }

        for (int i = 0; i < 2; i++)
        {
            var source = new DataTypeLinkNode($"Source{i}", $"Index{i}");
            var target = new DataTypeLinkNode($"Target{i}", $"Index{i}");
            var link = new Link(source, target);
            block.Post(new RelationshipMessage(link, MessageAction.Create));
        }

        Thread.Sleep(WaitTime);

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);
        Assert.Equal(20, (int)totalInstanceCount);

        block.Post(new CommandMessage(true));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled && _lastFlushedInstanceMessage != null, SpinWaitTimeout));

        Assert.True(_flushCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(10, _lastFlushedInstanceMessage.StreamingDataObjectCount);
        Assert.Equal(5, _lastFlushedInstanceMessage.EntitiesCount);
        Assert.Equal(3, _lastFlushedInstanceMessage.EventsCount);
        Assert.Equal(2, _lastFlushedInstanceMessage.RelationshipCount);
    }

    #endregion

    #region CommandMessage Tests

    [Fact]
    public void InstanceGroupingBlock_Post_CommandMessage_ForceFlush_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        block.Post(_dataMessage);
        block.Post(new CommandMessage(false));

        Thread.Sleep(WaitTime);

        Assert.False(_flushCalled);
        Assert.False(_flushTypesStreamsCalled);

        var totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);
        Assert.Equal(1, totalInstanceCount);

        block.Post(new CommandMessage(true));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled, SpinWaitTimeout));

        Assert.True(_flushTypesStreamsCalled);
        Assert.True(_flushCalled);
        Assert.True(_flushTypesStreamsCalledTimestamp <= _flushCalledTimestamp);

        totalInstanceCount = GetFieldValueFromObject(TotalInstanceCountFieldName, block);
        Assert.Equal(0, totalInstanceCount);
    }

    #endregion

    #region StateMessage Tests

    [Fact]
    public void InstanceGroupingBlock_Post_StateMessage_Test()
    {
        var maxBatchCount = 50_000;
        var capacity = 1000;
        var desiredBatchCount = 40_000;
        var testLogger = new TestLogger();

        using var block = new InstanceGroupingBlock(
            testLogger,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        var currentBatchCount = GetFieldValueFromObject(BatchCountFieldName, block);
        Assert.Equal(maxBatchCount, currentBatchCount);

        block.Post(new StateMessage(desiredBatchCount));
        Assert.True(SpinWait.SpinUntil(() => (int)GetFieldValueFromObject(BatchCountFieldName, block) == desiredBatchCount, SpinWaitTimeout));

        currentBatchCount = GetFieldValueFromObject(BatchCountFieldName, block);
        Assert.Equal(desiredBatchCount, currentBatchCount);
    }

    [Fact]
    public void InstanceGroupingBlock_Post_StateMessage_TriggersFlush_Test()
    {
        var maxBatchCount = 50_000;
        var capacity = 1000;
        var testLogger = new TestLogger();

        using var block = new InstanceGroupingBlock(
            testLogger,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        for (int i = 0; i < 100; i++)
        {
            block.Post(_dataMessage);
        }

        Thread.Sleep(WaitTime);
        Assert.False(_flushCalled);

        block.Post(new StateMessage(50));
        Assert.True(SpinWait.SpinUntil(() => _flushCalled, SpinWaitTimeout));

        Assert.True(_flushCalled);
    }

    #endregion

    #region Empty Flush Tests

    [Fact]
    public void InstanceGroupingBlock_Post_EmptyFlush_DoesNothing_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 1000;
        var capacity = 1000;

        using var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        block.Post(new CommandMessage(true));
        Thread.Sleep(WaitTime);

        Assert.False(_flushCalled);
        Assert.False(_flushTypesStreamsCalled);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void InstanceGroupingBlock_Dispose_WithPendingMessages_FlushesPendingMessages_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var maxBatchCount = 50_000;
        var capacity = 1000;

        var block = new InstanceGroupingBlock(
            mockLogger.Object,
            capacity,
            DummyFlushAction,
            DummyFlushTypesStreamsAction,
            maxBatchCount,
            int.MaxValue,
            CancellationToken.None);

        const int count = 10;
        for (var i = 0; i < count; i++)
        {
            block.Post(_dataMessage);
        }

        Thread.Sleep(WaitTime);
        Assert.False(_flushCalled);

        block.Dispose();

        Assert.True(_flushCalled);
        Assert.NotNull(_lastFlushedInstanceMessage);
        Assert.Equal(count, _lastFlushedInstanceMessage.StreamingDataTotalCount);
    }

    #endregion

    #region Helper Methods

    private void DummyFlushAction(Message m)
    {
        _flushCalledTimestamp = DateTime.UtcNow;
        _flushCalled = true;
        _flushCalledCounter++;

        if (m is InstanceMessage instanceMessage)
        {
            _flushMessageAction = instanceMessage.MessageAction;
            _lastFlushedInstanceMessage = instanceMessage;
        }
    }

    private void DummyFlushTypesStreamsAction(Message m)
    {
        _flushTypesStreamsCalledTimestamp = DateTime.UtcNow;
        _flushTypesStreamsCalled = true;
    }

    #endregion
}
