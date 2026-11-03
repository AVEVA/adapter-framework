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
#pragma warning disable CS0618 // Deliberately covers the obsolete Action<Message> constructor overload.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

public class SchemaInstanceFlushOrdering_Tests
{
    private const int DrainTimeoutEventId = DataflowEventIds.DrainTimeout;
    private const int DrainFaultedEventId = DataflowEventIds.DrainFaulted;
    private const int DrainCanceledEventId = DataflowEventIds.DrainCanceled;
    private const int FinalFlushFailedEventId = DataflowEventIds.FinalFlushFailed;
    private const int MisroutedEnvelopeEventId = DataflowEventIds.MisroutedEnvelope;
    private const int WorkerFaultedEventId = DataflowEventIds.WorkerFaulted;
    private const int WorkerCleanupFailedEventId = DataflowEventIds.WorkerCleanupFailed;
    private const int BlockDisposeFailedEventId = DataflowEventIds.BlockDisposeFailed;
    private const int ForwardFailedEventId = DataflowEventIds.ForwardFailed;
    private const int MessagesRejectedEventId = DataflowEventIds.MessagesRejected;
    private const string HandoffFailureMessage = "Handing a batch to the definitions block failed";
    private const string OwnershipTransferJustification = "Ownership of the envelope and its payload transfers to the block under test; the test asserts that it is released exactly once.";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    internal enum BlockKind
    {
        DataProducer,
        InstanceProducer,
        TypesStreamsDefinitions,
        SchemaDefinitions,
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 50)]
    [InlineData(false, 1)]
    [InlineData(false, 50)]
    public void ProducerFlush_WhenDefinitionsAreStalled_DeliversDefinitionsBeforeData(bool schema, int definitionCount)
    {
        using var recorder = new Recorder();
        var logger = new Mock<ILogger>().Object;
        using var flushEntered = new ManualResetEventSlim();
        using var releaseFlush = new ManualResetEventSlim();
        using var definitions = CreateDefinitionsBlock(schema, logger, message =>
        {
            flushEntered.Set();
            Assert.True(releaseFlush.Wait(WaitTimeout), "Timed out releasing the definitions flush.");
            recorder.Add(message);
        });
        using BaseBlock<Message> producer = schema
            ? new InstanceGroupingBlock(logger, DataflowBlockOptions.Unbounded, recorder.Add, PostEnvelope, 1000, 60000, CancellationToken.None)
            : new DataGroupingBlock(logger, DataflowBlockOptions.Unbounded, recorder.Add, PostEnvelope, 1000, 60000, CancellationToken.None);

        try
        {
            for (var i = 0; i < definitionCount; i++)
            {
                Assert.True(definitions.Post(CreateStreamMessage("S" + i)));
            }

            Assert.True(producer.Post(CreateDataMessage("S0")));
            Assert.True(producer.Post(new CommandMessage(true)));
            producer.Dispose();
            Assert.True(flushEntered.Wait(WaitTimeout), "The definitions flush did not start.");
            Assert.Empty(recorder.Snapshot());
            releaseFlush.Set();

            recorder.WaitForCount(2);
            var messages = recorder.Snapshot();
            if (schema)
            {
                Assert.IsType<SchemaMessage>(messages[0]);
                Assert.IsType<InstanceMessage>(messages[1]);
            }
            else
            {
                Assert.IsType<OmfMessage<DataStream>>(messages[0]);
                Assert.IsType<GroupedDataMessage>(messages[1]);
            }
        }
        finally
        {
            releaseFlush.Set();
        }

        void PostEnvelope(Message message)
        {
            definitions.PostOrThrow(message);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dispose_ProducerBeforeDefinitions_DeliversDefinitionsBeforeFinalBatch(bool schema)
    {
        using var recorder = new Recorder();
        var logger = new Mock<ILogger>().Object;
        using var definitions = CreateDefinitionsBlock(schema, logger, recorder.Add);
        using BaseBlock<Message> producer = schema
            ? new InstanceGroupingBlock(logger, DataflowBlockOptions.Unbounded, recorder.Add, PostEnvelope, 1000, 60000, CancellationToken.None)
            : new DataGroupingBlock(logger, DataflowBlockOptions.Unbounded, recorder.Add, PostEnvelope, 1000, 60000, CancellationToken.None);

        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        Assert.True(producer.Post(CreateDataMessage("S1")));
        producer.Dispose();
        definitions.Dispose();

        var messages = recorder.Snapshot();
        Assert.Equal(2, messages.Count);
        if (schema)
        {
            Assert.IsType<SchemaMessage>(messages[0]);
            Assert.IsType<InstanceMessage>(messages[1]);
        }
        else
        {
            Assert.IsType<OmfMessage<DataStream>>(messages[0]);
            Assert.IsType<GroupedDataMessage>(messages[1]);
        }

        void PostEnvelope(Message message) => definitions.PostOrThrow(message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void ForwardFailure_ReleasesPayloadAndDoesNotDisableLaterDefinitionsOrBatches(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, recorder.Add);
        var rejectedPayload = new DisposableMessage();

        try
        {
            Assert.True(definitions.Post(new FlushThenForwardMessage(rejectedPayload, _ =>
            {
                entered.Set();
                Assert.True(release.Wait(WaitTimeout), "Timed out releasing the failed callback.");
                throw new InvalidOperationException("Injected forwarding failure.");
            })));
            Assert.True(entered.Wait(WaitTimeout));
            Assert.True(definitions.Post(CreateStreamMessage("S1")));
            Assert.True(definitions.Post(new FlushThenForwardMessage(new CommandMessage(false), recorder.Add)));
            release.Set();
            recorder.WaitForCount(2);
            definitions.Dispose();

            Assert.Equal(1, rejectedPayload.DisposeCount);
            var messages = recorder.Snapshot();
            Assert.IsType<CommandMessage>(messages[1]);
            if (schema)
            {
                Assert.IsType<SchemaMessage>(messages[0]);
            }
            else
            {
                Assert.IsType<OmfMessage<DataStream>>(messages[0]);
            }

            VerifyError(logger, Times.Once());
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public async Task RejectedAdmission_DuringDefinitionDrain_ReleasesPayloadAndThrows(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, message =>
        {
            entered.Set();
            Assert.True(release.Wait(WaitTimeout), "Timed out releasing the draining definitions.");
            recorder.Add(message);
        });
        Assert.True(await definitions.PostAsync(CreateStreamMessage("S1")));
        Assert.True(await definitions.PostAsync(new CommandMessage(true)));
        Assert.True(entered.Wait(WaitTimeout));
        var disposal = Task.Run(definitions.Dispose);
        var payload = new DisposableMessage();

        try
        {
            Assert.True(SpinWait.SpinUntil(() => !definitions.Post(new CommandMessage(false)), WaitTimeout));
            Assert.Throws<InvalidOperationException>(() =>
                definitions.PostOrThrow(new FlushThenForwardMessage(payload, recorder.Add)));
            Assert.Empty(recorder.Snapshot());
            Assert.Equal(1, payload.DisposeCount);
        }
        finally
        {
            release.Set();
            await disposal;
        }

        Assert.Single(recorder.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void RejectedAdmission_AfterDefinitionDisposal_ReleasesPayloadAndThrows(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, recorder.Add);
        definitions.Dispose();
        var payload = new DisposableMessage();

        Assert.Throws<InvalidOperationException>(() =>
            definitions.PostOrThrow(new FlushThenForwardMessage(payload, recorder.Add)));

        Assert.Empty(recorder.Snapshot());
        Assert.Equal(1, payload.DisposeCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void RejectedAdmission_ByClosedNonDefinitionsBlock_ReleasesPayloadAndThrows()
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var wrongBlock = new OmfDataMessageBlock(logger.Object, DataflowBlockOptions.Unbounded, recorder.Add, CancellationToken.None);
        wrongBlock.Dispose();
        var payload = new DisposableMessage();

        Assert.Throws<InvalidOperationException>(() =>
            wrongBlock.PostOrThrow(new FlushThenForwardMessage(payload, recorder.Add)));

        Assert.Empty(recorder.Snapshot());
        Assert.Equal(1, payload.DisposeCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void DefinitionFailure_WhenPayloadDisposalThrows_ReleasesRemainingPendingPayloads(bool schema)
    {
        var logger = CreateLogger();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var payloads = new[] { new DisposableMessage(throwOnDispose: true), new DisposableMessage(throwOnDispose: true) };
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, message =>
        {
            (message as IDisposable)?.Dispose();
            entered.Set();
            Assert.True(release.Wait(WaitTimeout), "Timed out releasing the definitions worker.");
            throw new InvalidOperationException("Injected definition failure.");
        });

        try
        {
            Assert.True(definitions.Post(CreateStreamMessage("S1")));
            Assert.True(definitions.Post(new CommandMessage(true)));
            Assert.True(entered.Wait(WaitTimeout));
            foreach (var payload in payloads)
            {
                Assert.True(definitions.Post(new FlushThenForwardMessage(payload, _ =>
                    throw new InvalidOperationException("A discarded batch must not be forwarded."))));
            }

            release.Set();
            Assert.True(SpinWait.SpinUntil(() => payloads.All(payload => payload.DisposeCount == 1), WaitTimeout),
                "A pending payload was not released after an earlier disposal failed.");
            Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, DataflowEventIds.ReleaseForwardFailed) == payloads.Length, WaitTimeout));
            Assert.Null(Record.Exception(definitions.Dispose));
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void DefinitionFlushFailure_WhenPayloadDisposalThrows_ReportsFlushFailure(bool schema)
    {
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, message =>
        {
            (message as IDisposable)?.Dispose();
            throw new InvalidOperationException("Injected definition failure.");
        });

        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        var payload = new DisposableMessage(throwOnDispose: true);
        Assert.True(definitions.Post(new FlushThenForwardMessage(payload, _ =>
            throw new InvalidOperationException("A discarded batch must not be forwarded."))));

        Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, WorkerFaultedEventId) == 1, WaitTimeout));
        var faulted = logger.Invocations.ToArray().Single(invocation =>
            invocation.Method.Name == nameof(ILogger.Log) && ((EventId)invocation.Arguments[1]).Id == WorkerFaultedEventId);
        Assert.Equal("Injected definition failure.", ((Exception)faulted.Arguments[3]).Message);
        Assert.Equal(1, payload.DisposeCount);
        Assert.Equal(1, CountLogs(logger, DataflowEventIds.ReleaseForwardFailed));
        Assert.Null(Record.Exception(definitions.Dispose));
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void HandoffFailure_WhenPayloadDisposalThrows_PreservesBothFailures()
    {
        var payload = new DisposableMessage(throwOnDispose: true);
        var handoffFailure = new InvalidOperationException("Injected handoff failure.");

        var exception = Assert.Throws<AggregateException>(() =>
            BaseBlockExtensions.PostForOrderedFlush(payload, _ => throw handoffFailure, _ => { }));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(handoffFailure, exception.InnerExceptions[0]);
        Assert.Equal("Injected disposal failure.", exception.InnerExceptions[1].Message);
        Assert.Equal(1, payload.DisposeCount);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void ForwardFailure_WhenPayloadDisposalThrows_PreservesBothFailures()
    {
        var payload = new DisposableMessage(throwOnDispose: true);
        var forwardFailure = new InvalidOperationException("Injected forward failure.");
        using var envelope = new FlushThenForwardMessage(payload, _ => throw forwardFailure);

        var exception = Assert.Throws<AggregateException>(envelope.Deliver);

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Same(forwardFailure, exception.InnerExceptions[0]);
        Assert.Equal("Injected disposal failure.", exception.InnerExceptions[1].Message);
        Assert.Equal(1, payload.DisposeCount);
        Assert.Throws<InvalidOperationException>(envelope.Deliver);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void DefinitionFailureOrCancellation_ReleasesAcceptedPendingPayloads(bool schema, bool cancel)
    {
        var logger = CreateLogger();
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, message =>
        {
            (message as IDisposable)?.Dispose();
            entered.Set();
            Assert.True(release.Wait(WaitTimeout), "Timed out releasing the definitions worker.");
            if (!cancel)
            {
                throw new InvalidOperationException("Injected definition failure.");
            }
        }, cancellation.Token);

        try
        {
            Assert.True(definitions.Post(CreateStreamMessage("S1")));
            Assert.True(definitions.Post(new CommandMessage(true)));
            Assert.True(entered.Wait(WaitTimeout));
            var payload = new DisposableMessage();
            Assert.True(definitions.Post(new FlushThenForwardMessage(payload, _ =>
                throw new InvalidOperationException("A discarded batch must not be forwarded."))));
            if (cancel)
            {
                cancellation.Cancel();
            }

            release.Set();
            Assert.True(SpinWait.SpinUntil(() => payload.DisposeCount == 1, WaitTimeout));
            Assert.False(definitions.Post(new CommandMessage(false)));
            Assert.Null(Record.Exception(definitions.Dispose));

            VerifyError(logger, Times.AtLeastOnce());
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void ForwardToClosedDownstream_ReleasesPayloadAndKeepsDefinitionsHealthy(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var downstream = new OmfDataMessageBlock(logger.Object, DataflowBlockOptions.Unbounded, _ => { }, CancellationToken.None);
        downstream.Dispose();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, recorder.Add);
        var payload = new DisposableMessage();

        Assert.True(definitions.Post(new FlushThenForwardMessage(payload,
            message => downstream.PostOrThrow(message))));
        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        Assert.True(definitions.Post(new FlushThenForwardMessage(new CommandMessage(false), recorder.Add)));
        recorder.WaitForCount(2);
        definitions.Dispose();

        Assert.Equal(1, payload.DisposeCount);
        VerifyError(logger, Times.Once());
    }

    [Fact]
    public void ForwardSuccess_TransfersOwnershipAndCannotBeRetried()
    {
        var payload = new DisposableMessage();
        using var envelope = new FlushThenForwardMessage(payload, _ => { });
        envelope.Deliver();
        envelope.Dispose();

        Assert.Equal(0, payload.DisposeCount);
        Assert.Throws<InvalidOperationException>(envelope.Deliver);
        payload.Dispose();
        Assert.Equal(1, payload.DisposeCount);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public async Task PostAsync_ReleasesRejectedEnvelopesAndTransfersAcceptedOnes(bool schema, bool reject)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, recorder.Add);
        if (reject)
        {
            definitions.Dispose();
        }

        var payload = new DisposableMessage();
        var accepted = await definitions.PostAsync(new FlushThenForwardMessage(payload, recorder.Add));
        Assert.Equal(!reject, accepted);
        if (reject)
        {
            Assert.Empty(recorder.Snapshot());
            Assert.Equal(1, payload.DisposeCount);
        }
        else
        {
            recorder.WaitForCount(1);
            definitions.Dispose();
            Assert.Equal(0, payload.DisposeCount);
            Assert.Same(payload, Assert.Single(recorder.Snapshot()));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectedDefinitionCallback_ReleasesEnvelopeBeforeFaultingProducer(bool schema)
    {
        var logger = CreateLogger();
        FlushThenForwardMessage rejectedEnvelope = null;
        using BaseBlock<Message> producer = schema
            ? new InstanceGroupingBlock(logger.Object, DataflowBlockOptions.Unbounded, _ => { }, Reject, 1, 60000, CancellationToken.None)
            : new DataGroupingBlock(logger.Object, DataflowBlockOptions.Unbounded, _ => { }, Reject, 1, 60000, CancellationToken.None);
        Assert.True(producer.Post(CreateDataMessage("S1")));

        Assert.Null(Record.Exception(producer.Dispose));
        Assert.NotNull(rejectedEnvelope);
        Assert.Throws<InvalidOperationException>(rejectedEnvelope.Deliver);

        void Reject(Message message)
        {
            rejectedEnvelope = Assert.IsType<FlushThenForwardMessage>(message);
            throw new InvalidOperationException("Injected definition admission failure.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProducerState_WithNullLogger_DoesNotFaultBeforeForwarding(bool schema)
    {
        using var recorder = new Recorder();
        using var definitions = CreateDefinitionsBlock(schema, null, recorder.Add);
        using BaseBlock<Message> producer = schema
            ? new InstanceGroupingBlock(null, DataflowBlockOptions.Unbounded, recorder.Add, PostEnvelope, 1000, 60000, CancellationToken.None)
            : new DataGroupingBlock(null, DataflowBlockOptions.Unbounded, recorder.Add, PostEnvelope, 1000, 60000, CancellationToken.None);
        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        Assert.True(producer.Post(new StateMessage(500)));
        Assert.True(producer.Post(CreateDataMessage("S1")));
        Assert.True(producer.Post(new CommandMessage(true)));
        producer.Dispose();
        definitions.Dispose();

        Assert.Equal(2, recorder.Snapshot().Count);

        void PostEnvelope(Message message) => definitions.PostOrThrow(message);
    }

    [Fact]
    public void FlushThenForwardMessage_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new FlushThenForwardMessage(null, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new FlushThenForwardMessage(new CommandMessage(false), null));
    }

    [Theory]
    [InlineData("Drained", true, 0)]
    [InlineData("Faulted", true, DrainFaultedEventId)]
    [InlineData("Canceled", true, DrainCanceledEventId)]
    [InlineData("TimedOut", false, DrainTimeoutEventId)]
    public void FinalFlush_RunsOnceTheWorkerHasStopped_AndIsSkippedOnlyOnDrainTimeout(string outcome, bool expectFlush, int expectedEventId)
    {
        var logger = CreateLogger();
        using var cancellation = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var block = new DrainTestBlock(logger.Object, () =>
        {
            entered.Set();
            Assert.True(release.Wait(WaitTimeout), "Timed out releasing the drain-test worker.");
            if (outcome == "Faulted")
            {
                throw new InvalidOperationException("Injected worker failure.");
            }
        }, token: cancellation.Token);

        // Only the timeout case may expire; the others wait for the worker to stop.
        block.DrainTimeoutMilliseconds = outcome == "TimedOut" ? 100 : (int)WaitTimeout.TotalMilliseconds;
        try
        {
            Assert.True(block.Post(new CommandMessage(false)));
            Assert.True(entered.Wait(WaitTimeout));
            if (outcome == "Canceled")
            {
                cancellation.Cancel();
            }

            if (outcome != "TimedOut")
            {
                release.Set();
            }

            Assert.Null(Record.Exception(block.Dispose));

            Assert.Equal(expectFlush, block.Flushed);
            Assert.Equal(0, CountLogs(logger, FinalFlushFailedEventId));
            if (expectedEventId == 0)
            {
                VerifyError(logger, Times.Never());
            }
            else
            {
                Assert.Equal(1, CountLogs(logger, expectedEventId));
            }
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void WorkerStoppedCleanupFailure_IsLoggedAndDisposeStillFlushes()
    {
        var logger = CreateLogger();
        using var block = new DrainTestBlock(logger.Object, () => { }, onWorkerStopped: () => throw new InvalidOperationException("Injected cleanup failure."));

        Assert.True(block.Post(new CommandMessage(false)));
        Assert.Null(Record.Exception(block.Dispose));

        Assert.True(block.Flushed);
        Assert.Equal(1, CountLogs(logger, WorkerCleanupFailedEventId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProducerFault_FinalFlushHandsTheBufferedBatchToDefinitionsInOrder(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, recorder.Add);
        var handoffs = 0;
        using var producer = CreateProducer(schema, logger.Object, recorder.Add, message =>
        {
            if (Interlocked.Increment(ref handoffs) == 1)
            {
                throw new InvalidOperationException("Injected transient handoff failure.");
            }

            definitions.PostOrThrow(message);
        }, 3, 60000);

        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        Assert.True(producer.Post(CreateDataMessage("S1")));
        Assert.True(producer.Post(CreateEntityMessage("Entity1")));

        // The third item fills the batch, so the failed handoff carries a stream value, an entity and an event.
        Assert.True(producer.Post(CreateEventMessage("Event1", new Link(new RelationshipLinkNode("Event1"), new RelationshipLinkNode("Entity1")))));
        Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, WorkerFaultedEventId) == 1, WaitTimeout), "The producer did not fault.");

        producer.Dispose();
        definitions.Dispose();

        var messages = recorder.Snapshot();
        Assert.Equal(2, messages.Count);
        AssertDefinitionsThenData(schema, messages);
        if (schema)
        {
            var batch = (InstanceMessage)messages[1];
            Assert.Equal(1, batch.StreamingDataObjectCount);
            Assert.Equal(1, batch.EntitiesCount);
            Assert.Equal("Entity1", batch.Entities[0].InstanceId);
            Assert.Equal(1, batch.EventsCount);
            Assert.Equal("Event1", batch.Events[0].Id);
            Assert.Equal(1, batch.RelationshipCount);
        }
        else
        {
            var batch = (GroupedDataMessage)messages[1];
            Assert.Equal(3, batch.Count);
            Assert.True(batch.Groupings.ContainsKey("S1"));
            Assert.Equal("Entity1", Assert.Single(batch.StaticGroupings).InstanceId);
            Assert.Equal(Classification.Static, batch.Groupings["EventType"].Classification);
        }

        Assert.Equal(1, CountLogs(logger, DrainFaultedEventId));
        Assert.Equal(0, CountLogs(logger, FinalFlushFailedEventId));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ProducerCanceled_FinalFlushGoesThroughDefinitionsOrIsReleased(bool schema, bool definitionsCanceled)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var cancellation = new CancellationTokenSource();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, recorder.Add, definitionsCanceled ? cancellation.Token : CancellationToken.None);
        using var producer = CreateProducer(schema, logger.Object, recorder.Add, message => definitions.PostOrThrow(message), 1000, 60000, cancellation.Token);

        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        Assert.True(producer.Post(CreateDataMessage("S1")));

        // A dequeued message is still handled after cancellation, so empty queues mean both items are batched rather than dropped.
        Assert.True(SpinWait.SpinUntil(() => definitions.InputCount == 0 && producer.InputCount == 0, WaitTimeout));
        cancellation.Cancel();
        producer.Dispose();
        definitions.Dispose();

        var messages = recorder.Snapshot();
        Assert.Equal(definitionsCanceled ? 2 : 1, CountLogs(logger, DrainCanceledEventId));
        if (definitionsCanceled)
        {
            // The stopped definitions block rejects the batch, so it is released instead of being sent without its definitions.
            Assert.IsType(schema ? typeof(SchemaMessage) : typeof(OmfMessage<DataStream>), Assert.Single(messages));
            Assert.Equal(1, CountLogs(logger, FinalFlushFailedEventId));
        }
        else
        {
            Assert.Equal(2, messages.Count);
            AssertDefinitionsThenData(schema, messages);
            Assert.Equal(0, CountLogs(logger, FinalFlushFailedEventId));
        }
    }

    [Theory]
    [InlineData(nameof(BlockKind.DataProducer))]
    [InlineData(nameof(BlockKind.InstanceProducer))]
    [InlineData(nameof(BlockKind.TypesStreamsDefinitions))]
    [InlineData(nameof(BlockKind.SchemaDefinitions))]
    public void DrainTimeout_RealGroupingBlock_WaitsAndLogsOnlyOnce(string kindName)
    {
        var kind = Enum.Parse<BlockKind>(kindName);
        const int DrainTimeout = 1000;
        var logger = CreateLogger();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var block = CreateStuckBlock(kind, logger.Object, entered, release);
        block.DrainTimeoutMilliseconds = DrainTimeout;

        try
        {
            Assert.True(entered.Wait(WaitTimeout), "The worker did not start the stuck flush.");

            // Every wait that expires is logged, so one drain-timeout entry proves the drain was waited for exactly once,
            // including the base-class disposal and a repeated Dispose. Elapsed-time assertions are avoided because they are unreliable on busy machines.
            Assert.Null(Record.Exception(block.Dispose));
            Assert.Equal(1, CountLogs(logger, DrainTimeoutEventId));

            Assert.Null(Record.Exception(block.Dispose));
            Assert.Equal(1, CountLogs(logger, DrainTimeoutEventId));
            Assert.Equal(1, CountErrors(logger));
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IgnoredDefinitionsCallback_FaultsProducerAndFinalFlushCannotBypassIt(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var producer = CreateProducer(schema, logger.Object, recorder.Add, _ => { }, 1, 60000);

        Assert.True(producer.Post(CreateDataMessage("S1")));

        // The fault is reported when it happens, not only when the producer is disposed.
        Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, WorkerFaultedEventId) == 1, WaitTimeout), "The producer fault was not logged.");
        Assert.False(producer.Post(CreateDataMessage("S2")));
        Assert.Null(Record.Exception(producer.Dispose));

        // The best-effort final flush retries the same handoff, which fails the same way, so nothing is delivered.
        Assert.Empty(recorder.Snapshot());
        AssertHandoffFailuresLogged(logger, WorkerFaultedEventId, FinalFlushFailedEventId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MisroutedEnvelope_FaultsProducerEvenAfterTheWrongBlockReleasedIt(bool schema)
    {
        var logger = CreateLogger();
        using var wrongBlock = new OmfDataMessageBlock(logger.Object, DataflowBlockOptions.Unbounded, _ => { }, CancellationToken.None);
        using var producer = CreateProducer(schema, logger.Object, _ => { }, PostToWrongBlockAndWaitForRelease, 1, 60000);

        Assert.True(producer.Post(CreateDataMessage("S1")));

        Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, WorkerFaultedEventId) == 1, WaitTimeout), "The misrouted handoff did not fault the producer.");
        Assert.False(producer.Post(CreateDataMessage("S2")));
        AssertHandoffFailuresLogged(logger, WorkerFaultedEventId);
        Assert.Equal(1, CountLogs(logger, MisroutedEnvelopeEventId));

        // Widest race window: the wrong block has already released the envelope before the producer checks the handoff.
        void PostToWrongBlockAndWaitForRelease(Message message)
        {
            var released = CountLogs(logger, MisroutedEnvelopeEventId);
            wrongBlock.PostOrThrow(message);
            Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, MisroutedEnvelopeEventId) > released, WaitTimeout), "The wrong block did not release the envelope.");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectedHandoffWithIgnoredPostResult_FaultsProducer(bool schema)
    {
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, _ => { });
        definitions.Dispose();

        // A legacy-style callback that ignores Post's result must not make a rejected batch look delivered.
        using var producer = CreateProducer(schema, logger.Object, _ => { }, message => definitions.Post(message), 1, 60000);
        Assert.True(producer.Post(CreateDataMessage("S1")));

        Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, WorkerFaultedEventId) == 1, WaitTimeout), "The rejected handoff did not fault the producer.");
        AssertHandoffFailuresLogged(logger, WorkerFaultedEventId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void DuplicatePost_OfQueuedEnvelope_ThrowsAndDeliversOnce(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, message =>
        {
            entered.Set();
            Assert.True(release.Wait(WaitTimeout), "Timed out releasing the definitions flush.");
            recorder.Add(message);
        });
        var payload = new DisposableMessage();
        using var envelope = new FlushThenForwardMessage(payload, recorder.Add);

        try
        {
            Assert.True(definitions.Post(CreateStreamMessage("S1")));
            Assert.True(definitions.Post(new CommandMessage(true)));
            Assert.True(entered.Wait(WaitTimeout));
            Assert.True(definitions.Post(envelope));

            Assert.Throws<InvalidOperationException>(() => definitions.Post(envelope));

            release.Set();
            recorder.WaitForCount(2);
            definitions.Dispose();

            Assert.Equal(1, recorder.Snapshot().Count(message => ReferenceEquals(message, payload)));
            Assert.Equal(0, payload.DisposeCount);
            VerifyError(logger, Times.Never());
        }
        finally
        {
            release.Set();
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ProducerFlushBySizeOrTimer_WhenDefinitionsAreStalled_DeliversDefinitionsBeforeData(bool schema, bool timerDriven)
    {
        using var recorder = new Recorder();
        var logger = new Mock<ILogger>().Object;
        using var flushEntered = new ManualResetEventSlim();
        using var releaseFlush = new ManualResetEventSlim();
        using var definitions = (DefinitionsBlock)CreateDefinitionsBlock(schema, logger, message =>
        {
            flushEntered.Set();
            Assert.True(releaseFlush.Wait(WaitTimeout), "Timed out releasing the definitions flush.");
            recorder.Add(message);
        });

        // Size-driven: every data message fills the batch. Timer-driven: only the producer's 50 ms timer can flush.
        var maxBatchCount = timerDriven ? 1000 : 1;
        var maxFlushTime = timerDriven ? 50 : 60000;
        using BaseBlock<Message> producer = schema
            ? new InstanceGroupingBlock(logger, DataflowBlockOptions.Unbounded, recorder.Add, definitions, maxBatchCount, maxFlushTime, CancellationToken.None)
            : new DataGroupingBlock(logger, DataflowBlockOptions.Unbounded, recorder.Add, definitions, maxBatchCount, maxFlushTime, CancellationToken.None);

        try
        {
            Assert.True(definitions.Post(CreateStreamMessage("S1")));
            Assert.True(producer.Post(CreateDataMessage("S1")));

            Assert.True(flushEntered.Wait(WaitTimeout), "The producer flush did not reach the definitions block.");
            Assert.Empty(recorder.Snapshot());
            releaseFlush.Set();

            recorder.WaitForCount(2);
            AssertDefinitionsThenData(schema, recorder.Snapshot());
        }
        finally
        {
            releaseFlush.Set();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefinitionsWorkerFault_IsLoggedWhenItHappensWithExceptionOnlyOnce(bool schema)
    {
        var logger = CreateLogger();
        var failures = 0;
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, _ =>
        {
            // Only the worker's flush fails, so the best-effort final flush in Dispose adds no further log entry.
            if (Interlocked.Increment(ref failures) == 1)
            {
                throw new InvalidOperationException("Injected definitions failure.");
            }
        });

        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        Assert.True(definitions.Post(new CommandMessage(true)));

        Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, WorkerFaultedEventId) == 1, WaitTimeout), "The worker fault was not logged before disposal.");
        Assert.Null(Record.Exception(definitions.Dispose));

        Assert.Equal(1, CountLogs(logger, WorkerFaultedEventId));
        Assert.Equal(1, CountLogs(logger, DrainFaultedEventId));
        Assert.Equal(1, logger.Invocations.ToArray().Count(invocation => invocation.Method.Name == nameof(ILogger.Log) && invocation.Arguments[3] is Exception));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IgnoredDefinitionsCallback_DuringFinalFlushInDispose_LogsHandoffFailureOnce(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var producer = CreateProducer(schema, logger.Object, recorder.Add, _ => { }, 1000, 60000);
        Assert.True(producer.Post(CreateDataMessage("S1")));

        // The batch is still buffered, so the handoff fails during the caller-thread flush in Dispose, not on the worker.
        Assert.Null(Record.Exception(producer.Dispose));

        Assert.Empty(recorder.Snapshot());
        AssertHandoffFailuresLogged(logger, FinalFlushFailedEventId);
        Assert.Equal(0, CountLogs(logger, WorkerFaultedEventId));
    }

    [Fact]
    public void DrainTimeout_InvalidValue_IsRejectedWithoutBreakingDispose()
    {
        var logger = CreateLogger();
        using var block = new DrainTestBlock(logger.Object, () => { });

        Assert.Throws<ArgumentOutOfRangeException>(() => block.DrainTimeoutMilliseconds = -2);
        Assert.Throws<ArgumentOutOfRangeException>(() => block.CompleteAndWaitFor(-2));

        // Infinite last: Dispose drains with this value.
        block.DrainTimeoutMilliseconds = 0;
        block.DrainTimeoutMilliseconds = Timeout.Infinite;

        // The rejected values changed no state: the block still drains and disposes cleanly.
        Assert.True(block.Post(new CommandMessage(false)));
        Assert.Null(Record.Exception(block.Dispose));
        Assert.True(block.Flushed);
        VerifyError(logger, Times.Never());
    }

    [Fact]
    public void ReportDisposeFailure_LogsOnceAndNeverThrows()
    {
        var failure = new InvalidOperationException("Injected dispose failure.");
        var logger = CreateLogger();
        var throwingLogger = new ThrowingLogger();

        DataflowDisposal.ReportDisposeFailure(logger.Object, failure);

        Assert.Equal(1, CountLogs(logger, BlockDisposeFailedEventId));
        Assert.Null(Record.Exception(() => DataflowDisposal.ReportDisposeFailure(throwingLogger, failure)));
        Assert.Null(Record.Exception(() => DataflowDisposal.ReportDisposeFailure(null, failure)));
    }

    [Fact]
    public void DataflowEventIds_AreUnique()
    {
        var ids = typeof(DataflowEventIds).GetFields().Select(field => (int)field.GetRawConstantValue()).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void DataflowEventIds_UseDedicatedRangeExceptPreExistingBatchSizeChanged()
    {
        var outOfRange = typeof(DataflowEventIds).GetFields()
            .Where(field => field.Name != nameof(DataflowEventIds.BatchSizeChanged))
            .Select(field => (int)field.GetRawConstantValue())
            .Where(id => id is < 5000 or > 5099);

        Assert.Empty(outOfRange);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PostAfterUnexpectedStop_ReportsRejectedMessagesOncePerInterval(bool schema)
    {
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, _ => throw new InvalidOperationException("Injected definitions failure."));
        Assert.True(definitions.Post(CreateStreamMessage("S1")));
        Assert.True(definitions.Post(new CommandMessage(true)));
        Assert.True(SpinWait.SpinUntil(() => CountLogs(logger, WorkerFaultedEventId) == 1, WaitTimeout));

        Assert.True(SpinWait.SpinUntil(() => !definitions.Post(CreateStreamMessage("S2")), WaitTimeout));
        for (var i = 0; i < 10; i++)
        {
            Assert.False(definitions.Post(CreateStreamMessage("S3")));
        }

        Assert.Equal(1, CountLogs(logger, MessagesRejectedEventId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PostAfterDisposal_IsNotReportedAsRejected(bool schema)
    {
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, _ => { });
        definitions.Dispose();

        Assert.False(definitions.Post(CreateStreamMessage("S1")));
        Assert.Equal(0, CountLogs(logger, MessagesRejectedEventId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = OwnershipTransferJustification)]
    public void RepeatedForwardFailures_AreReportedOncePerInterval(bool schema)
    {
        using var recorder = new Recorder();
        var logger = CreateLogger();
        using var definitions = CreateDefinitionsBlock(schema, logger.Object, recorder.Add);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(definitions.Post(new FlushThenForwardMessage(new CommandMessage(false), _ =>
                throw new InvalidOperationException("Injected forwarding failure."))));
        }

        Assert.True(definitions.Post(new FlushThenForwardMessage(new CommandMessage(false), recorder.Add)));
        recorder.WaitForCount(1);

        Assert.Equal(1, CountLogs(logger, ForwardFailedEventId));
    }

    [Fact]
    public void RateLimitedCounter_ReportsFirstOccurrenceThenAccumulatesUntilIntervalElapses()
    {
        var counter = new RateLimitedCounter(200);

        Assert.True(counter.Increment(out var first));
        Assert.Equal(1, first);
        Assert.False(counter.Increment(out _));
        Assert.False(counter.Increment(out _));

        Thread.Sleep(250);
        Assert.True(counter.Increment(out var accumulated));
        Assert.Equal(3, accumulated);
    }

    private static void AssertHandoffFailuresLogged(Mock<ILogger> logger, params int[] expectedEventIds)
    {
        var withException = logger.Invocations.ToArray()
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log) && invocation.Arguments[3] is Exception)
            .ToList();
        Assert.Equal(expectedEventIds, withException.Select(invocation => ((EventId)invocation.Arguments[1]).Id));
        Assert.All(withException, invocation =>
            Assert.Contains(HandoffFailureMessage, ((Exception)invocation.Arguments[3]).Message, StringComparison.Ordinal));
    }

    private static int CountErrors(Mock<ILogger> logger)
    {
        return logger.Invocations.ToArray().Count(invocation =>
            invocation.Method.Name == nameof(ILogger.Log) && (LogLevel)invocation.Arguments[0] == LogLevel.Error);
    }

    private static BaseBlock<Message> CreateStuckBlock(BlockKind kind, ILogger logger, ManualResetEventSlim entered, ManualResetEventSlim release)
    {
        void Stall(Message message)
        {
            entered.Set();
            release.Wait(WaitTimeout);
            (message as FlushThenForwardMessage)?.Deliver();
        }

        BaseBlock<Message> block = kind switch
        {
            BlockKind.DataProducer => new DataGroupingBlock(logger, DataflowBlockOptions.Unbounded, _ => { }, Stall, 1, 60000, CancellationToken.None),
            BlockKind.InstanceProducer => new InstanceGroupingBlock(logger, DataflowBlockOptions.Unbounded, _ => { }, Stall, 1, 60000, CancellationToken.None),
            BlockKind.TypesStreamsDefinitions => new TypesStreamsGroupingBlock(logger, DataflowBlockOptions.Unbounded, Stall, 1, 1, 60000, CancellationToken.None),
            _ => new SchemaGroupingBlock(logger, DataflowBlockOptions.Unbounded, Stall, 1, 1, 60000, CancellationToken.None),
        };

        Assert.True(block.Post(kind is BlockKind.DataProducer or BlockKind.InstanceProducer ? CreateDataMessage("S1") : CreateStreamMessage("S1")));
        return block;
    }

    private static BaseBlock<Message> CreateProducer(bool schema, ILogger logger, Action<Message> flush, Action<Message> postDefinitions, int maxBatchCount, int maxFlushTime, CancellationToken token = default)
    {
        return schema
            ? new InstanceGroupingBlock(logger, DataflowBlockOptions.Unbounded, flush, postDefinitions, maxBatchCount, maxFlushTime, token)
            : new DataGroupingBlock(logger, DataflowBlockOptions.Unbounded, flush, postDefinitions, maxBatchCount, maxFlushTime, token);
    }

    private static void AssertDefinitionsThenData(bool schema, List<Message> messages)
    {
        if (schema)
        {
            Assert.IsType<SchemaMessage>(messages[0]);
            Assert.IsType<InstanceMessage>(messages[1]);
        }
        else
        {
            Assert.IsType<OmfMessage<DataStream>>(messages[0]);
            Assert.IsType<GroupedDataMessage>(messages[1]);
        }
    }

    private static int CountLogs(Mock<ILogger> logger, int eventId)
    {
        return logger.Invocations.ToArray().Count(invocation =>
            invocation.Method.Name == nameof(ILogger.Log) && ((EventId)invocation.Arguments[1]).Id == eventId);
    }

    private static BaseBlock<Message> CreateDefinitionsBlock(bool schema, ILogger logger, Action<Message> flush, CancellationToken token = default)
    {
        return schema
            ? new SchemaGroupingBlock(logger, DataflowBlockOptions.Unbounded, flush, 1000, 1000, 60000, token)
            : new TypesStreamsGroupingBlock(logger, DataflowBlockOptions.Unbounded, flush, 1000, 1000, 60000, token);
    }

    private static OmfMessage<DataStream> CreateStreamMessage(string id)
    {
        return new OmfMessage<DataStream>(1, [new DataStream { Id = id, TypeId = "T1" }], MessageAction.Create);
    }

    private static DataMessage CreateDataMessage(string id)
    {
        return new DataMessage(id, Classification.Dynamic, new Dictionary<string, string>(), MessageAction.Create);
    }

    private static StaticDataMessage CreateEntityMessage(string id)
    {
        return new StaticDataMessage("EntityType", id, id, null, null, null, null, null, null, new Dictionary<string, string>(), MessageAction.Create);
    }

    private static EventMessage CreateEventMessage(string id, Link relationship)
    {
        return new EventMessage("EventType", id, id, null, null, DateTime.UtcNow, null, null, null, null, null, new Dictionary<string, string>(), [relationship], MessageAction.Create);
    }

    private static Mock<ILogger> CreateLogger()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(value => value.IsEnabled(LogLevel.Error)).Returns(true);
        return logger;
    }

    private static void VerifyError(Mock<ILogger> logger, Times times)
    {
        logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), times);
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => throw new InvalidOperationException("Injected logging failure.");
    }
    private sealed class DisposableMessage : Message, IDisposable
    {
        private readonly bool _throwOnDispose;
        private int _disposeCount;

        public DisposableMessage(bool throwOnDispose = false)
        {
            _throwOnDispose = throwOnDispose;
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            if (_throwOnDispose)
            {
                throw new InvalidOperationException("Injected disposal failure.");
            }
        }
    }

    private sealed class DrainTestBlock : BaseBlock<Message>
    {
        private readonly Action _handle;
        private readonly Action _onWorkerStopped;

        public DrainTestBlock(ILogger logger, Action handle, Action onWorkerStopped = null, CancellationToken token = default)
            : base(logger, DataflowBlockOptions.Unbounded, false, false, token)
        {
            _handle = handle;
            _onWorkerStopped = onWorkerStopped;
        }

        public bool Flushed { get; private set; }

        protected override void Handle(Message message) => _handle();

        protected override Task HandleAsync(Message message) => throw new NotImplementedException();

        public string CompleteAndWaitFor(int millisecondsTimeout) => CompleteAndWait(millisecondsTimeout).ToString();

        private protected override void OnWorkerStopped() => _onWorkerStopped?.Invoke();

        protected override void Dispose(bool disposing)
        {
            if (!disposing || Disposed)
            {
                return;
            }

            DrainAndFlush(() => Flushed = true);

            // Like the production blocks, always continue into the base disposal after the caller-thread drain.
            base.Dispose(disposing);
        }
    }

    private sealed class Recorder : IDisposable
    {
        private readonly List<Message> _messages = [];

        public void Add(Message message)
        {
            lock (_messages)
            {
                _messages.Add(message);
                Monitor.PulseAll(_messages);
            }
        }

        public void WaitForCount(int count)
        {
            var deadline = DateTime.UtcNow + WaitTimeout;
            lock (_messages)
            {
                while (_messages.Count < count)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    Assert.True(remaining > TimeSpan.Zero && Monitor.Wait(_messages, remaining),
                        "Timed out waiting for recorded output.");
                }
            }
        }

        public List<Message> Snapshot()
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }

        public void Dispose()
        {
            foreach (var message in Snapshot().OfType<IDisposable>())
            {
                message.Dispose();
            }
        }
    }
}
