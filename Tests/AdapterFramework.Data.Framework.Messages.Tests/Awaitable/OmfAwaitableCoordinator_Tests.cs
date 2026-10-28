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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using Xunit;

namespace AdapterFramework.Data.Framework.Messages.Tests.Awaitable;

public sealed class OmfAwaitableCoordinator_Tests : IDisposable
{
    private const string EndpointA = "EndpointA";
    private const string EndpointB = "EndpointB";
    private static readonly TimeSpan LongWait = TimeSpan.FromSeconds(10);
    private static readonly OmfDeliveryTarget TargetA = new(EndpointA, new Uri("https://a.example.com/omf"));
    private static readonly OmfDeliveryTarget TargetB = new(EndpointB, new Uri("https://b.example.com/omf"));
    private static readonly OmfIngressReceipt Receipt = new(Guid.NewGuid(), 0, TimeSpan.FromSeconds(1));

    private readonly OmfAwaitableCoordinator _coordinator;

    public OmfAwaitableCoordinator_Tests()
    {
        _coordinator = new OmfAwaitableCoordinator(maxActiveScopes: 2);
        _coordinator.SetOmfVersion(OmfVersion.Omf20);
    }

    public void Dispose() => _coordinator.Dispose();

    [Fact]
    public void TryCreateScope_OmfVersionNotSetOrOmf12_ThrowsNotSupported()
    {
        using var coordinator = new OmfAwaitableCoordinator();
        Assert.Throws<NotSupportedException>(() => coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));

        coordinator.SetOmfVersion(OmfVersion.Omf12);
        Assert.Throws<NotSupportedException>(() => coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
    }

    /// <summary>Verifies Hot always rejects scopes by throwing, including when the scope capacity is exhausted.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryCreateScope_Hot_ThrowsNotSupported(bool atCapacity)
    {
        await using var first = atCapacity ? CreateScope() : null;
        await using var second = atCapacity ? CreateScope() : null;
        foreach (var mode in new[] { FailoverMode.NotConfigured, FailoverMode.Warm, FailoverMode.Cold })
        {
            _coordinator.SetFailoverMode(mode);
            _coordinator.SetFailoverMode(FailoverMode.Hot);
            Assert.Equal(FailoverMode.Hot, _coordinator.CurrentFailoverMode);
            Assert.Throws<NotSupportedException>(() => _coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        }

        Assert.Equal(atCapacity ? 2 : 0, _coordinator.ActiveScopeCount);
    }

    /// <summary>Verifies scopes with no admitted data still complete as filtered outside Hot mode.</summary>
    [Theory]
    [InlineData(FailoverMode.NotConfigured)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public async Task TryCreateScope_NonHot_IsSupported(FailoverMode mode)
    {
        _coordinator.SetFailoverMode(mode);

        await using var scope = CreateScope();

        Assert.Equal(OmfAcceptanceOutcome.Filtered, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    [Fact]
    public async Task TryCreateScope_AtActiveScopeLimit_ReturnsFalseUntilAScopeIsDisposed()
    {
        var first = CreateScope();
        await using var second = CreateScope();

        Assert.False(_coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        Assert.Equal(2, _coordinator.ActiveScopeCount);

        await first.DisposeAsync();
        await using var third = CreateScope();

        Assert.Equal(2, _coordinator.ActiveScopeCount);
    }

    [Fact]
    public void TryCreateScope_InvalidDefaultTimeout_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _coordinator.TryCreateScope(new OmfAwaitableScopeOptions { DefaultWaitTimeout = TimeSpan.Zero }, out _));
    }

    [Fact]
    public async Task Wait_NothingAdmitted_IsFiltered()
    {
        await using var scope = CreateScope();

        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Filtered, result.Outcome);
        Assert.Null(result.Reason);
        Assert.Empty(result.Deliveries);
    }

    [Fact]
    public async Task Wait_AllEndpointsAccept_IsAcceptedWithReceipts()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 4, TargetA, TargetB);

        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
        var pending = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.TimedOut, pending.Outcome);
        Assert.Equal(OmfReasonCode.Queued, pending.Reason.Code);

