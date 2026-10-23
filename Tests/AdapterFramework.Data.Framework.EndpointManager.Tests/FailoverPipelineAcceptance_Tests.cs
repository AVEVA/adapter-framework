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
/// Regression coverage for ordinary scopes and legacy unscoped hot handoff through the real
/// endpoint manager, writer factory, HTTP writer, and acceptance coordinator.
/// </summary>
public sealed class FailoverPipelineAcceptance_Tests
{
    private static readonly TimeSpan CompletionWait = TimeSpan.FromSeconds(30);

    /// <summary>Non-hot scopes remain pending through real HTTP retries and complete only after an exact 202.</summary>
    [Theory]
    [InlineData(FailoverMode.NotConfigured)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public async Task ProcessOmfMessage_NonHotScopedBody_AcceptedAfterRetry(FailoverMode mode)
    {
        using var pipeline = new PipelineFixture(mode);
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        await using var scope = pipeline.CreateScope();
        var body = CreateBody("scoped-retry");
        pipeline.RegisterBody(scope, body);
        pipeline.Process(body);

        await pipeline.WaitForRetriesAsync(body);
        var retrying = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, retrying.Outcome);
        Assert.Equal(OmfReasonCode.Retrying, retrying.Reason.Code);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, Assert.Single(retrying.Deliveries).LastStatusCode);

        pipeline.AcceptRequests();
        AssertAccepted(await scope.WaitForAcceptanceAsync(CompletionWait), pipeline, body);
        await pipeline.WaitForEgressAsync(1);
    }

    /// <summary>Hot progress reflects handoff even during real HTTP retries and stays exact after acceptance, without a scope.</summary>
    [Fact]
    public async Task LastDataProcessedTime_HotDuringRetries_RemainsExactHandoffBeforeAndAfter202()
    {
        using var pipeline = new PipelineFixture(FailoverMode.Hot);
        Assert.Throws<NotSupportedException>(() => pipeline.Coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out _));
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        var body = CreateBody("legacy-retry");

        var handoff = pipeline.Process(body);
        await pipeline.WaitForRetriesAsync(body);

        Assert.Equal(handoff, pipeline.Processor.LastDataProcessedTime);
        pipeline.AcceptRequests();
        await pipeline.WaitForEgressAsync(1);
        Assert.Equal(handoff, pipeline.Processor.LastDataProcessedTime);
        await Task.Delay(25);
        Assert.Equal(handoff, pipeline.Processor.LastDataProcessedTime);
        Assert.Null(body.SerializedMessageId);
        Assert.Equal(DateTimeKind.Utc, pipeline.Processor.LastDataProcessedTime.Kind);
    }

    /// <summary>Non-202 responses cannot freeze legacy hot handoff progress or prevent a later unscoped body from being sent.</summary>
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task LastDataProcessedTime_HotAfterNon202_AdvancesWithNextHandoff(HttpStatusCode status)
    {
        using var pipeline = new PipelineFixture(FailoverMode.Hot);
        pipeline.Endpoint.SetHttpResponse(status);
        pipeline.Processor.UpdateState(FailoverRole.Primary, default);
        var rejected = CreateBody("legacy-non202");
        var firstHandoff = pipeline.Process(rejected);
        await pipeline.WaitForEgressAsync(1);

        Assert.True(pipeline.Endpoint.VerifyMessageReceived(BodyText(rejected)));
        Assert.Equal(firstHandoff, pipeline.Processor.LastDataProcessedTime);
        pipeline.AcceptRequests();
        var later = CreateBody("legacy-later202");
        var lastHandoff = pipeline.Process(later);
        await pipeline.WaitForEgressAsync(1);

        Assert.True(lastHandoff > firstHandoff);
        Assert.True(pipeline.Endpoint.VerifyMessageReceived(BodyText(later)));
        Assert.Equal(lastHandoff, pipeline.Processor.LastDataProcessedTime);
        Assert.Null(later.SerializedMessageId);
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
        public PipelineFixture(FailoverMode mode)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            TargetUri = new Uri($"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}/api/omf/");
            listener.Stop();
            Endpoint = new TestEndpoint(TargetUri.AbsoluteUri);
            Endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
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
            Processor.Initialize();
            Processor.UpdateMode(mode);
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

        /// <summary>Returns an exact 202 with a receipt.</summary>
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




