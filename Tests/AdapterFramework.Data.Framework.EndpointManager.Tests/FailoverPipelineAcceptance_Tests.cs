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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Failover;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

/// <summary>
/// Regression coverage for the real hot-failover buffer, endpoint manager, writer factory,
/// HTTP writer, and acceptance coordinator. Only configuration, logging, and secret storage are mocked.
/// </summary>
public sealed class FailoverPipelineAcceptance_Tests
{
    private static readonly TimeSpan CompletionWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Verifies hot-primary handoff cannot cover either a scoped or an unscoped body while HTTP retries,
    /// and that exact 202 responses advance both the manager watermark and advertised failover progress.
    /// </summary>
    [Fact]
    public async Task LastDataProcessedTime_HotPrimaryRetriesScopedAndUnscopedBodies_AdvancesOnlyAfter202()
    {
        using var pipeline = new PipelineFixture();
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        await using var scope = pipeline.CreateScope();
        var scopedBody = CreateBody("scoped");
        pipeline.RegisterBody(scope, scopedBody);
        var scopedTime = pipeline.Process(scopedBody);

        await pipeline.WaitForRetriesAsync(scopedBody);
        var retrying = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, retrying.Outcome);
        Assert.Equal(OmfReasonCode.Retrying, retrying.Reason.Code);
        var pending = Assert.Single(retrying.Deliveries);
        Assert.Equal(OmfDeliveryState.Pending, pending.State);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.LastStatusCode);
        Assert.Equal(scopedTime.AddTicks(-1), pipeline.Manager.GetAcceptanceWatermark());
        Assert.Equal(scopedTime.AddTicks(-1), pipeline.Processor.LastDataProcessedTime);

        pipeline.AcceptRequests();
        AssertAccepted(await scope.WaitForAcceptanceAsync(CompletionWait), pipeline, scopedBody);
        await pipeline.WaitForEgressAsync(1);
        Assert.Equal(scopedTime, pipeline.Manager.GetAcceptanceWatermark());
        Assert.True(pipeline.Processor.LastDataProcessedTime > scopedTime);

        pipeline.RetryRequests();
        var unscopedBody = CreateBody("unscoped");
        var unscopedTime = pipeline.Process(unscopedBody);
        Assert.Null(unscopedBody.SerializedMessageId);
        Assert.True(unscopedTime > scopedTime);
        await pipeline.WaitForRetriesAsync(unscopedBody);
        Assert.Equal(unscopedTime.AddTicks(-1), pipeline.Manager.GetAcceptanceWatermark());
        Assert.Equal(unscopedTime.AddTicks(-1), pipeline.Processor.LastDataProcessedTime);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await scope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);

        pipeline.AcceptRequests();
        await pipeline.WaitForEgressAsync(1);
        Assert.Equal(unscopedTime, pipeline.Manager.GetAcceptanceWatermark());
        Assert.True(pipeline.Processor.LastDataProcessedTime > unscopedTime);
        Assert.Equal(DateTimeKind.Utc, pipeline.Processor.LastDataProcessedTime.Kind);
    }

    /// <summary>
    /// Verifies terminal non-202 responses never authorize peer trimming, for scoped and unscoped
    /// hot bodies alike, even when a later body receives an exact 202 from the real HTTP writer.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NoContent, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task LastDataProcessedTime_HotPrimaryReceivesNon202_FreezesBeforeBody(HttpStatusCode status, bool scoped)
    {
        using var pipeline = new PipelineFixture();
        pipeline.Endpoint.SetHttpResponse(status);
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        await using var scope = pipeline.CreateScope();
        var body = CreateBody("non202");
        if (scoped)
        {
            pipeline.RegisterBody(scope, body);
        }

        var processTime = pipeline.Process(body);
        await pipeline.WaitForEgressAsync(1);
        Assert.True(pipeline.Endpoint.VerifyMessageReceived(BodyText(body)));
        Assert.Equal(processTime.AddTicks(-1), pipeline.Manager.GetAcceptanceWatermark());
        Assert.Equal(processTime.AddTicks(-1), pipeline.Processor.LastDataProcessedTime);
        if (scoped)
        {
            var rejected = await scope.WaitForAcceptanceAsync(CompletionWait);
            Assert.Equal(OmfAcceptanceOutcome.Rejected, rejected.Outcome);
            Assert.Equal(status == HttpStatusCode.BadRequest ? OmfReasonCode.RejectedByEndpoint : OmfReasonCode.NonAcceptedSuccess, rejected.Reason.Code);
            Assert.Equal(status, Assert.Single(rejected.Deliveries).LastStatusCode);
        }

        pipeline.AcceptRequests();
        await using var laterScope = pipeline.CreateScope();
        var laterBody = CreateBody("later202");
        pipeline.RegisterBody(laterScope, laterBody);
        Assert.True(pipeline.Process(laterBody) > processTime);
        AssertAccepted(await laterScope.WaitForAcceptanceAsync(CompletionWait), pipeline, laterBody);
        await pipeline.WaitForEgressAsync(1);
        Assert.Equal(processTime.AddTicks(-1), pipeline.Manager.GetAcceptanceWatermark());
        Assert.Equal(processTime.AddTicks(-1), pipeline.Processor.LastDataProcessedTime);
    }

    /// <summary>
    /// Verifies a real primary's retry boundary leaves the secondary pending, its accepted watermark
    /// trims exactly the covered body as PeerCovered, and promotion sends only the remaining body
    /// through the secondary's own real HTTP writer and completes it as Accepted.
    /// </summary>
    [Fact]
    public async Task UpdateState_HotSecondaryReceivesPrimaryWatermark_TrimsPeerCoverageAndAcceptsAfterPromotion()
    {
        using var primary = new PipelineFixture();
        using var secondary = new PipelineFixture();
        primary.Processor.UpdateState(FailoverRole.Primary, default);
        await using var primaryScope = primary.CreateScope();
        await using var peerScope = secondary.CreateScope();
        var primaryBody = CreateBody("peer-covered");
        var peerBody = CreateBody("peer-covered");
        primary.RegisterBody(primaryScope, primaryBody);
        secondary.RegisterBody(peerScope, peerBody);
        var secondaryTime = secondary.Process(peerBody);
        var primaryTime = primary.Process(primaryBody);

        // FailoverManager normally adjusts the peer clock. Direct UpdateState callers must supply
        // an already-adjusted timestamp; align these corresponding bodies explicitly in the test.
        var peerClockOffset = secondaryTime - primaryTime;
        await primary.WaitForRetriesAsync(primaryBody);
        var retryBoundary = primary.Processor.LastDataProcessedTime;
        Assert.Equal(primaryTime.AddTicks(-1), retryBoundary);
        secondary.Processor.UpdateState(FailoverRole.Secondary, retryBoundary + peerClockOffset);
        var waiting = await peerScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, waiting.Outcome);
        Assert.Equal(OmfReasonCode.AwaitingPeerCoverage, waiting.Reason.Code);
        var buffered = Assert.Single(waiting.Deliveries);
        Assert.Equal(peerBody.SerializedMessageId, buffered.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.Pending, buffered.State);
        Assert.Null(buffered.EndpointId);
        Assert.Null(buffered.LastStatusCode);
        Assert.Null(buffered.Receipt);
        Assert.False(secondary.Endpoint.VerifyMessageReceived(BodyText(peerBody)));
        Assert.Equal(DateTime.MinValue, secondary.Manager.GetAcceptanceWatermark());
        Assert.Equal(DateTime.MinValue, secondary.Processor.LastDataProcessedTime);

        // Leave a newer body buffered so trimming is also checked not to overrun its boundary.
        await using var promotedScope = secondary.CreateScope();
        var promotedBody = CreateBody("promoted");
        secondary.RegisterBody(promotedScope, promotedBody);
        var promotedTime = secondary.Process(promotedBody);
        Assert.True(promotedTime > secondaryTime);
        primary.AcceptRequests();
        AssertAccepted(await primaryScope.WaitForAcceptanceAsync(CompletionWait), primary, primaryBody);
        await primary.WaitForEgressAsync(1);
        Assert.True(primary.Processor.LastDataProcessedTime > primaryTime);
        // Apply the exact accepted boundary here to test selective trim. The advertised idle
        // watermark advances further, allowing a finite batch to cross the real heartbeat offset.
        secondary.Processor.UpdateState(FailoverRole.Secondary, primary.Manager.GetAcceptanceWatermark() + peerClockOffset);

        var covered = await peerScope.WaitForAcceptanceAsync(CompletionWait);
        Assert.Equal(OmfAcceptanceOutcome.PeerCovered, covered.Outcome);
        Assert.Null(covered.Reason);
        var peerDelivery = Assert.Single(covered.Deliveries);
        Assert.Equal(peerBody.SerializedMessageId, peerDelivery.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.PeerCovered, peerDelivery.State);
        Assert.Null(peerDelivery.EndpointId);
        Assert.Null(peerDelivery.TargetUri);
        Assert.Null(peerDelivery.LastStatusCode);
        Assert.Null(peerDelivery.Receipt);
        Assert.Null(peerDelivery.Reason);
        Assert.NotNull(peerDelivery.CompletedAtUtc);
        var stillBuffered = await promotedScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, stillBuffered.Outcome);
        Assert.Equal(OmfReasonCode.AwaitingPeerCoverage, stillBuffered.Reason.Code);

        secondary.Processor.UpdateState(FailoverRole.Primary, default);
        await secondary.WaitForRetriesAsync(promotedBody);
        var retrying = await promotedScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, retrying.Outcome);
        Assert.Equal(OmfReasonCode.Retrying, retrying.Reason.Code);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, Assert.Single(retrying.Deliveries).LastStatusCode);
        Assert.Equal(promotedTime.AddTicks(-1), secondary.Processor.LastDataProcessedTime);
        secondary.AcceptRequests();
        AssertAccepted(await promotedScope.WaitForAcceptanceAsync(CompletionWait), secondary, promotedBody);
        await secondary.WaitForEgressAsync(1);
        Assert.Equal(promotedTime, secondary.Manager.GetAcceptanceWatermark());
        Assert.True(secondary.Processor.LastDataProcessedTime > promotedTime);

        // Join the failover consumer before checking that the peer-covered body was never sent.
        secondary.Processor.UpdateState(FailoverRole.Secondary, default);
        Assert.Equal(0, secondary.Endpoint.NumberOfTimesMessageReceived(BodyText(peerBody)));
        Assert.Equal(OmfAcceptanceOutcome.PeerCovered, (await peerScope.WaitForAcceptanceAsync(TimeSpan.Zero)).Outcome);
    }

    /// <summary>Verifies legacy hot progress reflects handoff even during real HTTP retries, stays exact while idle, and needs no awaitable scope.</summary>
    [Fact]
    public async Task LastDataProcessedTime_LegacyHotDuringRetries_RemainsExactHandoffBeforeAndAfter202()
    {
        using var pipeline = new PipelineFixture(enableAcceptanceTrimWatermark: false);
        Assert.Throws<NotSupportedException>(() => pipeline.Coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        var body = CreateBody("legacy-retry");

        var handoff = pipeline.Process(body);
        await pipeline.WaitForRetriesAsync(body);

        Assert.Equal(handoff, pipeline.Processor.LastDataProcessedTime);
        Assert.Equal(DateTime.MinValue, pipeline.Manager.GetAcceptanceWatermark());
        pipeline.AcceptRequests();
        await pipeline.WaitForEgressAsync(1);
        Assert.Equal(handoff, pipeline.Processor.LastDataProcessedTime);
        await Task.Delay(25);
        Assert.Equal(handoff, pipeline.Processor.LastDataProcessedTime);
        Assert.Null(body.SerializedMessageId);
        Assert.Equal(DateTimeKind.Utc, pipeline.Processor.LastDataProcessedTime.Kind);
    }

    /// <summary>Verifies real non-202 responses cannot freeze legacy handoff progress or prevent a later unscoped body from being sent.</summary>
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task LastDataProcessedTime_LegacyHotAfterNon202_AdvancesWithNextHandoff(HttpStatusCode status)
    {
        using var pipeline = new PipelineFixture(enableAcceptanceTrimWatermark: false);
        pipeline.Endpoint.SetHttpResponse(status);
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        var rejected = CreateBody("legacy-non202");
        var firstHandoff = pipeline.Process(rejected);
        await pipeline.WaitForEgressAsync(1);

        Assert.True(pipeline.Endpoint.VerifyMessageReceived(BodyText(rejected)));
        Assert.Equal(firstHandoff, pipeline.Processor.LastDataProcessedTime);
        Assert.Equal(DateTime.MinValue, pipeline.Manager.GetAcceptanceWatermark());
        pipeline.AcceptRequests();
        var later = CreateBody("legacy-later202");
        var lastHandoff = pipeline.Process(later);
        await pipeline.WaitForEgressAsync(1);

        Assert.True(lastHandoff > firstHandoff);
        Assert.True(pipeline.Endpoint.VerifyMessageReceived(BodyText(later)));
        Assert.Equal(lastHandoff, pipeline.Processor.LastDataProcessedTime);
        Assert.Null(later.SerializedMessageId);
    }

    /// <summary>Verifies disabling tracking preserves a real retrying writer delivery and queued data while making buffered scope failure irreversible.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateMode_HotTrackingDisabledThenReenabled_PreservesReal202AndLegacyBufferedBehavior(bool trimBuffered)
    {
        using var pipeline = new PipelineFixture();
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        await using var localScope = pipeline.CreateScope();
        var local = CreateBody("dispatched-before-disable");
        pipeline.RegisterBody(localScope, local);
        pipeline.Process(local);
        await pipeline.WaitForRetriesAsync(local);
        pipeline.Processor.UpdateState(FailoverRole.Secondary, default);
        await using var bufferedScope = pipeline.CreateScope();
        var buffered = CreateBody("buffered-before-disable");
        pipeline.RegisterBody(bufferedScope, buffered);
        pipeline.Process(buffered);
        var wait = bufferedScope.WaitForAcceptanceAsync(CompletionWait);
        Assert.False(wait.IsCompleted);

        pipeline.Processor.UpdateMode(FailoverMode.Hot, false);

        var discarded = await wait.WaitAsync(CompletionWait);
        Assert.Equal(OmfAcceptanceOutcome.Discarded, discarded.Outcome);
        Assert.Equal(OmfReasonCode.AcceptanceTrackingDisabled, discarded.Reason.Code);
        var lost = Assert.Single(discarded.Deliveries);
        Assert.Equal(buffered.SerializedMessageId, lost.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.Discarded, lost.State);
        Assert.Equal(OmfReasonCode.AcceptanceTrackingDisabled, lost.Reason.Code);
        Assert.Null(lost.EndpointId);
        Assert.Null(lost.TargetUri);
        Assert.Null(lost.LastStatusCode);
        Assert.Null(lost.Receipt);
        Assert.NotNull(lost.CompletedAtUtc);
        Assert.Throws<NotSupportedException>(() => pipeline.Coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        var pending = await localScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, pending.Outcome);
        Assert.Equal(OmfReasonCode.Retrying, pending.Reason.Code);

        if (trimBuffered)
        {
            pipeline.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        }

        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        pipeline.AcceptRequests();
        AssertAccepted(await localScope.WaitForAcceptanceAsync(CompletionWait), pipeline, local);
        await pipeline.WaitForEgressAsync(trimBuffered ? 1 : 2);
        pipeline.Processor.UpdateState(FailoverRole.Secondary, default);
        Assert.Equal(!trimBuffered, pipeline.Endpoint.VerifyMessageReceived(BodyText(buffered)));

        pipeline.Processor.UpdateMode(FailoverMode.Hot, true);
        pipeline.Processor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow.AddSeconds(1));
        var later = await bufferedScope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.Discarded, later.Outcome);
        Assert.Equal(OmfReasonCode.AcceptanceTrackingDisabled, later.Reason.Code);
        Assert.Equal(OmfDeliveryState.Discarded, Assert.Single(later.Deliveries).State);
        Assert.Equal(lost.CompletedAtUtc, Assert.Single(later.Deliveries).CompletedAtUtc);
        AssertAccepted(await localScope.WaitForAcceptanceAsync(TimeSpan.Zero), pipeline, local);

        await using var newScope = pipeline.CreateScope();
        var newBody = CreateBody("new-opted-in-scope");
        pipeline.RegisterBody(newScope, newBody);
        pipeline.Process(newBody);
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        AssertAccepted(await newScope.WaitForAcceptanceAsync(CompletionWait), pipeline, newBody);
        await pipeline.WaitForEgressAsync(1);
    }

    /// <summary>Creates distinct valid JSON bodies without a scope or a caller-supplied process time.</summary>
    private static SerializedOmfMessage CreateBody(string name) =>
        new(MessageType.Instance, Encoding.UTF8.GetBytes($"{{\"id\":\"{name}\"}}"), MessageAction.Create, 1, OmfVersion.Omf20);

    /// <summary>Returns the exact JSON text observed by the local HTTP endpoint.</summary>
    private static string BodyText(ISerializedOmfMessage body) => Encoding.UTF8.GetString(body.MessageBody);

    /// <summary>Checks that acceptance came from the fixture's HTTP endpoint, not a peer placeholder.</summary>
    private static void AssertAccepted(OmfAcceptanceResult result, PipelineFixture pipeline, ISerializedOmfMessage body)
    {
        Assert.Equal(OmfAcceptanceOutcome.Accepted, result.Outcome);
        Assert.Null(result.Reason);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(body.SerializedMessageId, delivery.SerializedMessageId);
        Assert.Equal(OmfDeliveryState.Accepted, delivery.State);
        Assert.Equal(PipelineFixture.EndpointId, delivery.EndpointId);
        Assert.Equal(pipeline.TargetUri, delivery.TargetUri);
        Assert.Equal(HttpStatusCode.Accepted, delivery.LastStatusCode);
        Assert.Equal(pipeline.Receipt, delivery.Receipt);
        Assert.True(pipeline.Endpoint.VerifyMessageReceived(BodyText(body)));
    }

    private sealed class PipelineFixture : IDisposable
    {
        public const string EndpointId = "pipeline";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), nameof(FailoverPipelineAcceptance_Tests), Guid.NewGuid().ToString("N"));

        /// <summary>Composes an isolated real failover-to-HTTP pipeline initially responding with retryable 503.</summary>
        public PipelineFixture(bool enableAcceptanceTrimWatermark = true)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            TargetUri = new Uri($"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}/api/omf/");
            listener.Stop();
            Endpoint = new TestEndpoint(TargetUri.AbsoluteUri);
            RetryRequests();
            Endpoint.StartListening();

            var configuration = new Mock<IConfigurationProvider>();
            ICollection<string> errors = Array.Empty<string>();
            var buffering = new BufferingConfiguration { EnablePersistentBuffering = false, MaxBufferSizeMB = 20, BufferLocation = _directory };
            var endpoints = new[] { new EndpointConfigurationBase { Id = EndpointId, Endpoint = TargetUri.AbsoluteUri, UserName = "user", Password = "password" } };
            configuration.Setup(provider => provider.TryGetConfiguration(
                EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.BufferingFacetName, out buffering, out errors)).Returns(true);
            configuration.Setup(provider => provider.TryGetConfiguration(
                EdgeSystemConstants.OmfEgressComponentId, EdgeSystemConstants.DataEndpointsFacetName, out endpoints, out errors)).Returns(true);
            configuration.Setup(provider => provider.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns(_directory);
            var logManager = new Mock<ILogManager>();
            logManager.Setup(manager => manager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>())).Returns(NullLogger.Instance);
            var dataProtector = new Mock<IEdgeDataProtector>();
            dataProtector.Setup(protector => protector.Unprotect(It.IsAny<string>())).Returns("password");
            Coordinator.SetOmfVersion(OmfVersion.Omf20);
            var factory = new OmfWriterFactory(dataProtector.Object, new OmfJsonSerializer(), configuration.Object,
                new ApplicationManifest(), awaitableCoordinator: Coordinator);
            Manager = new OmfEndpointManager(configuration.Object, factory, logManager.Object,
                new Mock<IConfigurationProtector>().Object, Coordinator);
            Processor = new FailoverDataMessageProcessor(NullLogger.Instance, configuration.Object, Manager, Coordinator);
            Processor.Initialize(); // Initializes the real manager and creates its writer through the real factory.
            if (enableAcceptanceTrimWatermark)
            {
                Processor.UpdateMode(FailoverMode.Hot, true);
            }
            else
            {
                Processor.UpdateMode(FailoverMode.Hot);
            }
        }

        public Uri TargetUri { get; }
        public TestEndpoint Endpoint { get; }
        public OmfIngressReceipt Receipt { get; } = new(Guid.NewGuid(), 0, TimeSpan.Zero);
        public OmfAwaitableCoordinator Coordinator { get; } = new();
        public OmfEndpointManager Manager { get; }
        public FailoverDataMessageProcessor Processor { get; }

        /// <summary>Creates a scope whose lifetime belongs to the test's await-using declaration.</summary>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The caller asynchronously disposes the scope.")]
        public OmfAwaitableScopeState CreateScope()
        {
            Assert.True(Coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out var scope));
            return scope;
        }

        /// <summary>Registers only materialization; real processor buffering and manager dispatch own all delivery transitions.</summary>
        public void RegisterBody(OmfAwaitableScopeState scope, SerializedOmfMessage body)
        {
            var id = Guid.NewGuid();
            body.SerializedMessageId = id;
            Coordinator.RecordAdmitted(scope, 1);
            Coordinator.RegisterBody(id, new Dictionary<ScopeToken, int> { [scope] = 1 });
            Coordinator.CloseMaterialization(scope);
            scope.Seal();
        }

        /// <summary>Hands off normally and reads the processor-assigned timestamp without changing its clock or queues.</summary>
        public DateTime Process(ISerializedOmfMessage body)
        {
            Processor.ProcessOmfMessage(body);
            return new DateTime((long)TestUtilities.GetFieldValueFromObject("_lastReceivedTimeTicks", Processor), DateTimeKind.Utc);
        }

        /// <summary>Retries at a short bounded interval so tests can switch to 202 without a minute-long delay.</summary>
        public void RetryRequests() => Endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });

        /// <summary>Returns an exact 202 with a receipt, leaving non-202 cases such as 204 free of response bodies.</summary>
        public void AcceptRequests()
        {
            Endpoint.SetPostResponseBody($"{{\"Operation-Id\":\"{Receipt.OperationId}\",\"ErrorCount\":0,\"QueueAge\":\"00:00:00\"}}");
            Endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        }

        /// <summary>Requires a second actual POST, proving the first 503 was processed and the body retained for retry.</summary>
        public async Task WaitForRetriesAsync(ISerializedOmfMessage body)
        {
            var elapsed = Stopwatch.StartNew();
            while (Endpoint.NumberOfTimesMessageReceived(BodyText(body)) < 2 && elapsed.Elapsed < CompletionWait)
            {
                await Task.Delay(25);
            }

            Assert.True(Endpoint.NumberOfTimesMessageReceived(BodyText(body)) >= 2, "The real writer did not retry the body over HTTP.");
        }

        /// <summary>Waits for terminal writer dequeues rather than racing a server callback before response handling finishes.</summary>
        public async Task WaitForEgressAsync(long expected)
        {
            var elapsed = Stopwatch.StartNew();
            var count = 0L;
            while (count < expected && elapsed.Elapsed < CompletionWait)
            {
                foreach (var value in Manager.GetAndResetEgressedValuesCounters().Values)
                {
                    count += value;
                }

                if (count < expected)
                {
                    await Task.Delay(25);
                }
            }

            Assert.Equal(expected, count);
        }

        /// <summary>Joins failover and writer consumers before closing HTTP and releasing coordinator tracking.</summary>
        public void Dispose()
        {
            Processor.Dispose();
            Manager.Dispose();
            Endpoint.Dispose();
            Coordinator.Dispose();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
    }
}