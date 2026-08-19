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
using Xunit;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

public sealed class SchemaGroupingBlock_Tests : IDisposable
{
    #region Private Constants

    private const int WaitTime = 500;
    private const int SpinWaitTimeout = 30000;
    private const int Capacity = 100;
    private const int MaxStreamsBatchCount = 10;
    private const int MaxTypesBatchCount = 10;
    private const int MaxFlushTime = 100;

    #endregion

    #region Private Fields

    private readonly Mock<ILogger> _loggerMock;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private SchemaMessage _flushedMessage;
    private int _flushCount;
    private bool _disposed;

    #endregion

    #region Constructor

    public SchemaGroupingBlock_Tests()
    {
        _loggerMock = new Mock<ILogger>();
        _cancellationTokenSource = new CancellationTokenSource();
        _flushCount = 0;
        _flushedMessage = null;
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cancellationTokenSource?.Dispose();
            _flushedMessage?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Constructor Validation Tests

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenFlushIsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SchemaGroupingBlock(
                _loggerMock.Object,
                Capacity,
                null,
                MaxStreamsBatchCount,
                MaxTypesBatchCount,
                MaxFlushTime,
                _cancellationTokenSource.Token));
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenMaxStreamsBatchCountIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SchemaGroupingBlock(
                _loggerMock.Object,
                Capacity,
                FlushAction,
                0,
                MaxTypesBatchCount,
                MaxFlushTime,
                _cancellationTokenSource.Token));
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenMaxTypesBatchCountIsZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SchemaGroupingBlock(
                _loggerMock.Object,
                Capacity,
                FlushAction,
                MaxStreamsBatchCount,
                0,
                MaxFlushTime,
                _cancellationTokenSource.Token));
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenMaxFlushTimeIsLessThanMinimum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SchemaGroupingBlock(
                _loggerMock.Object,
                Capacity,
                FlushAction,
                MaxStreamsBatchCount,
                MaxTypesBatchCount,
                10,
                _cancellationTokenSource.Token));
    }

    #endregion

    #region DataType Tests

    [Fact]
    public void Handle_FlushesWhenMaxTypesBatchCountReached()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataTypes = CreateDataTypes(MaxTypesBatchCount);
        var message = new OmfMessage<DataType>(dataTypes.Count, dataTypes.ToArray(), MessageAction.Create);

        block.Post(message);

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.NotNull(_flushedMessage);
        Assert.Equal(MaxTypesBatchCount, _flushedMessage.TypeCount);
        Assert.Equal(0, _flushedMessage.ContainerCount);
        Assert.Equal(0, _flushedMessage.RelationshipCount);
        Assert.Equal(MessageAction.Create, _flushedMessage.MessageAction);
    }

    [Fact]
    public void Handle_BatchesMultipleDataTypes()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataType1 = CreateDataType("Type1");
        var dataType2 = CreateDataType("Type2");

        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType1 }, MessageAction.Create));
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType2 }, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(2, _flushedMessage.TypeCount);
    }

    [Fact]
    public void Handle_ProcessesMultipleDataTypesInSingleMessage()
    {
        using var block = CreateSchemaGroupingBlock(maxTypesBatchCount: 20);

        var dataTypes = CreateDataTypes(15);
        var message = new OmfMessage<DataType>(dataTypes.Count, dataTypes.ToArray(), MessageAction.Create);

        block.Post(message);
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(15, _flushedMessage.TypeCount);
    }

    [Fact]
    public void Handle_FlushesWhenDataTypesExceedBatchCount()
    {
        using var block = CreateSchemaGroupingBlock(maxTypesBatchCount: 5);

        var dataTypes = CreateDataTypes(12);
        var message = new OmfMessage<DataType>(dataTypes.Count, dataTypes.ToArray(), MessageAction.Create);

        block.Post(message);

        SpinWait.SpinUntil(() => _flushCount >= 2, SpinWaitTimeout);

        Assert.True(_flushCount >= 2);
    }

    [Fact]
    public void Handle_DataType_WithRelationships_IncludesRelationshipsInSchemaMessage()
    {
        using var block = CreateSchemaGroupingBlock();

        var link = CreateLink("Source1", "Target1");
        var dataType = CreateDataType("Type1");
        dataType.Relationships = new List<Link> { link };

        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.TypeCount);
        Assert.Equal(1, _flushedMessage.RelationshipCount);
    }

    [Fact]
    public void Handle_DataType_WithRelationships_PreservesRelationshipsPropertyOnOriginalInstance()
    {
        using var block = CreateSchemaGroupingBlock();

        var link = CreateLink("Source1", "Target1");
        var dataType = CreateDataType("Type1");
        dataType.Relationships = new List<Link> { link };

        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        // The block must operate on a copy so that callers caching the original
        // instance (e.g. for resend) do not lose the relationships.
        Assert.NotNull(dataType.Relationships);
        Assert.Single(dataType.Relationships);
    }

    [Fact]
    public void Handle_BulkDataTypes_WithRelationships_ExtractsRelationshipsFromAllTypes()
    {
        using var block = CreateSchemaGroupingBlock(maxTypesBatchCount: 20);

        var link1 = CreateLink("Source1", "Target1");
        var link2 = CreateLink("Source2", "Target2");

        var dataType1 = CreateDataType("Type1");
        dataType1.Relationships = new List<Link> { link1 };

        var dataType2 = CreateDataType("Type2");
        dataType2.Relationships = new List<Link> { link2 };

        block.Post(new OmfMessage<DataType>(2, new DataType[] { dataType1, dataType2 }, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(2, _flushedMessage.TypeCount);
        Assert.Equal(2, _flushedMessage.RelationshipCount);
        Assert.NotNull(dataType1.Relationships);
        Assert.NotNull(dataType2.Relationships);
    }

    #endregion

    #region DataStream Tests

    [Fact]
    public void Handle_FlushesWhenMaxStreamsBatchCountReached()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataStreams = CreateDataStreams(MaxStreamsBatchCount);
        var message = new OmfMessage<DataStream>(dataStreams.Count, dataStreams.ToArray(), MessageAction.Create);

        block.Post(message);

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.NotNull(_flushedMessage);
        Assert.Equal(0, _flushedMessage.TypeCount);
        Assert.Equal(MaxStreamsBatchCount, _flushedMessage.ContainerCount);
        Assert.Equal(0, _flushedMessage.RelationshipCount);
        Assert.Equal(MessageAction.Create, _flushedMessage.MessageAction);
    }

    [Fact]
    public void Handle_BatchesMultipleDataStreams()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataStream1 = CreateDataStream("Stream1");
        var dataStream2 = CreateDataStream("Stream2");

        block.Post(new OmfMessage<DataStream>(1, new DataStream[] { dataStream1 }, MessageAction.Create));
        block.Post(new OmfMessage<DataStream>(1, new DataStream[] { dataStream2 }, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(2, _flushedMessage.ContainerCount);
    }

    [Fact]
    public void Handle_ProcessesMultipleDataStreamsInSingleMessage()
    {
        using var block = CreateSchemaGroupingBlock(maxStreamsBatchCount: 20);

        var dataStreams = CreateDataStreams(15);
        var message = new OmfMessage<DataStream>(dataStreams.Count, dataStreams.ToArray(), MessageAction.Create);

        block.Post(message);
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(15, _flushedMessage.ContainerCount);
    }

    [Fact]
    public void Handle_FlushesWhenDataStreamsExceedBatchCount()
    {
        using var block = CreateSchemaGroupingBlock(maxStreamsBatchCount: 5);

        var dataStreams = CreateDataStreams(12);
        var message = new OmfMessage<DataStream>(dataStreams.Count, dataStreams.ToArray(), MessageAction.Create);

        block.Post(message);

        SpinWait.SpinUntil(() => _flushCount >= 2, SpinWaitTimeout);

        Assert.True(_flushCount >= 2);
    }

    #endregion

    #region RelationshipMessage Tests

    [Fact]
    public void Handle_ProcessesRelationshipMessages()
    {
        using var block = CreateSchemaGroupingBlock();

        var link1 = CreateLink("Source1", "Target1");
        var link2 = CreateLink("Source2", "Target2");

        block.Post(new RelationshipMessage(link1, MessageAction.Create));
        block.Post(new RelationshipMessage(link2, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(2, _flushedMessage.RelationshipCount);
    }

    [Fact]
    public void Handle_DataTypeRelationship_ReusedAsIndependentRelationship_IncludedInSchemaMessageTwice()
    {
        using var block = CreateSchemaGroupingBlock();

        var reusableLink = CreateLink("Source1", "Target1");
        var dataType = CreateDataType("Type1");
        dataType.Relationships = new List<Link> { reusableLink };

        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));
        block.Post(new RelationshipMessage(reusableLink, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.TypeCount);
        Assert.Equal(2, _flushedMessage.RelationshipCount);
        Assert.Equal("Source1", _flushedMessage.Relationships[0].Source.Index);
        Assert.Equal("Target1", _flushedMessage.Relationships[0].Target.Index);
        Assert.Equal("Source1", _flushedMessage.Relationships[1].Source.Index);
        Assert.Equal("Target1", _flushedMessage.Relationships[1].Target.Index);
    }

    #endregion

    #region Mixed Message Tests

    [Fact]
    public void Handle_BatchesTypesStreamsAndRelationships()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataType = CreateDataType("Type1");
        var dataStream = CreateDataStream("Stream1");
        var link = CreateLink("Source1", "Target1");

        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));
        block.Post(new OmfMessage<DataStream>(1, new DataStream[] { dataStream }, MessageAction.Create));
        block.Post(new RelationshipMessage(link, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.TypeCount);
        Assert.Equal(1, _flushedMessage.ContainerCount);
        Assert.Equal(1, _flushedMessage.RelationshipCount);
    }

    [Fact]
    public void Handle_FlushesWhenAnyBatchLimitReached()
    {
        using var block = CreateSchemaGroupingBlock(maxTypesBatchCount: 5, maxStreamsBatchCount: 5);

        var dataTypes = CreateDataTypes(3);
        var dataStreams = CreateDataStreams(5);

        block.Post(new OmfMessage<DataType>(dataTypes.Count, dataTypes.ToArray(), MessageAction.Create));
        block.Post(new OmfMessage<DataStream>(dataStreams.Count, dataStreams.ToArray(), MessageAction.Create));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(3, _flushedMessage.TypeCount);
        Assert.Equal(5, _flushedMessage.ContainerCount);
    }

    #endregion

    #region CommandMessage Tests

    [Fact]
    public void Handle_FlushesOnCommandMessageWithForceFlush()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataType = CreateDataType("Type1");
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.TypeCount);
    }

    [Fact]
    public void Handle_DoesNotFlushOnCommandMessage_WhenNoData()
    {
        using var block = CreateSchemaGroupingBlock();

        block.Post(new CommandMessage(true));

        Thread.Sleep(WaitTime);

        Assert.Equal(0, _flushCount);
    }

    #endregion

    #region Timer Flush Tests

    [Fact]
    public void Handle_FlushesAutomaticallyAfterMaxFlushTime()
    {
        using var block = CreateSchemaGroupingBlock(maxFlushTime: 200);

        var dataType = CreateDataType("Type1");
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.TypeCount);
    }

    [Fact]
    public void Handle_DoesNotFlushBeforeMaxFlushTime_WhenBatchNotFull()
    {
        using var block = CreateSchemaGroupingBlock(maxFlushTime: 1000);

        var dataType = CreateDataType("Type1");
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));

        Thread.Sleep(WaitTime);

        Assert.Equal(0, _flushCount);
    }

    #endregion

    #region StateMessage Tests

    [Fact]
    public void Handle_UpdatesStreamsBatchCountOnStateMessage()
    {
        using var block = CreateSchemaGroupingBlock(maxStreamsBatchCount: 10);

        var dataStreams = CreateDataStreams(5);
        block.Post(new OmfMessage<DataStream>(dataStreams.Count, dataStreams.ToArray(), MessageAction.Create));
        block.Post(new StateMessage(3));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(5, _flushedMessage.ContainerCount);
    }

    #endregion

    #region MessageAction Change Tests

    [Fact]
    public void Handle_FlushesWhenMessageActionChanges()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataType1 = CreateDataType("Type1");
        var dataType2 = CreateDataType("Type2");

        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType1 }, MessageAction.Create));
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType2 }, MessageAction.Update));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.TypeCount);
        Assert.Equal(MessageAction.Create, _flushedMessage.MessageAction);
    }

    [Fact]
    public void Handle_FlushesOnMessageActionChange_WithPendingRelationships()
    {
        using var block = CreateSchemaGroupingBlock();

        var link = CreateLink("Source1", "Target1");
        var dataType = CreateDataType("Type1");

        block.Post(new RelationshipMessage(link, MessageAction.Create));
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Update));

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.RelationshipCount);
        Assert.Equal(MessageAction.Create, _flushedMessage.MessageAction);
    }

    [Fact]
    public void Handle_MaintainsSeparateMessageActionBatches()
    {
        using var block = CreateSchemaGroupingBlock();

        var dataType1 = CreateDataType("Type1");
        var dataType2 = CreateDataType("Type2");

        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType1 }, MessageAction.Create));
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType2 }, MessageAction.Update));
        block.Post(new CommandMessage(true));

        SpinWait.SpinUntil(() => _flushCount >= 2, SpinWaitTimeout);

        Assert.Equal(2, _flushCount);
    }

    #endregion

    #region Empty Flush Tests

    [Fact]
    public void Handle_DoesNotFlushWhenNoData()
    {
        using var block = CreateSchemaGroupingBlock();

        block.Post(new CommandMessage(true));

        Thread.Sleep(WaitTime);

        Assert.Equal(0, _flushCount);
    }

    [Fact]
    public void Handle_DoesNotFlushEmptyBatchOnTimer()
    {
        using var block = CreateSchemaGroupingBlock(maxFlushTime: 200);

        Thread.Sleep(WaitTime);

        Assert.Equal(0, _flushCount);
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_FlushesRemainingData()
    {
        var block = CreateSchemaGroupingBlock();

        var dataType = CreateDataType("Type1");
        block.Post(new OmfMessage<DataType>(1, new DataType[] { dataType }, MessageAction.Create));

        block.Dispose();

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(1, _flushedMessage.TypeCount);
    }

    [Fact]
    public void Dispose_DoesNotFlushWhenNoData()
    {
        var block = CreateSchemaGroupingBlock();

        block.Dispose();

        Assert.Equal(0, _flushCount);
    }

    [Fact]
    public void Dispose_FlushesRelationships_WhenOnlyRelationshipsPending()
    {
        var block = CreateSchemaGroupingBlock();

        var link1 = CreateLink("Source1", "Target1");
        var link2 = CreateLink("Source2", "Target2");

        block.Post(new RelationshipMessage(link1, MessageAction.Create));
        block.Post(new RelationshipMessage(link2, MessageAction.Create));

        block.Dispose();

        SpinWait.SpinUntil(() => _flushCount > 0, SpinWaitTimeout);

        Assert.Equal(1, _flushCount);
        Assert.Equal(0, _flushedMessage.TypeCount);
        Assert.Equal(0, _flushedMessage.ContainerCount);
        Assert.Equal(2, _flushedMessage.RelationshipCount);
    }

    #endregion

    #region Helper Methods

    private static DataType CreateDataType(string id)
    {
        return new StaticDataType()
        {
            Id = id,
            Name = "Test type",
        };
    }

    private static List<DataType> CreateDataTypes(int count)
    {
        var types = new List<DataType>();
        for (int i = 0; i < count; i++)
        {
            types.Add(CreateDataType($"Type{i}"));
        }

        return types;
    }

    private static DataStream CreateDataStream(string id)
    {
        return new DataStream(id, "Type1", "Test stream");
    }

    private static List<DataStream> CreateDataStreams(int count)
    {
        var streams = new List<DataStream>();
        for (int i = 0; i < count; i++)
        {
            streams.Add(CreateDataStream($"Stream{i}"));
        }

        return streams;
    }

    private static Link CreateLink(string sourceId, string targetId)
    {
        return new Link(
            new RelationshipLinkNode(sourceId),
            new RelationshipLinkNode(targetId));
    }

    private SchemaGroupingBlock CreateSchemaGroupingBlock(
        int maxStreamsBatchCount = MaxStreamsBatchCount,
        int maxTypesBatchCount = MaxTypesBatchCount,
        int maxFlushTime = MaxFlushTime)
    {
        return new SchemaGroupingBlock(
            _loggerMock.Object,
            Capacity,
            FlushAction,
            maxStreamsBatchCount,
            maxTypesBatchCount,
            maxFlushTime,
            _cancellationTokenSource.Token);
    }

    private void FlushAction(Message message)
    {
        _flushedMessage?.Dispose();
        _flushedMessage = message as SchemaMessage;
        _flushCount++;
    }

    #endregion
}