        _coordinator.RecordDisposition(bodyId, EndpointB, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Accepted, result.Outcome);
        Assert.Null(result.Reason);
        Assert.Equal(2, result.Deliveries.Count);
        Assert.All(result.Deliveries, delivery =>
        {
            Assert.Equal(OmfDeliveryState.Accepted, delivery.State);
            Assert.Equal(bodyId, delivery.SerializedMessageId);
            Assert.Equal(HttpStatusCode.Accepted, delivery.LastStatusCode);
            Assert.Equal(Receipt, delivery.Receipt);
            Assert.Null(delivery.Reason);
            Assert.NotNull(delivery.CompletedAtUtc);
        });
        Assert.Equal(TargetA.TargetUri, result.Deliveries.Single(delivery => delivery.EndpointId == EndpointA).TargetUri);
    }

    [Fact]
    public async Task Wait_PendingWaitCompletesWhenLastDeliveryIsAccepted()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);

        var wait = scope.WaitForAcceptanceAsync(LongWait);
        Assert.False(wait.IsCompleted);

        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);

        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await wait).Outcome);
    }

    [Fact]
    public async Task Wait_AllActiveEndpoints_OneRejection_IsRejectedEarlyAndLaterWaitsShowOtherDeliveries()
    {
        await using var scope = CreateScope();
        var reason = new OmfOutcomeReason(OmfReasonCode.RejectedByEndpoint, "400 Bad Request");
        scope.EnterWrite();
        _coordinator.RecordAdmitted(scope, 2);
        scope.ExitWrite();
        var bodyId = Guid.NewGuid();
        _coordinator.RegisterBody(bodyId, new Dictionary<ScopeToken, int> { [scope] = 2 });
        _coordinator.RegisterDeliveries(bodyId, new[] { TargetA, TargetB });

        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Rejected, HttpStatusCode.BadRequest, reason);
        var rejected = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Rejected, rejected.Outcome);
        Assert.Equal(reason, rejected.Reason);
        Assert.Equal(OmfDeliveryState.Pending, rejected.Deliveries.Single(delivery => delivery.EndpointId == EndpointB).State);

        _coordinator.RecordDisposition(bodyId, EndpointB, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
        var later = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Rejected, later.Outcome);
        Assert.Equal(OmfDeliveryState.Accepted, later.Deliveries.Single(delivery => delivery.EndpointId == EndpointB).State);
    }

    [Fact]
    public async Task Wait_AnyCompleteEndpoint_OneEndpointAcceptsEveryBody_IsAccepted()
    {
        await using var scope = CreateScope(new OmfAwaitableScopeOptions { EndpointMode = OmfAwaitableEndpointMode.AnyCompleteEndpoint });
        var reason = new OmfOutcomeReason(OmfReasonCode.RejectedByEndpoint, string.Empty);
        var (firstBody, secondBody) = SimulateTwoBodies(scope, new[] { TargetA, TargetB }, new[] { TargetA, TargetB });

        _coordinator.RecordDisposition(firstBody, EndpointA, OmfDeliveryState.Rejected, HttpStatusCode.Conflict, reason);
        _coordinator.RecordDisposition(firstBody, EndpointB, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);

        _coordinator.RecordDisposition(secondBody, EndpointB, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);

        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    [Fact]
    public async Task Wait_AnyCompleteEndpoint_AcceptancesFromDifferentEndpointsAreNotCombined()
    {
        await using var scope = CreateScope(new OmfAwaitableScopeOptions { EndpointMode = OmfAwaitableEndpointMode.AnyCompleteEndpoint });
        var reason = new OmfOutcomeReason(OmfReasonCode.RejectedByEndpoint, string.Empty);
        var (firstBody, secondBody) = SimulateTwoBodies(scope, new[] { TargetA, TargetB }, new[] { TargetA, TargetB });

        _coordinator.RecordDisposition(firstBody, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
        _coordinator.RecordDisposition(secondBody, EndpointB, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
        _coordinator.RecordDisposition(firstBody, EndpointB, OmfDeliveryState.Rejected, HttpStatusCode.BadRequest, reason);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);

        _coordinator.RecordDisposition(secondBody, EndpointA, OmfDeliveryState.Rejected, HttpStatusCode.BadRequest, reason);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Rejected, result.Outcome);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task Wait_AnyCompleteEndpoint_DisjointSnapshots_IsDiscarded()
    {
        await using var scope = CreateScope(new OmfAwaitableScopeOptions { EndpointMode = OmfAwaitableEndpointMode.AnyCompleteEndpoint });

        SimulateTwoBodies(scope, new[] { TargetA }, new[] { TargetB });
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.NoEndpoints, result.Reason.Code);
    }

    [Fact]
    public async Task Wait_NotMaterialized_TimesOutWithAwaitingFlush()
    {
        await using var scope = CreateScope();
        scope.EnterWrite();
        _coordinator.RecordAdmitted(scope, 3);
        scope.ExitWrite();

        var result = await scope.WaitForAcceptanceAsync(TimeSpan.FromMilliseconds(50));

        Assert.Equal(OmfAcceptanceOutcome.TimedOut, result.Outcome);
        Assert.Equal(OmfReasonCode.AwaitingFlush, result.Reason.Code);
        Assert.Equal(OmfPipelineStage.Grouping, result.Reason.Stage);
    }

    [Fact]
    public async Task Wait_RetryingDelivery_TimesOutWithLastStatus()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);
        var retrying = new OmfOutcomeReason(OmfReasonCode.Retrying, "401 Unauthorized, attempt 3");

        _coordinator.RecordAttempt(bodyId, EndpointA, HttpStatusCode.Unauthorized, retrying);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.TimedOut, result.Outcome);
        Assert.Equal(retrying, result.Reason);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(OmfDeliveryState.Pending, delivery.State);
        Assert.Equal(HttpStatusCode.Unauthorized, delivery.LastStatusCode);
        Assert.Null(delivery.CompletedAtUtc);
    }

    [Fact]
    public async Task Wait_RepeatedWaitObservesCompletionAfterEarlierTimeout()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);

        Assert.Equal(OmfAcceptanceOutcome.TimedOut, (await scope.WaitForAcceptanceAsync(TimeSpan.FromMilliseconds(20))).Outcome);
        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);

        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    [Fact]
    public async Task Wait_DefaultTimeoutFromOptions_IsUsedWhenNoTimeoutIsPassed()
    {
        await using var scope = CreateScope(new OmfAwaitableScopeOptions { DefaultWaitTimeout = TimeSpan.FromMilliseconds(20) });
        SimulateWriteAndDispatch(scope, 1, TargetA);

        var result = await scope.WaitForAcceptanceAsync().WaitAsync(LongWait);

        Assert.Equal(OmfAcceptanceOutcome.TimedOut, result.Outcome);
    }

    [Fact]
    public async Task Wait_MaterializationCountShortfall_IsDiscardedWithCountMismatch()
    {
        await using var scope = CreateScope();
        scope.EnterWrite();
        _coordinator.RecordAdmitted(scope, 5);
        scope.ExitWrite();
        _coordinator.RegisterBody(Guid.NewGuid(), new Dictionary<ScopeToken, int> { [scope] = 4 });

        _coordinator.CloseMaterialization(scope);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.CountMismatch, result.Reason.Code);
        Assert.Equal(OmfPipelineStage.Serialization, result.Reason.Stage);
    }

    [Fact]
    public async Task Wait_NoEndpointsAtDispatch_IsDiscarded()
    {
        await using var scope = CreateScope();

        SimulateWriteAndDispatch(scope, 2);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.NoEndpoints, result.Reason.Code);
    }

    [Fact]
    public async Task Wait_ItemsDiscardedBeforeSerialization_IsDiscardedWithoutWaitingForMaterialization()
    {
        await using var scope = CreateScope();
        var reason = new OmfOutcomeReason(OmfReasonCode.ItemTooLarge, "Value 8 of stream S1 is too large.");
        scope.EnterWrite();
        _coordinator.RecordAdmitted(scope, 3);
        scope.ExitWrite();

        _coordinator.RecordItemsDiscarded(scope, 1, reason);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(reason, result.Reason);
    }

    [Fact]
    public async Task Wait_EndpointRemoved_DiscardsPendingDeliveries()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA, TargetB);
        var reason = new OmfOutcomeReason(OmfReasonCode.EndpointRemoved, EndpointB);
        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);

        _coordinator.RecordEndpointDiscarded(EndpointB, reason);
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(OmfDeliveryState.Accepted, result.Deliveries.Single(delivery => delivery.EndpointId == EndpointA).State);
    }

    /// <summary>
    /// Verifies that a delivery is reported as pending only while a scope tracks it: not for an unknown body or another
    /// endpoint, and no longer once its final disposition is recorded.
    /// </summary>
    [Fact]
    public async Task IsDeliveryPending_TracksOnlyUnfinishedDeliveriesOfKnownBodies()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);

        Assert.True(_coordinator.IsDeliveryPending(bodyId, EndpointA));
        Assert.False(_coordinator.IsDeliveryPending(bodyId, EndpointB));
        Assert.False(_coordinator.IsDeliveryPending(Guid.NewGuid(), EndpointA));

        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);

        Assert.False(_coordinator.IsDeliveryPending(bodyId, EndpointA));
    }

    [Fact]
    public async Task Wait_FirstDispositionWins()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);

        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
        _coordinator.RecordEndpointDiscarded(EndpointA, new OmfOutcomeReason(OmfReasonCode.EndpointRemoved, string.Empty));
        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);

        Assert.Equal(OmfAcceptanceOutcome.Accepted, result.Outcome);
        Assert.Equal(OmfDeliveryState.Accepted, Assert.Single(result.Deliveries).State);
    }

    [Fact]
    public async Task Wait_TwoScopesSharingABody_KeepIndependentOutcomes()
    {
        await using var first = CreateScope();
        await using var second = CreateScope();
        foreach (var scope in new[] { first, second })
        {
            scope.EnterWrite();
            _coordinator.RecordAdmitted(scope, 2);
            scope.ExitWrite();
        }

        var bodyId = Guid.NewGuid();
        _coordinator.RegisterBody(bodyId, new Dictionary<ScopeToken, int> { [first] = 2, [second] = 1 });
        _coordinator.CloseMaterialization(first);
        _coordinator.RegisterDeliveries(bodyId, new[] { TargetA });
        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);

        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await first.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
        var secondResult = await second.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, secondResult.Outcome);
        Assert.Equal(OmfReasonCode.AwaitingFlush, secondResult.Reason.Code);
    }

    [Fact]
    public async Task Wait_CallerCancellation_ThrowsAndTrackingContinues()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);
        using var cts = new CancellationTokenSource();

        var wait = scope.WaitForAcceptanceAsync(LongWait, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    [Fact]
    public async Task Dispose_CancelsOutstandingWaitsAndLaterWaitsThrow()
    {
        var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);

        var wait = scope.WaitForAcceptanceAsync(LongWait);
        await scope.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => scope.WaitForAcceptanceAsync(TimeSpan.Zero));
        Assert.Throws<ObjectDisposedException>(scope.EnterWrite);
        Assert.Equal(0, _coordinator.ActiveScopeCount);

        _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
    }

    [Fact]
    public async Task Shutdown_CancelsOutstandingWaitsAndBlocksNewScopes()
    {
        await using var scope = CreateScope();
        SimulateWriteAndDispatch(scope, 1, TargetA);

        var wait = scope.WaitForAcceptanceAsync(LongWait);
        _coordinator.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Throws<ObjectDisposedException>(() => _coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
    }

    [Fact]
    public async Task Seal_LaterWritesThrow()
    {
        await using var scope = CreateScope();

        scope.Seal();

        Assert.Throws<InvalidOperationException>(scope.EnterWrite);
    }

    [Fact]
    public async Task Seal_WaitsForInFlightWrites()
    {
        await using var scope = CreateScope();
        scope.EnterWrite();

        var seal = Task.Run(scope.Seal);
        await Task.Delay(100);
        Assert.False(seal.IsCompleted);

        _coordinator.RecordAdmitted(scope, 1);
        scope.ExitWrite();
        await seal.WaitAsync(LongWait);

        Assert.Throws<InvalidOperationException>(scope.EnterWrite);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    [Fact]
    public async Task Completion_ContinuationsDoNotRunOnTheReportingThread()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);
        using var releaseContinuation = new ManualResetEventSlim(false);

        var waiter = Task.Run(async () =>
        {
            var result = await scope.WaitForAcceptanceAsync(LongWait);
            releaseContinuation.Wait(LongWait);
            return result;
        });

        await Task.Delay(50);
        Assert.False(waiter.IsCompleted);
        var report = Task.Run(() => _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null));

        await report.WaitAsync(LongWait);
        releaseContinuation.Set();
        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await waiter).Outcome);
    }

    [Fact]
    public async Task PipelineReports_UnknownScopesAndBodies_AreIgnored()
    {
        var unknownBody = Guid.NewGuid();

        _coordinator.RecordAdmitted(null, 1);
        _coordinator.RegisterBody(unknownBody, new Dictionary<ScopeToken, int> { [new ForeignToken()] = 1 });
        _coordinator.RegisterDeliveries(unknownBody, new[] { TargetA });
        _coordinator.RecordAttempt(unknownBody, EndpointA, HttpStatusCode.ServiceUnavailable, new OmfOutcomeReason(OmfReasonCode.Retrying, string.Empty));
        _coordinator.RecordDisposition(unknownBody, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);

        await using var scope = CreateScope();
        Assert.Equal(OmfAcceptanceOutcome.Filtered, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    [Fact]
    public async Task Tracking_AtActiveScopeLimitDuringOutage_StaysBoundedAndIsReleasedOnDispose()
    {
        const int BodiesPerScope = 50;
        const int AttemptsPerDelivery = 200;
        var scopes = new[] { CreateScope(), CreateScope() };
        var bodies = new List<Guid>();
        foreach (var scope in scopes)
        {
            scope.EnterWrite();
            _coordinator.RecordAdmitted(scope, BodiesPerScope);
            scope.ExitWrite();
            for (var i = 0; i < BodiesPerScope; i++)
            {
                var bodyId = Guid.NewGuid();
                _coordinator.RegisterBody(bodyId, new Dictionary<ScopeToken, int> { [scope] = 1 });
                _coordinator.RegisterDeliveries(bodyId, new[] { TargetA, TargetB });
                bodies.Add(bodyId);
            }

            _coordinator.CloseMaterialization(scope);
        }

        for (var attempt = 1; attempt <= AttemptsPerDelivery; attempt++)
        {
            var reason = new OmfOutcomeReason(OmfReasonCode.Retrying, $"503 Service Unavailable, attempt {attempt}");
            foreach (var bodyId in bodies)
            {
                _coordinator.RecordAttempt(bodyId, EndpointA, HttpStatusCode.ServiceUnavailable, reason);
                _coordinator.RecordAttempt(bodyId, EndpointB, HttpStatusCode.ServiceUnavailable, reason);
            }
        }

        Assert.False(_coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        Assert.Equal(2 * BodiesPerScope, _coordinator.TrackedBodyCount);
        var result = await scopes[0].WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, result.Outcome);
        Assert.Equal(2 * BodiesPerScope, result.Deliveries.Count);
        Assert.All(result.Deliveries, delivery => Assert.EndsWith($"attempt {AttemptsPerDelivery}", delivery.Reason.Message, StringComparison.Ordinal));

        foreach (var scope in scopes)
        {
            await scope.DisposeAsync();
        }

        Assert.Equal(0, _coordinator.TrackedBodyCount);
        Assert.Equal(0, _coordinator.ActiveScopeCount);
    }

    [Fact]
    public async Task Release_BodySharedByTwoScopes_IsKeptUntilBothAreDisposed()
    {
        var first = CreateScope();
        var second = CreateScope();
        _coordinator.RegisterBody(Guid.NewGuid(), new Dictionary<ScopeToken, int> { [first] = 1, [second] = 1 });

        await first.DisposeAsync();
        Assert.Equal(1, _coordinator.TrackedBodyCount);

        await second.DisposeAsync();
        Assert.Equal(0, _coordinator.TrackedBodyCount);
    }

    [Fact]
    public async Task RecordDisposition_FailureWithoutReasonOrPendingState_Throws()
    {
        await using var scope = CreateScope();
        var bodyId = SimulateWriteAndDispatch(scope, 1, TargetA);

        Assert.Throws<ArgumentNullException>(() => _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Rejected, HttpStatusCode.BadRequest, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => _coordinator.RecordDisposition(bodyId, EndpointA, OmfDeliveryState.Pending, null, null));
    }

    /// <summary>Verifies the disabled-tracking reason keeps its public numeric value and identifies the failover stage.</summary>
    [Fact]
    public void AcceptanceTrackingDisabled_HasStableCodeAndFailoverStage()
    {
        Assert.Equal(16, (int)OmfReasonCode.AcceptanceTrackingDisabled);
        Assert.Equal(OmfPipelineStage.Failover, OmfReasonCode.AcceptanceTrackingDisabled.GetStage());
    }

    /// <summary>Verifies Hot immediately finalizes every unfinished pipeline stage, even without sealing or registered bodies.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task SetFailoverMode_AllUnfinishedStages_AreDiscarded(int stage, bool sealedScope)
    {
        foreach (var endpointMode in new[] { OmfAwaitableEndpointMode.AllActiveEndpointsAtDispatch, OmfAwaitableEndpointMode.AnyCompleteEndpoint })
        {
            _coordinator.SetFailoverMode(FailoverMode.Warm);
            await using var scope = CreateScope(new OmfAwaitableScopeOptions { EndpointMode = endpointMode });
            var body = Guid.NewGuid();
            if (stage >= 1)
            {
                scope.EnterWrite();
                _coordinator.RecordAdmitted(scope, 1);
                scope.ExitWrite();
            }

            if (stage >= 2)
            {
                _coordinator.RegisterBody(body, new Dictionary<ScopeToken, int> { [scope] = 1 });
            }

            if (stage >= 3)
            {
                _coordinator.CloseMaterialization(scope);
                _coordinator.RegisterDeliveries(body, new[] { TargetA });
            }

            if (stage >= 4)
            {
                _coordinator.RecordDisposition(body, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
            }

            if (sealedScope)
            {
                scope.Seal();
            }

            var pending = sealedScope ? scope.WaitForAcceptanceAsync(Timeout.InfiniteTimeSpan) : null;
            Assert.Equal(OmfAcceptanceOutcome.TimedOut, _coordinator.GetResult(scope).Outcome);
            _coordinator.SetFailoverMode(FailoverMode.Hot);
            AssertTrackingDisabled(_coordinator.GetResult(scope));
            if (pending is not null)
            {
                AssertTrackingDisabled(await pending.WaitAsync(LongWait));
            }

            foreach (var mode in new[] { FailoverMode.Cold, FailoverMode.Hot, FailoverMode.NotConfigured, FailoverMode.Warm })
            {
                _coordinator.SetFailoverMode(mode);
                _coordinator.CloseMaterialization(scope);
                _coordinator.RecordDisposition(body, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
                AssertTrackingDisabled(await scope.WaitForAcceptanceAsync(TimeSpan.Zero));
            }
        }
    }

    /// <summary>Verifies Hot finalizes all scopes sharing a body and does not wait for an in-flight write to finish.</summary>
    [Fact]
    public async Task SetFailoverMode_SharedBodyAndInFlightWrite_FinalizesEveryScope()
    {
        await using var first = CreateScope();
        await using var second = CreateScope();
        first.EnterWrite();
        second.EnterWrite();
        _coordinator.RecordAdmitted(first, 1);
        _coordinator.RecordAdmitted(second, 1);
        var body = Guid.NewGuid();
        _coordinator.RegisterBody(body, new Dictionary<ScopeToken, int> { [first] = 1, [second] = 1 });
        try
        {
            _coordinator.SetFailoverMode(FailoverMode.Hot);
            AssertTrackingDisabled(_coordinator.GetResult(first));
            AssertTrackingDisabled(_coordinator.GetResult(second));
        }
        finally
        {
            first.ExitWrite();
            second.ExitWrite();
        }

        _coordinator.RegisterDeliveries(body, new[] { TargetA });
        _coordinator.RecordDisposition(body, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
        AssertTrackingDisabled(await first.WaitForAcceptanceAsync(TimeSpan.Zero));
        AssertTrackingDisabled(await second.WaitForAcceptanceAsync(TimeSpan.Zero));
        Assert.Equal(OmfDeliveryState.Accepted, Assert.Single(_coordinator.GetResult(first).Deliveries).State);
    }

    /// <summary>Verifies Hot preserves already completed filtered, rejected, and discarded outcomes and reasons.</summary>
    [Theory]
    [InlineData(OmfAcceptanceOutcome.Filtered)]
    [InlineData(OmfAcceptanceOutcome.Rejected)]
    [InlineData(OmfAcceptanceOutcome.Discarded)]
    public async Task SetFailoverMode_CompletedScope_RemainsUnchanged(OmfAcceptanceOutcome outcome)
    {
        await using var scope = CreateScope();
        if (outcome != OmfAcceptanceOutcome.Filtered)
        {
            var body = SimulateWriteAndDispatch(scope, 1, TargetA);
            var state = outcome == OmfAcceptanceOutcome.Rejected ? OmfDeliveryState.Rejected : OmfDeliveryState.Discarded;
            _coordinator.RecordDisposition(body, EndpointA, state, null, new OmfOutcomeReason(OmfReasonCode.EndpointRemoved, "Original failure."));
        }

        var before = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(outcome, before.Outcome);
        foreach (var mode in new[] { FailoverMode.Hot, FailoverMode.Cold, FailoverMode.Hot, FailoverMode.Warm })
        {
            _coordinator.SetFailoverMode(mode);
            var after = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
            Assert.Equal(before.Outcome, after.Outcome);
            Assert.Same(before.Reason, after.Reason);
            Assert.Equal(before.Deliveries, after.Deliveries);
        }
    }

    /// <summary>Verifies new cold and warm scopes after Hot still observe exact-202 acceptance and terminal non-202 rejection normally.</summary>
    [Theory]
    [InlineData(FailoverMode.Cold, HttpStatusCode.Accepted)]
    [InlineData(FailoverMode.Warm, HttpStatusCode.Accepted)]
    [InlineData(FailoverMode.Cold, HttpStatusCode.OK)]
    [InlineData(FailoverMode.Warm, HttpStatusCode.NoContent)]
    public async Task SetFailoverMode_ReturnFromHot_NewScopesObserveEndpointDisposition(FailoverMode mode, HttpStatusCode status)
    {
        _coordinator.SetFailoverMode(FailoverMode.Hot);
        _coordinator.SetFailoverMode(mode);
        await using var scope = CreateScope();
        var body = SimulateWriteAndDispatch(scope, 1, TargetA);
        var accepted = status == HttpStatusCode.Accepted;
        _coordinator.RecordDisposition(body, EndpointA, accepted ? OmfDeliveryState.Accepted : OmfDeliveryState.Rejected,
            status, accepted ? null : new OmfOutcomeReason(OmfReasonCode.NonAcceptedSuccess, "Only exact 202 establishes acceptance."));

        var result = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(accepted ? OmfAcceptanceOutcome.Accepted : OmfAcceptanceOutcome.Rejected, result.Outcome);
        Assert.Equal(status, Assert.Single(result.Deliveries).LastStatusCode);
    }

    /// <summary>Verifies Hot finalizes dispatched observations unless already completed, without stopping real delivery.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetFailoverMode_DispatchedScope_FirstFinalOutcomeSurvives(bool acceptedBeforeHot)
    {
        await using var dispatchedScope = CreateScope();
        var dispatched = SimulateWriteAndDispatch(dispatchedScope, 1, TargetA);
        dispatchedScope.Seal();
        if (acceptedBeforeHot)
        {
            _coordinator.RecordDisposition(dispatched, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
            Assert.Equal(OmfAcceptanceOutcome.Accepted, (await dispatchedScope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
        }

        var wait = dispatchedScope.WaitForAcceptanceAsync(Timeout.InfiniteTimeSpan);
        _coordinator.SetFailoverMode(FailoverMode.Hot);
        Assert.Throws<NotSupportedException>(() => _coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        var local = await wait.WaitAsync(LongWait);
        Assert.Equal(acceptedBeforeHot ? OmfAcceptanceOutcome.Accepted : OmfAcceptanceOutcome.Discarded, local.Outcome);
        Assert.Equal(EndpointA, Assert.Single(local.Deliveries).EndpointId);
        if (!acceptedBeforeHot)
        {
            AssertTrackingDisabled(local);
            Assert.Equal(OmfDeliveryState.Pending, Assert.Single(local.Deliveries).State);
            _coordinator.RecordDisposition(dispatched, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
        }

        foreach (var mode in new[] { FailoverMode.Cold, FailoverMode.Hot, FailoverMode.Warm, FailoverMode.Hot })
        {
            _coordinator.SetFailoverMode(mode);
            var later = await dispatchedScope.WaitForAcceptanceAsync(TimeSpan.Zero);
            Assert.Equal(local.Outcome, later.Outcome);
            Assert.Same(local.Reason, later.Reason);
            var delivery = Assert.Single(later.Deliveries);
            Assert.Equal(OmfDeliveryState.Accepted, delivery.State);
            Assert.Equal(HttpStatusCode.Accepted, delivery.LastStatusCode);
            Assert.Equal(Receipt, delivery.Receipt);
            if (acceptedBeforeHot)
            {
                Assert.Equal(Assert.Single(local.Deliveries).CompletedAtUtc, delivery.CompletedAtUtc);
            }
        }
    }

    /// <summary>Verifies scopes with no registered bodies are discarded before later writes, sealing, or serialization can mask the failure.</summary>
    [Theory]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    [InlineData(FailoverMode.NotConfigured)]
    public async Task SetFailoverMode_UnsealedScopeWithoutBodies_IsDiscardedImmediately(FailoverMode originalMode)
    {
        _coordinator.SetFailoverMode(originalMode);
        await using var scope = CreateScope();
        _coordinator.SetFailoverMode(FailoverMode.Hot);
        AssertTrackingDisabled(_coordinator.GetResult(scope));

        // Old unsealed tokens still allow physical delivery; they must never regain a successful observation.
        scope.EnterWrite();
        _coordinator.RecordAdmitted(scope, 1);
        scope.ExitWrite();
        var body = Guid.NewGuid();
        _coordinator.RegisterBody(body, new Dictionary<ScopeToken, int> { [scope] = 1 });
        _coordinator.CloseMaterialization(scope);
        AssertTrackingDisabled(await scope.WaitForAcceptanceAsync(Timeout.InfiniteTimeSpan).WaitAsync(LongWait));

        _coordinator.SetFailoverMode(originalMode);
        _coordinator.RegisterDeliveries(body, new[] { TargetA });
        _coordinator.RecordDisposition(body, EndpointA, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null, Receipt);
        AssertTrackingDisabled(await scope.WaitForAcceptanceAsync(TimeSpan.Zero));
    }

    /// <summary>Checks the terminal observation is explicitly discarded at the failover stage, independent of physical delivery diagnostics.</summary>
    private static void AssertTrackingDisabled(OmfAcceptanceResult result)
    {
        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.AcceptanceTrackingDisabled, result.Reason.Code);
        Assert.Equal(OmfPipelineStage.Failover, result.Reason.Stage);
    }

    private OmfAwaitableScopeState CreateScope(OmfAwaitableScopeOptions options = null)
    {
        Assert.True(_coordinator.TryCreateScope(options ?? new OmfAwaitableScopeOptions(), out var scope));
        return scope;
    }

    private Guid SimulateWriteAndDispatch(OmfAwaitableScopeState scope, int itemCount, params OmfDeliveryTarget[] targets)
    {
        scope.EnterWrite();
        _coordinator.RecordAdmitted(scope, itemCount);
        scope.ExitWrite();

        var bodyId = Guid.NewGuid();
        _coordinator.RegisterBody(bodyId, new Dictionary<ScopeToken, int> { [scope] = itemCount });
        _coordinator.CloseMaterialization(scope);
        _coordinator.RegisterDeliveries(bodyId, targets);
        return bodyId;
    }

    private (Guid First, Guid Second) SimulateTwoBodies(OmfAwaitableScopeState scope, OmfDeliveryTarget[] firstTargets, OmfDeliveryTarget[] secondTargets)
    {
        scope.EnterWrite();
        _coordinator.RecordAdmitted(scope, 2);
        scope.ExitWrite();

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _coordinator.RegisterBody(first, new Dictionary<ScopeToken, int> { [scope] = 1 });
        _coordinator.RegisterBody(second, new Dictionary<ScopeToken, int> { [scope] = 1 });
        _coordinator.CloseMaterialization(scope);
        _coordinator.RegisterDeliveries(first, firstTargets);
        _coordinator.RegisterDeliveries(second, secondTargets);
        return (first, second);
    }

    private sealed class ForeignToken : ScopeToken
    {
    }
}
