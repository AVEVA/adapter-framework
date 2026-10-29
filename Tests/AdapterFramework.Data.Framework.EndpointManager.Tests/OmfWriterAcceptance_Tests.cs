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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

/// <summary>Verifies ordinary writer scopes and persisted record compatibility against a local HTTP endpoint.</summary>
public sealed class OmfWriterAcceptance_Tests : IDisposable
{
    private const string EndpointId = "Endpoint1";
    private static readonly TimeSpan CompletionWait = TimeSpan.FromSeconds(15);
    private readonly OmfAwaitableCoordinator _coordinator = new();
    private readonly string _uri;
    private readonly TestEndpoint _endpoint;
    private readonly string _bufferPath = Path.Combine(Path.GetTempPath(), "UnitTests", Path.GetRandomFileName());
    private OmfWriter _writer;

    /// <summary>Initializes a coordinator supporting scopes and a local endpoint on a free port.</summary>
    public OmfWriterAcceptance_Tests()
    {
        _coordinator.SetOmfVersion(OmfVersion.Omf20);
        _uri = $"http://localhost:{GetAvailablePort()}/api/omf/";
        _endpoint = new TestEndpoint(_uri);
    }

    /// <summary>An exact 202 accepts the delivery and keeps the endpoint's parsed receipt.</summary>
    [Fact]
    public async Task SendMessage_202_DeliveryAcceptedWithReceipt()
    {
        var operationId = Guid.NewGuid();
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _endpoint.SetPostResponseBody($"{{\"Operation-Id\":\"{operationId}\",\"ErrorCount\":2,\"QueueAge\":\"00:00:30\"}}");
        _endpoint.StartListening();
        var result = await SendScopedBodyAsync(CompletionWait);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, result.Outcome);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(HttpStatusCode.Accepted, delivery.LastStatusCode);
        Assert.Equal(new OmfIngressReceipt(operationId, 2, TimeSpan.FromSeconds(30)), delivery.Receipt);
    }

    /// <summary>Terminal non-202 responses reject only the scoped delivery with the appropriate reason.</summary>
    [Theory]
    [InlineData(HttpStatusCode.OK, OmfReasonCode.NonAcceptedSuccess)]
    [InlineData(HttpStatusCode.NoContent, OmfReasonCode.NonAcceptedSuccess)]
    [InlineData(HttpStatusCode.BadRequest, OmfReasonCode.RejectedByEndpoint)]
    public async Task SendMessage_Non202_DeliveryRejected(HttpStatusCode status, OmfReasonCode reason)
    {
        _endpoint.SetHttpResponse(status);
        _endpoint.StartListening();
        var result = await SendScopedBodyAsync(CompletionWait);
        Assert.Equal(OmfAcceptanceOutcome.Rejected, result.Outcome);
        Assert.Equal(reason, result.Reason.Code);
        Assert.Equal(status, Assert.Single(result.Deliveries).LastStatusCode);
    }

    /// <summary>
    /// Each data body that gets 404 is sent once, requeued, and then dropped on its retry; the drop reports the body's own 404, not a skip.
    /// </summary>
    [Fact]
    public async Task SendMessage_DataBodiesGet404_EachRejectedWithItsOwnStatus()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.NotFound);
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false);
        var (first, firstMessage) = RegisterScopedBody(MessageType.Data);
        var (second, secondMessage) = RegisterScopedBody(MessageType.Data);
        await using (first)
        await using (second)
        {
            _writer.SendMessage(firstMessage);
            _writer.SendMessage(secondMessage);

            foreach (var scope in new[] { first, second })
            {
                var result = await scope.WaitForAcceptanceAsync(CompletionWait);
                Assert.Equal(OmfAcceptanceOutcome.Rejected, result.Outcome);
                Assert.Equal(OmfReasonCode.RejectedByEndpoint, result.Reason.Code);
                Assert.Equal(HttpStatusCode.NotFound, Assert.Single(result.Deliveries).LastStatusCode);
            }
        }
    }

    /// <summary>
    /// A data body the writer skips without sending, because the body at the head of the queue changed after another body got 404,
    /// is discarded as <see cref="OmfReasonCode.SkippedAfterNotFound"/> with no status, instead of taking the other body's 404.
    /// </summary>
    [Fact]
    public async Task SendMessage_DataSkippedAfterAnother404_DiscardedWithoutStatus()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false);

        // The state the writer is in after another body got 404 and was then evicted from the queue before its retry.
        SetWriterField("_shouldRetry404", false);
        SetWriterField("_messageType", MessageType.Data);
        SetWriterField("_cachedResponse", new EndpointResponse(ResponseStatusEnum.NotFound) { StatusCode = HttpStatusCode.NotFound });
        SetWriterField("_cachedResponseBodyId", (Guid?)Guid.NewGuid());

        var (scope, message) = RegisterScopedBody(MessageType.Data);
        await using (scope)
        {
            _writer.SendMessage(message);

            var result = await scope.WaitForAcceptanceAsync(CompletionWait);
            Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
            Assert.Equal(OmfReasonCode.SkippedAfterNotFound, result.Reason.Code);
            Assert.Null(Assert.Single(result.Deliveries).LastStatusCode);
        }
    }

    /// <summary>
    /// A scoped type body the types and containers queue drops for exceeding its memory limit is reported as discarded with
    /// <see cref="OmfReasonCode.BufferFull"/>, instead of leaving the scope to time out.
    /// </summary>
    [Fact]
    public async Task SendMessage_TypeBodyOverMemoryLimit_DeliveryDiscardedAsBufferFull()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false);
        var (scope, message) = RegisterScopedBody(MessageType.Type, new byte[(BufferingConstants.DefaultVolatileMemorySizeMb + 1) * 1024 * 1024]);
        await using (scope)
        {
            _writer.SendMessage(message);

            var result = await scope.WaitForAcceptanceAsync(CompletionWait);
            Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
            Assert.Equal(OmfReasonCode.BufferFull, result.Reason.Code);
        }
    }

    /// <summary>A retryable response leaves the delivery pending and exposes its latest status to a timed-out waiter.</summary>
    [Fact]
    public async Task SendMessage_503_DeliveryPendingWithRetrying()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "60" });
        _endpoint.StartListening();
        var result = await SendScopedBodyAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(OmfAcceptanceOutcome.TimedOut, result.Outcome);
        Assert.Equal(OmfReasonCode.Retrying, result.Reason.Code);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(OmfDeliveryState.Pending, delivery.State);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, delivery.LastStatusCode);
    }

    /// <summary>Bodies moved to disk while retrying keep their IDs and complete their scopes after an exact 202.</summary>
    [Fact]
    public async Task SendMessage_PersistentBuffering_BodiesMovedToDiskAreAccepted()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: true);
        var (retried, retriedMessage) = RegisterScopedBody();
        var (readBack, readBackMessage) = RegisterScopedBody();
        await using (retried)
        await using (readBack)
        {
            _writer.SendMessage(retriedMessage);
            _writer.SendMessage(readBackMessage);
            var dataQueue = (BackedUpOmfMessageQueueBase<ISerializedOmfMessage>)TestUtilities.GetFieldValueFromObject("_dataQueue", _writer);
            Assert.True(SpinWait.SpinUntil(
                () => (long)TestUtilities.GetFieldValueFromObject("_currentVolatileQueueSize", dataQueue) == 0,
                TimeSpan.FromSeconds(30)));
            _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
            Assert.Equal(OmfAcceptanceOutcome.Accepted, (await retried.WaitForAcceptanceAsync(CompletionWait)).Outcome);
            Assert.Equal(OmfAcceptanceOutcome.Accepted, (await readBack.WaitForAcceptanceAsync(CompletionWait)).Outcome);
        }
    }

    /// <summary>Recovered V3/V5 records retain their payload and IDs; later ordinary scopes remain independent of earlier outcomes.</summary>
    [Theory]
    [InlineData(DataItemVersion.V3, false, HttpStatusCode.Accepted)]
    [InlineData(DataItemVersion.V5, true, HttpStatusCode.Accepted)]
    [InlineData(DataItemVersion.V5, true, HttpStatusCode.OK)]
    [InlineData(DataItemVersion.V5, true, HttpStatusCode.NoContent)]
    [InlineData(DataItemVersion.V5, true, HttpStatusCode.BadRequest)]
    public async Task SendMessage_PersistedBacklog_PreservesVersionsAndScopedAcceptance(DataItemVersion version, bool scoped, HttpStatusCode status)
    {
        _endpoint.SetHttpResponse(status);
        _endpoint.StartListening();
        var (scope, message) = RegisterScopedBody();
        await using (scope)
        {
            if (!scoped)
            {
                message.SerializedMessageId = null;
            }

            using (var files = new FileQueue(_bufferPath, "data", EndpointId, 1, 20, new TestLogger()))
            using (var messages = new PersistentOmfMessageQueue(_uri, files, new TestLogger()))
            {
                messages.Enqueue(message);
                Assert.Equal(version, files.Peek().Version);
                Assert.True(messages.TryPeek(out var restored));
                Assert.Equal(message.SerializedMessageId, restored.SerializedMessageId);
                Assert.Equal(message.MessageBody, restored.MessageBody);
            }

            _writer = CreateWriter(persistentBuffering: true);
            await WaitForEgressAsync(1);
            Assert.True(_endpoint.VerifyMessageReceived("{}"));
            if (scoped)
            {
                var result = await scope.WaitForAcceptanceAsync(CompletionWait);
                Assert.Equal(status == HttpStatusCode.Accepted ? OmfAcceptanceOutcome.Accepted : OmfAcceptanceOutcome.Rejected, result.Outcome);
                Assert.Equal(status, Assert.Single(result.Deliveries).LastStatusCode);
            }

            _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
            var (laterScope, laterMessage) = RegisterScopedBody();
            await using (laterScope)
            {
                _writer.SendMessage(laterMessage);
                Assert.Equal(OmfAcceptanceOutcome.Accepted, (await laterScope.WaitForAcceptanceAsync(CompletionWait)).Outcome);
            }
        }
    }

    /// <summary>Waits for the requested number of terminal dequeues, failing rather than silently timing out.</summary>
    private async Task WaitForEgressAsync(long expected)
    {
        var deadline = DateTime.UtcNow + CompletionWait;
        var count = 0L;
        while (count < expected && DateTime.UtcNow < deadline)
        {
            count += _writer.GetAndResetEgressedValuesCounter();
            if (count < expected)
            {
                await Task.Delay(25);
            }
        }

        Assert.Equal(expected, count);
    }

    /// <summary>
    /// Disposes the writer, the endpoint, and the coordinator, and deletes the buffer directory.
    /// </summary>
    public void Dispose()
    {
        _writer?.Dispose();
        _endpoint.Dispose();
        _coordinator.Dispose();
        if (Directory.Exists(_bufferPath))
        {
            Directory.Delete(_bufferPath, true);
        }
    }

    /// <summary>
    /// Gets a free local TCP port.
    /// </summary>
    /// <returns>The port.</returns>
    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Registers a scoped body with one delivery to the writer's endpoint, sends it, and waits for the scope.
    /// </summary>
    /// <param name="wait">How long to wait for the outcome.</param>
    /// <returns>The scope result.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The scope is disposed with await using.")]
    private async Task<OmfAcceptanceResult> SendScopedBodyAsync(TimeSpan wait)
    {
        _writer = CreateWriter(persistentBuffering: false);
        var (scope, message) = RegisterScopedBody();
        await using (scope)
        {
            _writer.SendMessage(message);
            return await scope.WaitForAcceptanceAsync(wait);
        }
    }

    /// <summary>
    /// Registers a sealed, materialized scope with one body that has one delivery to the writer's endpoint.
    /// </summary>
    /// <param name="messageType">The body's message type.</param>
    /// <param name="body">The body bytes, or an empty JSON object when null.</param>
    /// <returns>The scope and its body.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The caller disposes the scope.")]
    private (OmfAwaitableScopeState Scope, SerializedOmfMessage Message) RegisterScopedBody(MessageType messageType = MessageType.Instance, byte[] body = null)
    {
        Assert.True(_coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out var scope));
        var id = Guid.NewGuid();
        _coordinator.RecordAdmitted(scope, 1);
        _coordinator.RegisterBody(id, new Dictionary<ScopeToken, int> { [scope] = 1 });
        _coordinator.CloseMaterialization(scope);
        _coordinator.RegisterDeliveries(id, new[] { new OmfDeliveryTarget(EndpointId, _writer?.TargetUri ?? new Uri(_uri)) });
        scope.Seal();
        return (scope, new SerializedOmfMessage(messageType, body ?? new byte[] { 0x7b, 0x7d }, MessageAction.Create, 1, OmfVersion.Omf20) { SerializedMessageId = id });
    }

    /// <summary>
    /// Sets a private field on the writer under test.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The value.</param>
    private void SetWriterField(string name, object value) =>
        typeof(OmfWriter).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(_writer, value);

    /// <summary>
    /// Creates a data writer that reports to the coordinator.
    /// </summary>
    /// <param name="persistentBuffering">Whether bodies older than the volatile expiration move to disk.</param>
    /// <returns>The writer.</returns>
    private OmfWriter CreateWriter(bool persistentBuffering)
    {
        var dataProtector = new Mock<IEdgeDataProtector>();
        dataProtector.Setup(protector => protector.Unprotect(It.IsAny<string>())).Returns("password");

        return new OmfWriter(
            new EndpointConfigurationBase { Id = EndpointId, Endpoint = _uri, UserName = "user", Password = "password" },
            new TestLogger(),
            new OmfJsonSerializer(),
            null,
            dataProtector.Object,
            new BufferingConfiguration { EnablePersistentBuffering = persistentBuffering, BufferLocation = _bufferPath, MaxBufferSizeMB = 20 },
            new ApplicationManifest(),
            _bufferPath,
            string.Empty,
            OmfWriterType.Data,
            _ => { },
            null,
            _coordinator);
    }
}
