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
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

/// <summary>
/// Writer-side acceptance reporting in <see cref="OmfWriter"/>, against a local HTTP endpoint.
/// </summary>
public sealed class OmfWriterAcceptance_Tests : IDisposable
{
    private const string EndpointId = "Endpoint1";
    private static readonly TimeSpan CompletionWait = TimeSpan.FromSeconds(15);

    private readonly OmfAwaitableCoordinator _coordinator = new();
    private readonly string _uri;
    private readonly TestEndpoint _endpoint;
    private readonly string _bufferPath = Path.Combine(Path.GetTempPath(), "UnitTests", Path.GetRandomFileName());
    private OmfWriter _writer;

    /// <summary>
    /// Initializes a coordinator that allows scopes and a local endpoint on a free port.
    /// </summary>
    public OmfWriterAcceptance_Tests()
    {
        _coordinator.SetOmfVersion(OmfVersion.Omf20);
        _uri = $"http://localhost:{GetAvailablePort()}/api/omf/";
        _endpoint = new TestEndpoint(_uri);
    }

    /// <summary>
    /// An exact 202 accepts the delivery, which completes the scope as accepted and keeps the parsed receipt.
    /// </summary>
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

    /// <summary>
    /// A 2xx other than 202 is dequeued as success but rejects the delivery with <see cref="OmfReasonCode.NonAcceptedSuccess"/>.
    /// </summary>
    [Fact]
    public async Task SendMessage_200_DeliveryRejectedAsNonAcceptedSuccess()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.OK);
        _endpoint.StartListening();

        var result = await SendScopedBodyAsync(CompletionWait);

        Assert.Equal(OmfAcceptanceOutcome.Rejected, result.Outcome);
        Assert.Equal(OmfReasonCode.NonAcceptedSuccess, result.Reason.Code);
        Assert.Equal(HttpStatusCode.OK, Assert.Single(result.Deliveries).LastStatusCode);
    }

    /// <summary>
    /// A terminal error status rejects the delivery with <see cref="OmfReasonCode.RejectedByEndpoint"/>.
    /// </summary>
    [Fact]
    public async Task SendMessage_400_DeliveryRejectedByEndpoint()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.BadRequest);
        _endpoint.StartListening();

        var result = await SendScopedBodyAsync(CompletionWait);

        Assert.Equal(OmfAcceptanceOutcome.Rejected, result.Outcome);
        Assert.Equal(OmfReasonCode.RejectedByEndpoint, result.Reason.Code);
        Assert.Equal(HttpStatusCode.BadRequest, Assert.Single(result.Deliveries).LastStatusCode);
    }

    /// <summary>
    /// A retryable status leaves the delivery pending, with <see cref="OmfReasonCode.Retrying"/> and the last status visible to a timed-out wait.
    /// </summary>
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

    /// <summary>
    /// With persistent buffering, a body retried while it moves to disk and a body read back from disk both keep their IDs,
    /// so both scopes complete as accepted once the endpoint returns 202.
    /// </summary>
    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The scopes are disposed with await using.")]
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

            // The volatile tier moves bodies older than 5 s to disk every 2.5 s.
            await Task.Delay(TimeSpan.FromSeconds(9));
            var dataQueue = (BackedUpOmfMessageQueueBase<ISerializedOmfMessage>)TestUtilities.GetFieldValueFromObject("_dataQueue", _writer);
            Assert.Equal(0L, (long)TestUtilities.GetFieldValueFromObject("_currentVolatileQueueSize", dataQueue));
            _endpoint.SetHttpResponse(HttpStatusCode.Accepted);

            Assert.Equal(OmfAcceptanceOutcome.Accepted, (await retried.WaitForAcceptanceAsync(CompletionWait)).Outcome);
            Assert.Equal(OmfAcceptanceOutcome.Accepted, (await readBack.WaitForAcceptanceAsync(CompletionWait)).Outcome);
        }
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
    /// <returns>The scope and its body.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The caller disposes the scope.")]
    private (OmfAwaitableScopeState Scope, SerializedOmfMessage Message) RegisterScopedBody()
    {
        Assert.True(_coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out var scope));
        var id = Guid.NewGuid();
        _coordinator.RecordAdmitted(scope, 1);
        _coordinator.RegisterBody(id, new Dictionary<ScopeToken, int> { [scope] = 1 });
        _coordinator.CloseMaterialization(scope);
        _coordinator.RegisterDeliveries(id, new[] { new OmfDeliveryTarget(EndpointId, _writer.TargetUri) });
        scope.Seal();
        return (scope, new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x7b, 0x7d }, MessageAction.Create, 1, OmfVersion.Omf20) { SerializedMessageId = id });
    }

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
            new BufferingConfiguration { EnablePersistentBuffering = persistentBuffering, BufferLocation = _bufferPath },
            new ApplicationManifest(),
            _bufferPath,
            string.Empty,
            OmfWriterType.Data,
            _ => { },
            null,
            _coordinator);
    }
}
