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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Failover.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests;

public sealed class FailoverDataMessageProcessorAcceptance_Tests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Verifies a secondary reports peer-wait diagnostics before a watermark trims the body without an HTTP receipt.</summary>
    [Fact]
    public async Task ProcessOmfMessage_HotSecondary_TrimCompletesPeerCoverage()
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        await using var scope = fixture.CreateScope();
        var message = fixture.RegisterBodies(scope).Single();

        fixture.Processor.ProcessOmfMessage(message);
        var pending = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, pending.Outcome);
        Assert.Equal(OmfReasonCode.AwaitingPeerCoverage, pending.Reason.Code);
        var buffered = Assert.Single(pending.Deliveries);
        Assert.Equal(message.SerializedMessageId, buffered.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.Pending, buffered.State);
        Assert.Null(buffered.EndpointId);
        Assert.Null(buffered.TargetUri);
        Assert.Null(buffered.LastStatusCode);
        Assert.Null(buffered.Receipt);
        Assert.Equal(DateTime.MinValue, fixture.Processor.LastDataProcessedTime);
        Assert.Empty(fixture.Sent);

        fixture.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        var covered = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.PeerCovered, covered.Outcome);
        Assert.Null(covered.Reason);
        var delivery = Assert.Single(covered.Deliveries);
        Assert.Equal(OmfDeliveryState.PeerCovered, delivery.State);
        Assert.Equal(message.SerializedMessageId, delivery.SerializedMessageId);
        Assert.Null(delivery.EndpointId);
        Assert.Null(delivery.TargetUri);
        Assert.Null(delivery.LastStatusCode);
        Assert.Null(delivery.Receipt);
        Assert.Null(delivery.Reason);
        Assert.NotNull(delivery.CompletedAtUtc);

        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        // Stop/join the consumer before checking that a trimmed body was never dispatched.
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        Assert.Empty(fixture.Sent);
    }

    /// <summary>Verifies a default peer watermark does not trim, and promotion preserves identity, metadata, and stamped process time.</summary>
    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.Instance)]
    public async Task ProcessOmfMessage_HotPromotion_PreservesBodyAndAcceptsLocally(MessageType messageType)
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        await using var scope = fixture.CreateScope();
        var message = fixture.RegisterBodies(scope, messageType: messageType).Single();
        message.ProcessTimeTicks = 1; // Hot processing must stamp local time, not trust an incoming timestamp.
        var before = DateTime.UtcNow.Ticks;
        fixture.Processor.ProcessOmfMessage(message);
        var after = DateTime.UtcNow.Ticks;
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);

        fixture.WaitForSent(1);
        var sent = Assert.Single(fixture.Sent);
        Assert.Equal(message.SerializedMessageId, sent.SerializedMessageId);
        Assert.InRange(sent.ProcessTimeTicks, before, after);
        Assert.Equal(message.MessageBody, sent.MessageBody);
        Assert.Equal(message.MessageType, sent.MessageType);
        Assert.Equal(message.MessageAction, sent.MessageAction);
        Assert.Equal(message.ItemCount, sent.ItemCount);
        Assert.Equal(message.OmfVersion, sent.OmfVersion);
        Assert.Equal(message.PartitionKey, sent.PartitionKey);
        var result = await scope.WaitForAcceptanceAsync(TestTimeout);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, result.Outcome);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(ProcessorFixture.EndpointId, delivery.EndpointId);
        Assert.Equal(ProcessorFixture.Target.TargetUri, delivery.TargetUri);
        Assert.Equal(HttpStatusCode.Accepted, delivery.LastStatusCode);
        Assert.Equal(ProcessorFixture.Receipt, delivery.Receipt);
        Assert.Equal(OmfDeliveryState.Accepted, delivery.State);
    }

    /// <summary>Verifies hot conversion timestamps every body, including unscoped bodies, in strictly increasing UTC order.</summary>
    [Fact]
    public void ProcessOmfMessage_HotBurst_StampsMonotonicTicksForAllBodies()
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        var before = DateTime.UtcNow.Ticks;
        for (var index = 0; index < 128; index++)
        {
            fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));
        }

        var after = DateTime.UtcNow.Ticks;
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.WaitForSent(128);
        var messages = fixture.Sent.ToArray();
        Assert.All(messages, message =>
        {
            Assert.Null(message.SerializedMessageId);
            Assert.InRange(message.ProcessTimeTicks, before, after);
        });
        for (var index = 1; index < messages.Length; index++)
        {
            Assert.True(messages[index].ProcessTimeTicks > messages[index - 1].ProcessTimeTicks);
        }
    }

    /// <summary>Verifies hot progress follows endpoint acceptance and never exceeds the last dispatched body's process time.</summary>
    [Fact]
    public void LastDataProcessedTime_Hot_DispatchIsCappedByLiveAcceptanceWatermark()
    {
        using var fixture = new ProcessorFixture();
        fixture.Watermark = DateTime.MinValue;
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        Assert.Equal(DateTime.MinValue, fixture.Processor.LastDataProcessedTime);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));
        fixture.WaitForSent(1);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default); // Join dispatch before querying progress.
        var sent = Assert.Single(fixture.Sent);

        Assert.Equal(DateTime.MinValue, fixture.Processor.LastDataProcessedTime);
        var accepted = new DateTime(sent.ProcessTimeTicks - 1, DateTimeKind.Utc);
        fixture.Watermark = accepted;
        Assert.Equal(accepted, fixture.Processor.LastDataProcessedTime);
        fixture.Watermark = DateTime.MaxValue;
        Assert.Equal(new DateTime(sent.ProcessTimeTicks, DateTimeKind.Utc), fixture.Processor.LastDataProcessedTime);
        Assert.Equal(DateTimeKind.Utc, fixture.Processor.LastDataProcessedTime.Kind);

        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 2 }, MessageAction.Default));
        Assert.Equal(new DateTime(sent.ProcessTimeTicks, DateTimeKind.Utc), fixture.Processor.LastDataProcessedTime);
        Assert.Single(fixture.Sent);
    }

    /// <summary>Verifies the interface's default MinValue watermark blocks hot progress when an endpoint has no acceptance implementation.</summary>
    [Fact]
    public void LastDataProcessedTime_EndpointUsesDefaultInterfaceWatermark_RemainsMinValue()
    {
        using var fixture = new ProcessorFixture(configureWatermark: false);
        Assert.Equal(DateTime.MinValue, fixture.Endpoint.Object.GetAcceptanceWatermark());
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));
        fixture.WaitForSent(1);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);

        Assert.Equal(DateTime.MinValue, fixture.Processor.LastDataProcessedTime);
    }

    /// <summary>Verifies a finite accepted batch can cross the peer trim offset without needing subsequent traffic.</summary>
    [Fact]
    public void LastDataProcessedTime_AcceptedIdlePrimary_AdvancesBeyondLastDispatch()
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));
        fixture.WaitForSent(1);
        var sent = Assert.Single(fixture.Sent);
        fixture.Watermark = new DateTime(sent.ProcessTimeTicks, DateTimeKind.Utc);
        var beforeQuery = DateTime.UtcNow;

        var watermark = fixture.Processor.LastDataProcessedTime;

        Assert.InRange(watermark, beforeQuery, DateTime.UtcNow);
        Assert.True(watermark.Ticks > sent.ProcessTimeTicks);
        fixture.Watermark = new DateTime(sent.ProcessTimeTicks - 1, DateTimeKind.Utc);
        Assert.Equal(fixture.Watermark, fixture.Processor.LastDataProcessedTime);
    }

    /// <summary>Verifies warm and cold processing retain synchronous handoff progress even when acceptance is unknown.</summary>
    [Theory]
    [InlineData(FailoverMode.Warm, FailoverRole.Primary)]
    [InlineData(FailoverMode.Warm, FailoverRole.Secondary)]
    [InlineData(FailoverMode.Cold, FailoverRole.Primary)]
    [InlineData(FailoverMode.Cold, FailoverRole.Secondary)]
    public void LastDataProcessedTime_NonHot_RetainsHandoffBehavior(FailoverMode mode, FailoverRole role)
    {
        using var fixture = new ProcessorFixture();
        fixture.Watermark = DateTime.MinValue;
        fixture.Processor.UpdateMode(mode, true);
        fixture.Processor.UpdateState(role, default);
        var before = DateTime.UtcNow;

        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));

        Assert.Single(fixture.Sent);
        Assert.InRange(fixture.Processor.LastDataProcessedTime, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, fixture.Processor.LastDataProcessedTime.Kind);
    }

    /// <summary>Verifies demotion leaves locally dispatched work intact while new buffered work completes through peer coverage.</summary>
    [Theory]
    [InlineData(OmfAwaitableEndpointMode.AllActiveEndpointsAtDispatch)]
    [InlineData(OmfAwaitableEndpointMode.AnyCompleteEndpoint)]
    public async Task UpdateState_Demotion_MixesLocalAcceptanceAndPeerCoverage(OmfAwaitableEndpointMode endpointMode)
    {
        using var fixture = new ProcessorFixture();
        fixture.AcceptOnSend = false;
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        await using var scope = fixture.CreateScope(endpointMode);
        var bodies = fixture.RegisterBodies(scope, count: 2);
        fixture.Processor.ProcessOmfMessage(bodies[0]);
        fixture.WaitForSent(1);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        fixture.Processor.ProcessOmfMessage(bodies[1]);

        var buffered = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, buffered.Outcome);
        Assert.Equal(2, buffered.Deliveries.Count);
        Assert.Equal(OmfReasonCode.AwaitingPeerCoverage,
            Assert.Single(buffered.Deliveries, delivery => delivery.SerializedMessageId == bodies[1].SerializedMessageId).Reason.Code);

        fixture.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        var pending = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, pending.Outcome);
        Assert.Equal(OmfDeliveryState.Pending, Assert.Single(pending.Deliveries, delivery => delivery.SerializedMessageId == bodies[0].SerializedMessageId).State);
        Assert.Equal(OmfDeliveryState.PeerCovered, Assert.Single(pending.Deliveries, delivery => delivery.SerializedMessageId == bodies[1].SerializedMessageId).State);

        fixture.Accept(bodies[0].SerializedMessageId.Value);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.PeerCovered, result.Outcome);
        Assert.Single(fixture.Sent);
    }

    /// <summary>Verifies leaving hot mode deletes only queued bodies, not deliveries already owned by the endpoint.</summary>
    [Theory]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    [InlineData(FailoverMode.NotConfigured)]
    public async Task UpdateMode_LeavingHot_DiscardsOnlyQueuedBodies(FailoverMode nextMode)
    {
        using var fixture = new ProcessorFixture();
        fixture.AcceptOnSend = false;
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        await using var localScope = fixture.CreateScope();
        var local = fixture.RegisterBodies(localScope).Single();
        fixture.Processor.ProcessOmfMessage(local);
        fixture.WaitForSent(1);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        await using var bufferedScope = fixture.CreateScope();
        var buffered = fixture.RegisterBodies(bufferedScope).Single();
        fixture.Processor.ProcessOmfMessage(buffered);

        fixture.Processor.UpdateMode(nextMode, true);

        var deleted = await bufferedScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.Discarded, deleted.Outcome);
        Assert.Equal(OmfReasonCode.BufferDeleted, deleted.Reason.Code);
        var delivery = Assert.Single(deleted.Deliveries);
        Assert.Equal(buffered.SerializedMessageId, delivery.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.Discarded, delivery.State);
        Assert.Null(delivery.EndpointId);
        Assert.Null(delivery.TargetUri);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, (await localScope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
        fixture.Accept(local.SerializedMessageId.Value);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await localScope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
        Assert.Single(fixture.Sent);
    }

    /// <summary>Verifies a scope opened before entering hot mode can materialize into its failover buffer and complete on promotion.</summary>
    [Fact]
    public async Task UpdateMode_EnteringHotWithActiveScope_SupportsBufferedAcceptance()
    {
        using var fixture = new ProcessorFixture();
        await using var scope = fixture.CreateScope();
        var body = fixture.RegisterBodies(scope).Single();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.ProcessOmfMessage(body);
        var pending = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfReasonCode.AwaitingPeerCoverage, pending.Reason.Code);

        fixture.Processor.UpdateState(FailoverRole.Primary, default);

        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await scope.WaitForAcceptanceAsync(TestTimeout)).Outcome);
        fixture.WaitForSent(1);
        Assert.Equal(body.SerializedMessageId, Assert.Single(fixture.Sent).SerializedMessageId);
    }

    /// <summary>Verifies shutdown is not a buffer-deletion disposition, including volatile queues drained during disposal.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_HotSecondary_DoesNotReportBufferDeletionOrLoss(bool persistent)
    {
        using var fixture = new ProcessorFixture(persistent: persistent);
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        await using var scope = fixture.CreateScope();
        fixture.Processor.ProcessOmfMessage(fixture.RegisterBodies(scope).Single());

        fixture.Processor.Dispose();
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.TimedOut, result.Outcome);
        Assert.Equal(OmfReasonCode.AwaitingPeerCoverage, result.Reason.Code);
        Assert.Equal(OmfDeliveryState.Pending, Assert.Single(result.Deliveries).State);
        Assert.Empty(fixture.Sent);
    }

    /// <summary>Verifies overflow reports both an older evicted body and an oversized body lost during its own enqueue.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessOmfMessage_VolatileOverflow_ReportsBufferFullForEvictedBody(bool oversizedBody)
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        await using var scope = fixture.CreateScope();
        var bodySizeMb = oversizedBody ? BufferingConstants.DefaultVolatileMemorySizeMb + 1 : 11;
        var body = fixture.RegisterBodies(scope, bodySize: bodySizeMb * 1024 * 1024).Single();
        fixture.Processor.ProcessOmfMessage(body);
        if (!oversizedBody)
        {
            fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[11 * 1024 * 1024], MessageAction.Default));
        }

        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.BufferFull, result.Reason.Code);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(body.SerializedMessageId, delivery.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.Discarded, delivery.State);
        Assert.Null(delivery.EndpointId);
        Assert.Null(delivery.TargetUri);
        Assert.Null(delivery.Receipt);

        // Even after the surviving unscoped body is accepted, local coverage must not pass the lost body.
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        if (!oversizedBody)
        {
            fixture.WaitForSent(1);
        }

        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        Assert.Equal(DateTime.MinValue, fixture.Processor.LastDataProcessedTime);
    }

    /// <summary>Verifies real disk spill and processor restart preserve both legacy unscoped and new scoped records with timestamps.</summary>
    [Fact]
    public async Task ProcessOmfMessage_PersistentSpillAndRestart_PreservesLegacyAndScopedBodies()
    {
        using var fixture = new ProcessorFixture(persistent: true);
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        await using var scope = fixture.CreateScope();
        var scoped = fixture.RegisterBodies(scope, bodySize: 600 * 1024).Single();
        var unscoped = new SerializedOmfMessage(MessageType.Instance, new byte[600 * 1024], MessageAction.Update, 3, OmfVersion.Omf20, PartitionKey.Key5);
        var before = DateTime.UtcNow.Ticks;
        fixture.Processor.ProcessOmfMessage(unscoped);
        fixture.Processor.ProcessOmfMessage(scoped);
        var after = DateTime.UtcNow.Ticks;
        fixture.Processor.Dispose(); // Flush the remaining volatile body to disk without reporting loss.
        Assert.NotEmpty(Directory.GetFiles(fixture.DirectoryPath, "*", SearchOption.AllDirectories));
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);

        fixture.Restart();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.WaitForSent(2);

        var sent = fixture.Sent.ToArray();
        Assert.Null(sent[0].SerializedMessageId);
        Assert.Equal(scoped.SerializedMessageId, sent[1].SerializedMessageId);
        Assert.All(sent, message => Assert.InRange(message.ProcessTimeTicks, before, after));
        Assert.True(sent[1].ProcessTimeTicks > sent[0].ProcessTimeTicks);
        Assert.Equal(unscoped.MessageBody, sent[0].MessageBody);
        Assert.Equal(unscoped.PartitionKey, sent[0].PartitionKey);
        Assert.Equal(unscoped.OmfVersion, sent[0].OmfVersion);
        Assert.Equal(scoped.MessageBody, sent[1].MessageBody);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await scope.WaitForAcceptanceAsync(TestTimeout)).Outcome);
    }

    /// <summary>Verifies disabling acceptance in hot mode discards buffered tracking without deleting data or changing real endpoint deliveries.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateMode_HotOptInDisabledThenReenabled_RetainsDataAndFirstFinalOutcomes(bool trimBuffered)
    {
        using var fixture = new ProcessorFixture();
        fixture.AcceptOnSend = false;
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        await using var localScope = fixture.CreateScope();
        var local = fixture.RegisterBodies(localScope).Single();
        fixture.Processor.ProcessOmfMessage(local);
        fixture.WaitForSent(1);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        await using var bufferedScope = fixture.CreateScope();
        var buffered = fixture.RegisterBodies(bufferedScope).Single();
        fixture.Processor.ProcessOmfMessage(buffered);
        var wait = bufferedScope.WaitForAcceptanceAsync(TestTimeout);
        Assert.False(wait.IsCompleted);

        fixture.Processor.UpdateMode(FailoverMode.Hot, false);

        Assert.Equal(FailoverMode.Hot, fixture.Processor.CurrentFailoverMode);
        Assert.Throws<NotSupportedException>(() => fixture.Coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        var discarded = await wait.WaitAsync(TestTimeout);
        AssertTrackingDisabled(discarded, buffered.SerializedMessageId.Value);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, (await localScope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
        if (trimBuffered)
        {
            fixture.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        }

        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        var expectedSends = trimBuffered ? 1 : 2;
        if (!trimBuffered)
        {
            fixture.WaitForSent(expectedSends);
        }

        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        Assert.Equal(expectedSends, fixture.Sent.Count);
        if (!trimBuffered)
        {
            var legacy = fixture.Sent.Last();
            Assert.Equal(buffered.MessageBody, legacy.MessageBody);
            Assert.IsType<SerializedOmfMessage>(legacy);
            Assert.Equal(0, legacy.ProcessTimeTicks);
            Assert.Null(legacy.SerializedMessageId);
        }

        fixture.Accept(local.SerializedMessageId.Value);
        var accepted = await localScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, accepted.Outcome);
        Assert.Equal(HttpStatusCode.Accepted, Assert.Single(accepted.Deliveries).LastStatusCode);
        Assert.Equal(ProcessorFixture.Receipt, Assert.Single(accepted.Deliveries).Receipt);

        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        var later = await bufferedScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        AssertTrackingDisabled(later, buffered.SerializedMessageId.Value);
        Assert.Equal(Assert.Single(discarded.Deliveries).CompletedAtUtc, Assert.Single(later.Deliveries).CompletedAtUtc);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await localScope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
        await using var newScope = fixture.CreateScope();
        var newBody = fixture.RegisterBodies(newScope).Single();
        fixture.Processor.ProcessOmfMessage(newBody);
        fixture.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(OmfAcceptanceOutcome.PeerCovered, (await newScope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    /// <summary>Verifies re-enabling while a discarded body is physically buffered never turns a later trim into peer-coverage success.</summary>
    [Fact]
    public async Task UpdateMode_HotReenabledBeforeBufferedBodyIsTrimmed_DoesNotResurrectDiscardedTracking()
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        await using var scope = fixture.CreateScope();
        var body = fixture.RegisterBodies(scope).Single();
        fixture.Processor.ProcessOmfMessage(body);

        fixture.Processor.UpdateMode(FailoverMode.Hot, false);
        var discarded = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        AssertTrackingDisabled(discarded, body.SerializedMessageId.Value);
        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        fixture.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));

        var trimmed = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        AssertTrackingDisabled(trimmed, body.SerializedMessageId.Value);
        Assert.Equal(Assert.Single(discarded.Deliveries).CompletedAtUtc, Assert.Single(trimmed.Deliveries).CompletedAtUtc);
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        Assert.Empty(fixture.Sent);
    }

    /// <summary>Verifies late bodies from a non-hot scope are discarded diagnostically but still retained for legacy send or trim, never false peer coverage.</summary>
    [Theory]
    [InlineData(FailoverMode.Warm, false)]
    [InlineData(FailoverMode.Warm, true)]
    [InlineData(FailoverMode.NotConfigured, false)]
    [InlineData(FailoverMode.NotConfigured, true)]
    public async Task ProcessOmfMessage_ScopeMaterializesAfterLegacyHotSwitch_DiscardsTrackingButKeepsLegacyBehavior(FailoverMode originalMode, bool trimBuffered)
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(originalMode);
        await using var scope = fixture.CreateScope();
        fixture.Processor.UpdateMode(FailoverMode.Hot);
        var body = fixture.RegisterBodies(scope).Single();

        fixture.Processor.ProcessOmfMessage(body);

        var discarded = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        AssertTrackingDisabled(discarded, body.SerializedMessageId.Value);
        Assert.Empty(fixture.Sent);
        if (trimBuffered)
        {
            fixture.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        }

        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        if (!trimBuffered)
        {
            fixture.WaitForSent(1);
        }

        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        if (trimBuffered)
        {
            Assert.Empty(fixture.Sent);
        }
        else
        {
            var legacy = Assert.Single(fixture.Sent);
            Assert.Equal(body.MessageBody, legacy.MessageBody);
            Assert.Equal(body.MessageType, legacy.MessageType);
            Assert.Equal(body.MessageAction, legacy.MessageAction);
            Assert.Equal(body.ItemCount, legacy.ItemCount);
            Assert.Equal(body.OmfVersion, legacy.OmfVersion);
            Assert.Equal(body.PartitionKey, legacy.PartitionKey);
            Assert.Null(legacy.SerializedMessageId);
            Assert.Equal(0, legacy.ProcessTimeTicks);
        }

        fixture.Processor.UpdateMode(FailoverMode.Hot, true);
        var later = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        AssertTrackingDisabled(later, body.SerializedMessageId.Value);
        Assert.Equal(Assert.Single(discarded.Deliveries).CompletedAtUtc, Assert.Single(later.Deliveries).CompletedAtUtc);
        fixture.Endpoint.Verify(endpoint => endpoint.GetAcceptanceWatermark(), Times.Never);
    }

    /// <summary>Verifies legacy hot handoff progress recovers after buffer loss or a dispatch exception without querying endpoint acceptance.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LastDataProcessedTime_LegacyHotAfterLoss_AdvancesToNextSuccessfulHandoff(bool overflow)
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot);
        fixture.Endpoint.Setup(endpoint => endpoint.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback((ISerializedOmfMessage message) =>
        {
            if (message.MessageBody[0] == 0)
            {
                throw new InvalidOperationException("Simulated handoff failure.");
            }

            fixture.Sent.Enqueue(message);
        });
        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data,
            new byte[overflow ? (BufferingConstants.DefaultVolatileMemorySizeMb + 1) * 1024 * 1024 : 1], MessageAction.Default));
        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));
        var ticks = (long)TestUtilities.GetFieldValueFromObject("_lastReceivedTimeTicks", fixture.Processor);

        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.WaitForSent(1);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);

        Assert.Equal(new DateTime(ticks, DateTimeKind.Utc), fixture.Processor.LastDataProcessedTime);
        Assert.Equal(0, Assert.Single(fixture.Sent).ProcessTimeTicks);
        fixture.Endpoint.Verify(endpoint => endpoint.SendMessage(It.IsAny<ISerializedOmfMessage>()), Times.Exactly(overflow ? 1 : 2));
        fixture.Endpoint.Verify(endpoint => endpoint.GetAcceptanceWatermark(), Times.Never);
    }

    /// <summary>Verifies legacy failover disk spill retains trim timestamps across restart while stripping them from writer handoff.</summary>
    [Fact]
    public void ProcessOmfMessage_LegacyPersistentRestart_PreservesBufferTicksButForwardsUntimestampedBodies()
    {
        using var fixture = new ProcessorFixture(persistent: true);
        fixture.Processor.UpdateMode(FailoverMode.Hot);
        var first = new SerializedOmfMessage(MessageType.Instance, new byte[600 * 1024], MessageAction.Update, 3, OmfVersion.Omf20, PartitionKey.Key5);
        var second = new SerializedOmfMessage(MessageType.Data, new byte[600 * 1024], MessageAction.Default, 2, OmfVersion.Omf20, PartitionKey.Key2);
        fixture.Processor.ProcessOmfMessage(first);
        var firstTicks = (long)TestUtilities.GetFieldValueFromObject("_lastReceivedTimeTicks", fixture.Processor);
        fixture.Processor.ProcessOmfMessage(second);
        var lastTicks = (long)TestUtilities.GetFieldValueFromObject("_lastReceivedTimeTicks", fixture.Processor);
        Assert.True(lastTicks > firstTicks);
        fixture.Processor.Dispose();
        Assert.NotEmpty(Directory.GetFiles(fixture.DirectoryPath, "*", SearchOption.AllDirectories));

        fixture.Restart();
        fixture.Processor.UpdateMode(FailoverMode.Hot);
        fixture.Processor.UpdateState(FailoverRole.Secondary, new DateTime(firstTicks, DateTimeKind.Utc));
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.WaitForSent(1);
        fixture.Processor.UpdateState(FailoverRole.Secondary, default);

        var sent = Assert.Single(fixture.Sent);
        Assert.IsType<SerializedOmfMessage>(sent);
        Assert.Equal(second.MessageBody, sent.MessageBody);
        Assert.Equal(second.MessageType, sent.MessageType);
        Assert.Equal(second.ItemCount, sent.ItemCount);
        Assert.Equal(second.PartitionKey, sent.PartitionKey);
        Assert.Equal(second.OmfVersion, sent.OmfVersion);
        Assert.Equal(0, sent.ProcessTimeTicks);
        Assert.Null(sent.SerializedMessageId);
        Assert.Equal(new DateTime(lastTicks, DateTimeKind.Utc), fixture.Processor.LastDataProcessedTime);
        fixture.Endpoint.Verify(endpoint => endpoint.GetAcceptanceWatermark(), Times.Never);
    }

    /// <summary>Checks tracking was explicitly finalized without an invented HTTP or peer-coverage success.</summary>
    private static void AssertTrackingDisabled(OmfAcceptanceResult result, Guid bodyId)
    {
        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.AcceptanceTrackingDisabled, result.Reason.Code);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(bodyId, delivery.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.Discarded, delivery.State);
        Assert.Equal(OmfReasonCode.AcceptanceTrackingDisabled, delivery.Reason.Code);
        Assert.Null(delivery.EndpointId);
        Assert.Null(delivery.TargetUri);
        Assert.Null(delivery.LastStatusCode);
        Assert.Null(delivery.Receipt);
        Assert.NotNull(delivery.CompletedAtUtc);
    }

    private sealed class ProcessorFixture : IDisposable
    {
        public const string EndpointId = "local";
        public static readonly OmfDeliveryTarget Target = new(EndpointId, new Uri("https://local.example.com/omf"));
        public static readonly OmfIngressReceipt Receipt = new(Guid.NewGuid(), 0, TimeSpan.Zero);
        private readonly Mock<IConfigurationProvider> _configuration = new();
        private long _watermarkTicks = DateTime.MaxValue.Ticks;

        /// <summary>Creates isolated real failover queues and a local endpoint that registers deliveries before simulating 202 acceptance.</summary>
        public ProcessorFixture(bool persistent = false, bool configureWatermark = true)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), nameof(FailoverDataMessageProcessorAcceptance_Tests), Guid.NewGuid().ToString("N"));
            var configuration = new BufferingConfiguration
            {
                EnablePersistentBuffering = persistent,
                MaxBufferSizeMB = 1,
                BufferLocation = DirectoryPath,
            };
            ICollection<string> errors = Array.Empty<string>();
            _configuration.Setup(provider => provider.TryGetConfiguration(
                EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.BufferingFacetName, out configuration, out errors)).Returns(true);
            _configuration.Setup(provider => provider.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns(DirectoryPath);
            Coordinator.SetOmfVersion(OmfVersion.Omf20);
            Endpoint = new Mock<IOmfDataEndpointManager> { CallBase = true };
            if (configureWatermark)
            {
                Endpoint.Setup(endpoint => endpoint.GetAcceptanceWatermark()).Returns(() => Watermark);
            }

            Endpoint.Setup(endpoint => endpoint.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback((ISerializedOmfMessage message) =>
            {
                if (message.SerializedMessageId is Guid id)
                {
                    Coordinator.RegisterDeliveries(id, new[] { Target });
                    if (AcceptOnSend)
                    {
                        Accept(id);
                    }
                }

                Sent.Enqueue(message);
            });
            Restart();
        }

        public string DirectoryPath { get; }
        public OmfAwaitableCoordinator Coordinator { get; } = new();
        public Mock<IOmfDataEndpointManager> Endpoint { get; }
        public ConcurrentQueue<ISerializedOmfMessage> Sent { get; } = new();
        public FailoverDataMessageProcessor Processor { get; private set; }
        public bool AcceptOnSend { get; set; } = true;
        public DateTime Watermark
        {
            get => new(Interlocked.Read(ref _watermarkTicks), DateTimeKind.Utc);
            set => Interlocked.Exchange(ref _watermarkTicks, value.Ticks);
        }

        /// <summary>Creates scopes through the public coordinator API; the caller owns their asynchronous lifetime.</summary>
        public OmfAwaitableScopeState CreateScope(OmfAwaitableEndpointMode mode = OmfAwaitableEndpointMode.AllActiveEndpointsAtDispatch)
        {
            Assert.True(Coordinator.TryCreateScope(new OmfAwaitableScopeOptions { EndpointMode = mode }, out var scope));
            return scope;
        }

        /// <summary>Admits all items, registers serialized bodies, and closes materialization before processor handoff.</summary>
        public SerializedOmfMessage[] RegisterBodies(OmfAwaitableScopeState scope, int count = 1, int bodySize = 2, MessageType messageType = MessageType.Data)
        {
            scope.EnterWrite();
            Coordinator.RecordAdmitted(scope, count);
            scope.ExitWrite();
            var bodies = new SerializedOmfMessage[count];
            for (var index = 0; index < count; index++)
            {
                var id = Guid.NewGuid();
                bodies[index] = new SerializedOmfMessage(messageType, new byte[bodySize], MessageAction.Update, 1, OmfVersion.Omf20, PartitionKey.Key5)
                {
                    SerializedMessageId = id,
                };
                Coordinator.RegisterBody(id, new Dictionary<ScopeToken, int> { [scope] = 1 });
            }

            Coordinator.CloseMaterialization(scope);
            return bodies;
        }

        /// <summary>Reports the endpoint's final 202 response for an already registered local delivery.</summary>
        public void Accept(Guid id) => Coordinator.RecordDisposition(id, EndpointId, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);

        /// <summary>Waits for asynchronous consumer handoff, with the send callback publishing only after coordinator registration.</summary>
        public void WaitForSent(int count) => Assert.True(SpinWait.SpinUntil(() => Sent.Count >= count, TestTimeout), $"Expected {count} dispatches; observed {Sent.Count}.");

        /// <summary>Reopens the same on-disk buffers with a fresh processor while retaining scope tracking and the endpoint simulation.</summary>
        public void Restart()
        {
            Processor?.Dispose();
            Processor = new FailoverDataMessageProcessor(NullLogger.Instance, _configuration.Object, Endpoint.Object, Coordinator);
            Processor.Initialize();
        }

        /// <summary>Joins consumers and releases queues before deleting only this fixture's unique temporary directory.</summary>
        public void Dispose()
        {
            Processor?.Dispose();
            Coordinator.Dispose();
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}