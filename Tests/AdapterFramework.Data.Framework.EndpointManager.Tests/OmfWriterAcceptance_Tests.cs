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
using System.Threading;
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
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
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

    /// <summary>Timestamped unscoped bodies advance only for exact 202, independent of the coordinator.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Accepted, true)]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.NoContent, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task GetAcceptanceWatermark_UnscopedBody_RequiresExact202(HttpStatusCode status, bool accepted)
    {
        _endpoint.SetHttpResponse(status);
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false);
        _writer.SendMessage(UnscopedBody(100));
        await WaitForEgressAsync(1);
        Assert.Equal(accepted ? 100 : 99, _writer.GetAcceptanceWatermark().Ticks);

        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _writer.SendMessage(UnscopedBody(200));
        await WaitForEgressAsync(1);
        Assert.Equal(accepted ? 200 : 99, _writer.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>Retryable responses hold the pending timestamp until a later exact 202 accepts it.</summary>
    [Fact]
    public async Task GetAcceptanceWatermark_RetryThen202_AdvancesOnlyAfterAcceptance()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false);
        _writer.SendMessage(UnscopedBody(100));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(99, _writer.GetAcceptanceWatermark().Ticks);
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        await WaitForEgressAsync(1);
        Assert.Equal(100, _writer.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>Ordinary non-hot writes block only while pending; switching to timestamped hot writes restores coverage after exact 202.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Accepted, 200)]
    [InlineData(HttpStatusCode.OK, 0)]
    [InlineData(HttpStatusCode.BadRequest, 0)]
    public async Task GetAcceptanceWatermark_UnknownThenKnownTimestamp_ResumesOnlyAfterExact202(HttpStatusCode status, long expected)
    {
        _endpoint.SetHttpResponse(status);
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false);
        _writer.SendMessage(UnscopedBody(0));
        await WaitForEgressAsync(1);
        Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _writer.SendMessage(UnscopedBody(200));
        await WaitForEgressAsync(1);
        Assert.Equal(expected, _writer.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>Priority metadata and data both participate even when neither body carries a scoped ID.</summary>
    [Fact]
    public async Task GetAcceptanceWatermark_MetadataOvertakesData_DoesNotCoverPendingData()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false);
        _writer.SendMessage(UnscopedBody(100, MessageType.Type));
        await Task.Delay(TimeSpan.FromSeconds(1));
        _writer.SendMessage(UnscopedBody(200));
        _writer.SendMessage(UnscopedBody(300, MessageType.Container));
        Assert.Equal(99, _writer.GetAcceptanceWatermark().Ticks);
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        await WaitForEgressAsync(3);
        Assert.Equal(300, _writer.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>Volatile overflow of an unscoped body freezes and clears acceptance tracking.</summary>
    [Fact]
    public async Task GetAcceptanceWatermark_UnscopedVolatileLoss_FreezesBeforeLostBody()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: false, maxBufferSizeMB: 1);
        _writer.SendMessage(new SerializedOmfMessage(MessageType.Instance, new byte[2 * 1024 * 1024], MessageAction.Create, 1, OmfVersion.Omf20) { ProcessTimeTicks = 100 });
        _writer.SendMessage(UnscopedBody(200));
        await WaitForEgressAsync(1);
        Assert.Equal(99, _writer.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>Both recovered queues, including legacy unknown records, catch up automatically without a second restart.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task GetAcceptanceWatermark_RestartWithDiskBacklog_ResumesAfterDrain(long ticks)
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: true);
        _writer.SendMessage(UnscopedBody(ticks));
        _writer.SendMessage(UnscopedBody(200, MessageType.Type));
        _writer.Dispose(); // Shutdown consolidates both volatile tiers to disk.
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _writer = CreateWriter(persistentBuffering: true);
        await WaitForEgressAsync(2);
        Assert.Equal(200, _writer.GetAcceptanceWatermark().Ticks);
        _writer.SendMessage(UnscopedBody(300));
        await WaitForEgressAsync(1);
        Assert.Equal(300, _writer.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>New metadata and data cannot bypass recovery, even if recovered and new records share process times.</summary>
    [Fact]
    public async Task GetAcceptanceWatermark_RecoveryWithConcurrentAdmissions_WaitsForBothQueuesAndInFlightBody()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: true);
        _writer.SendMessage(UnscopedBody(100, MessageType.Type));
        _writer.SendMessage(UnscopedBody(500));
        _writer.Dispose();

        var firstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseLast = new ManualResetEventSlim();
        var requests = 0;
        _endpoint.SetMessageReceivedAction(() =>
        {
            var request = Interlocked.Increment(ref requests);
            if (request == 1)
            {
                firstRequest.TrySetResult();
                releaseFirst.Wait(CompletionWait);
            }
            else if (request == 4)
            {
                lastRequest.TrySetResult();
                releaseLast.Wait(CompletionWait);
            }
        });
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _writer = CreateWriter(persistentBuffering: true);
        try
        {
            await firstRequest.Task.WaitAsync(CompletionWait);
            _writer.SendMessage(UnscopedBody(300, MessageType.Container));
            _writer.SendMessage(UnscopedBody(500));
            Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
            releaseFirst.Set();
            await lastRequest.Task.WaitAsync(CompletionWait);
            Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
            releaseLast.Set();
            await WaitForEgressAsync(4);
            Assert.Equal(500, _writer.GetAcceptanceWatermark().Ticks);
            _writer.SendMessage(UnscopedBody(600));
            await WaitForEgressAsync(1);
            Assert.Equal(600, _writer.GetAcceptanceWatermark().Ticks);
        }
        finally
        {
            releaseFirst.Set();
            releaseLast.Set();
        }
    }

    /// <summary>A terminal or non-202 response in recovered backlog permanently blocks coverage even after all queues drain.</summary>
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task GetAcceptanceWatermark_RecoveryRejectsBody_RemainsFrozen(HttpStatusCode status)
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: true);
        _writer.SendMessage(UnscopedBody(100));
        _writer.Dispose();
        _endpoint.SetHttpResponse(status);
        _writer = CreateWriter(persistentBuffering: true);
        await WaitForEgressAsync(1);
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _writer.SendMessage(UnscopedBody(200));
        await WaitForEgressAsync(1);
        Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
    }

    /// <summary>Corrupt recovered data is a permanent loss even when all remaining metadata and data are accepted and both queues drain.</summary>
    [Fact]
    public async Task GetAcceptanceWatermark_RecoveryLosesData_RemainsFrozenAfterDrain()
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: true);
        _writer.SendMessage(UnscopedBody(100, MessageType.Type));
        _writer.SendMessage(UnscopedBody(200));
        _writer.SendMessage(UnscopedBody(201));
        _writer.Dispose();
        var dataFile = Assert.Single(Directory.GetFiles(_bufferPath, "data_data0", SearchOption.AllDirectories));
        using (var stream = File.Open(dataFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            stream.Position = 12;
            stream.WriteByte(0xff);
        }

        _writer = CreateWriter(persistentBuffering: true);
        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        await WaitForEgressAsync(2);
        _writer.SendMessage(UnscopedBody(300));
        await WaitForEgressAsync(1);
        Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
    }

    /// <summary>Startup eviction in either disk queue freezes coverage before any surviving records can be accepted.</summary>
    [Theory]
    [InlineData("data", MessageType.Instance, 3, 1)]
    [InlineData("metadata", MessageType.Type, 4, 3)]
    public async Task GetAcceptanceWatermark_StartupEvictsFiles_RemainsZeroAfterDrain(string prefix, MessageType type, int records, int survivors)
    {
        // Use real writer records with small queue files, not oversized HTTP bodies or a mocked repair.
        using (var files = new FileQueue(_bufferPath, prefix, maxFileSizeMb: 1, maxQueueFiles: records))
        using (var messages = new PersistentOmfMessageQueue(_uri, files, new TestLogger()))
        {
            var body = new byte[600 * 1024];
            Array.Fill(body, (byte)' ');
            body[^2] = (byte)'{';
            body[^1] = (byte)'}';
            for (var i = 0; i < records; i++)
            {
                messages.Enqueue(new SerializedOmfMessage(type, body, MessageAction.Create, 1, OmfVersion.Omf20) { ProcessTimeTicks = 100 + i });
            }
        }

        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: true);
        await WaitForEgressAsync(survivors);
        Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
        _writer.SendMessage(UnscopedBody(300));
        await WaitForEgressAsync(1);
        Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
    }

    /// <summary>Removing an entire queue before startup cannot be hidden by repair resetting it to an empty queue.</summary>
    [Theory]
    [InlineData("data")]
    [InlineData("metadata")]
    public async Task GetAcceptanceWatermark_StartupMissingAllQueueFiles_RemainsZeroAfterDrain(string prefix)
    {
        _endpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { ["Retry-After"] = "1" });
        _endpoint.StartListening();
        _writer = CreateWriter(persistentBuffering: true);
        _writer.SendMessage(UnscopedBody(100, MessageType.Type));
        _writer.SendMessage(UnscopedBody(200));
        _writer.Dispose();
        var file = Assert.Single(Directory.GetFiles(_bufferPath, $"{prefix}_data0", SearchOption.AllDirectories));
        File.Delete(file);

        _endpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _writer = CreateWriter(persistentBuffering: true);
        await WaitForEgressAsync(1);
        _writer.SendMessage(UnscopedBody(300));
        await WaitForEgressAsync(1);
        Assert.Equal(DateTime.MinValue, _writer.GetAcceptanceWatermark());
    }

    /// <summary>Creates a timestamped hot body without allocating a serialized tracking ID.</summary>
    private static SerializedOmfMessage UnscopedBody(long ticks, MessageType type = MessageType.Instance) =>
        new(type, new byte[] { 0x7b, 0x7d }, MessageAction.Create, 1, OmfVersion.Omf20) { ProcessTimeTicks = ticks };

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
    private OmfWriter CreateWriter(bool persistentBuffering, int maxBufferSizeMB = 20)
    {
        var dataProtector = new Mock<IEdgeDataProtector>();
        dataProtector.Setup(protector => protector.Unprotect(It.IsAny<string>())).Returns("password");

        return new OmfWriter(
            new EndpointConfigurationBase { Id = EndpointId, Endpoint = _uri, UserName = "user", Password = "password" },
            new TestLogger(),
            new OmfJsonSerializer(),
            null,
            dataProtector.Object,
            new BufferingConfiguration { EnablePersistentBuffering = persistentBuffering, BufferLocation = _bufferPath, MaxBufferSizeMB = maxBufferSizeMB },
            new ApplicationManifest(),
            _bufferPath,
            string.Empty,
            OmfWriterType.Data,
            _ => { },
            null,
            _coordinator);
    }
}
