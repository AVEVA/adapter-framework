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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

/// <summary>
/// Dispatch-side acceptance reporting in <see cref="OmfEndpointManager"/>, with test-double writers.
/// </summary>
public sealed class OmfEndpointManagerAcceptance_Tests : IDisposable
{
    private static readonly TimeSpan NoWait = TimeSpan.Zero;

    private readonly OmfAwaitableCoordinator _coordinator = new();
    private readonly Dictionary<string, Mock<IOmfWriter>> _writers = new();
    private EndpointConfigurationBase[] _endpoints;

    /// <summary>
    /// Initializes a coordinator that allows scopes and two configured endpoints.
    /// </summary>
    public OmfEndpointManagerAcceptance_Tests()
    {
        _coordinator.SetOmfVersion(OmfVersion.Omf20);
        _endpoints = new[] { Endpoint("A"), Endpoint("B") };
    }

    /// <summary>
    /// A scoped body is registered with one pending delivery per writer, carrying the writer's endpoint ID and target URI.
    /// </summary>
    [Fact]
    public async Task SendMessage_ScopedBody_RegistersOneDeliveryPerWriter()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        var (scope, message) = CreateScopedBody();
        await using (scope)
        {
            manager.SendMessage(message);

            var result = await scope.WaitForAcceptanceAsync(NoWait);
            Assert.Equal(OmfAcceptanceOutcome.TimedOut, result.Outcome);
            Assert.Equal(new[] { "A", "B" }, result.Deliveries.Select(delivery => delivery.EndpointId).Order());
            Assert.All(result.Deliveries, delivery => Assert.Equal(OmfDeliveryState.Pending, delivery.State));
            Assert.Equal(new Uri("https://a.example.com/omf"), result.Deliveries.Single(delivery => delivery.EndpointId == "A").TargetUri);
            _writers["A"].Verify(writer => writer.SendMessage(message), Times.Once);
            _writers["B"].Verify(writer => writer.SendMessage(message), Times.Once);
        }
    }

    /// <summary>
    /// A writer that throws while accepting a scoped body discards its delivery with <see cref="OmfReasonCode.EnqueueFailed"/>, which completes the scope.
    /// </summary>
    [Fact]
    public async Task SendMessage_WriterThrows_DeliveryDiscardedAsEnqueueFailed()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        _writers["B"].Setup(writer => writer.SendMessage(It.IsAny<ISerializedOmfMessage>())).Throws(new InvalidOperationException("Queue is closed."));
        var (scope, message) = CreateScopedBody();
        await using (scope)
        {
            manager.SendMessage(message);

            var result = await scope.WaitForAcceptanceAsync(NoWait);
            Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
            Assert.Equal(OmfReasonCode.EnqueueFailed, result.Reason.Code);
            Assert.Equal(OmfDeliveryState.Discarded, result.Deliveries.Single(delivery => delivery.EndpointId == "B").State);
        }
    }

    /// <summary>
    /// With no writers, a scoped body is discarded with <see cref="OmfReasonCode.NoEndpoints"/>.
    /// </summary>
    [Fact]
    public async Task SendMessage_NoWriters_BodyDiscardedAsNoEndpoints()
    {
        _endpoints = Array.Empty<EndpointConfigurationBase>();
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        var (scope, message) = CreateScopedBody();
        await using (scope)
        {
            manager.SendMessage(message);

            var result = await scope.WaitForAcceptanceAsync(NoWait);
            Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
            Assert.Equal(OmfReasonCode.NoEndpoints, result.Reason.Code);
        }
    }

    /// <summary>
    /// Removing an endpoint by configuration discards its pending deliveries with <see cref="OmfReasonCode.EndpointRemoved"/>.
    /// </summary>
    [Fact]
    public async Task AddRemoveEndpoints_EndpointRemoved_PendingDeliveriesDiscarded()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        var (scope, message) = CreateScopedBody();
        await using (scope)
        {
            manager.SendMessage(message);

            var change = new ConfigurationChangedEventArgs(_endpoints, new[] { Endpoint("A") });
            manager.AddRemoveEndpoints(change, OmfWriterType.Data);

            var result = await scope.WaitForAcceptanceAsync(NoWait);
            Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
            Assert.Equal(OmfReasonCode.EndpointRemoved, result.Reason.Code);
            Assert.Equal(OmfDeliveryState.Pending, result.Deliveries.Single(delivery => delivery.EndpointId == "A").State);
        }
    }

    /// <summary>
    /// Resetting the data buffers discards every pending delivery with <see cref="OmfReasonCode.BuffersReset"/>.
    /// </summary>
    [Fact]
    public async Task ResetDataBuffersAsync_PendingDeliveriesDiscardedAsBuffersReset()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        var (scope, message) = CreateScopedBody();
        await using (scope)
        {
            manager.SendMessage(message);

            await manager.ResetDataBuffersAsync();

            var result = await scope.WaitForAcceptanceAsync(NoWait);
            Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
            Assert.Equal(OmfReasonCode.BuffersReset, result.Reason.Code);
            Assert.All(result.Deliveries, delivery => Assert.Equal(OmfDeliveryState.Discarded, delivery.State));
        }
    }

    /// <summary>
    /// A health endpoint manager ignores the coordinator, so a body with an ID gets no deliveries.
    /// </summary>
    [Fact]
    public async Task SendMessage_HealthFacet_RegistersNothing()
    {
        using var manager = CreateManager(EdgeSystemConstants.HealthEndpointsFacetName);
        var (scope, message) = CreateScopedBody();
        await using (scope)
        {
            manager.SendMessage(message);

            var result = await scope.WaitForAcceptanceAsync(NoWait);
            Assert.Empty(result.Deliveries);
            Assert.Equal(OmfReasonCode.Queued, result.Reason.Code);
        }
    }

    /// <summary>The aggregate is the minimum of every writer, including unsupported writers returning the minimum.</summary>
    [Fact]
    public void GetAcceptanceWatermark_AllWriters_UsesMinimum()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        _writers["A"].Setup(writer => writer.GetAcceptanceWatermark()).Returns(new DateTime(200));
        _writers["B"].Setup(writer => writer.GetAcceptanceWatermark()).Returns(new DateTime(100));
        Assert.Equal(100, manager.GetAcceptanceWatermark().Ticks);
        _writers["B"].Setup(writer => writer.GetAcceptanceWatermark()).Returns(DateTime.MinValue);
        Assert.Equal(DateTime.MinValue, manager.GetAcceptanceWatermark());
    }

    /// <summary>Health managers cannot authorize data trimming even when their writers expose coverage.</summary>
    [Fact]
    public void GetAcceptanceWatermark_HealthManager_ReturnsMinimum()
    {
        using var manager = CreateManager(EdgeSystemConstants.HealthEndpointsFacetName);
        foreach (var writer in _writers.Values)
        {
            writer.Setup(w => w.GetAcceptanceWatermark()).Returns(new DateTime(200));
        }

        Assert.Equal(DateTime.MinValue, manager.GetAcceptanceWatermark());
    }

    /// <summary>Removing a lagging endpoint must never let the remaining fast endpoint cover its lost bodies.</summary>
    [Fact]
    public void GetAcceptanceWatermark_RemovedEndpoint_FreezesAggregate()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        _writers["A"].Setup(writer => writer.GetAcceptanceWatermark()).Returns(new DateTime(200));
        _writers["B"].Setup(writer => writer.GetAcceptanceWatermark()).Returns(new DateTime(99));
        manager.AddRemoveEndpoints(new ConfigurationChangedEventArgs(_endpoints, new[] { Endpoint("A") }), OmfWriterType.Data);
        Assert.Equal(99, manager.GetAcceptanceWatermark().Ticks);
        _writers["A"].Setup(writer => writer.GetAcceptanceWatermark()).Returns(new DateTime(300));
        Assert.Equal(99, manager.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>Resetting buffers and replacing writers preserves the pre-reset aggregate cap.</summary>
    [Fact]
    public async Task GetAcceptanceWatermark_ResetAndReplaceWriters_PreservesCap()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        foreach (var writer in _writers.Values)
        {
            writer.Setup(w => w.GetAcceptanceWatermark()).Returns(new DateTime(99));
        }

        await manager.ResetDataBuffersAsync();
        foreach (var writer in _writers.Values)
        {
            writer.Setup(w => w.GetAcceptanceWatermark()).Returns(new DateTime(300));
        }

        Assert.Equal(99, manager.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>Unscoped dispatch failure freezes at the snapshot before any writer can advance.</summary>
    [Fact]
    public void GetAcceptanceWatermark_UnscopedEnqueueFailure_FreezesBeforeDispatch()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        var ticks = 100L;
        foreach (var writer in _writers.Values)
        {
            writer.Setup(w => w.GetAcceptanceWatermark()).Returns(() => new DateTime(ticks));
        }

        _writers["A"].Setup(writer => writer.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback(() => ticks = 200);
        _writers["B"].Setup(writer => writer.SendMessage(It.IsAny<ISerializedOmfMessage>())).Throws(new InvalidOperationException("Queue closed."));
        manager.SendMessage(new SerializedOmfMessage(MessageType.Instance, new byte[] { 1 }, MessageAction.Create, 1, OmfVersion.Omf20) { ProcessTimeTicks = 200 });
        Assert.Equal(100, manager.GetAcceptanceWatermark().Ticks);
    }

    /// <summary>A dispatch with no endpoints permanently blocks later endpoints from claiming the missing body.</summary>
    [Fact]
    public void GetAcceptanceWatermark_NoWritersThenAdd_RemainsBlockedAfterLostDispatch()
    {
        _endpoints = Array.Empty<EndpointConfigurationBase>();
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        Assert.Equal(DateTime.MinValue, manager.GetAcceptanceWatermark());
        manager.SendMessage(new SerializedOmfMessage(MessageType.Instance, new byte[] { 1 }, MessageAction.Create, 1, OmfVersion.Omf20) { ProcessTimeTicks = 100 });
        manager.AddRemoveEndpoints(new ConfigurationChangedEventArgs(_endpoints, new[] { Endpoint("A") }), OmfWriterType.Data);
        _writers["A"].Setup(writer => writer.GetAcceptanceWatermark()).Returns(new DateTime(200));
        Assert.Equal(DateTime.MinValue, manager.GetAcceptanceWatermark());
    }

    /// <summary>A watermark query cannot observe the middle of a dispatch to a multi-writer snapshot.</summary>
    [Fact]
    public async Task GetAcceptanceWatermark_ConcurrentDispatch_QueriesAfterAllEnqueues()
    {
        using var manager = CreateManager(EdgeSystemConstants.DataEndpointsFacetName);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticks = 100L;
        foreach (var writer in _writers.Values)
        {
            writer.Setup(w => w.GetAcceptanceWatermark()).Returns(() => new DateTime(ticks));
        }

        _writers["A"].Setup(writer => writer.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback(() =>
        {
            entered.SetResult();
            release.Task.GetAwaiter().GetResult();
        });
        _writers["B"].Setup(writer => writer.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback(() => ticks = 200);
        var send = Task.Run(() => manager.SendMessage(new SerializedOmfMessage(MessageType.Instance, new byte[] { 1 }, MessageAction.Create, 1, OmfVersion.Omf20) { ProcessTimeTicks = 200 }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var query = Task.Run(() =>
        {
            queryStarted.SetResult();
            return manager.GetAcceptanceWatermark();
        });
        try
        {
            await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(100);
            Assert.False(query.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
        }

        await send.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(200, (await query.WaitAsync(TimeSpan.FromSeconds(10))).Ticks);
    }

    /// <summary>
    /// Disposes the coordinator.
    /// </summary>
    public void Dispose() => _coordinator.Dispose();

    /// <summary>
    /// Creates an endpoint configuration whose writer the test double factory returns.
    /// </summary>
    /// <param name="id">The endpoint ID.</param>
    /// <returns>The configuration.</returns>
    private static EndpointConfigurationBase Endpoint(string id) =>
        new() { Id = id, Endpoint = $"https://{id}.example.com/omf" };

    /// <summary>
    /// Creates a sealed and materialized scope with one admitted item carried by one registered body.
    /// </summary>
    /// <returns>The scope and the body.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The caller disposes the scope.")]
    private (OmfAwaitableScopeState Scope, SerializedOmfMessage Message) CreateScopedBody()
    {
        Assert.True(_coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out var scope));
        var id = Guid.NewGuid();
        _coordinator.RecordAdmitted(scope, 1);
        _coordinator.RegisterBody(id, new Dictionary<ScopeToken, int> { [scope] = 1 });
        _coordinator.CloseMaterialization(scope);
        scope.Seal();
        var message = new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x7b, 0x7d }, MessageAction.Create, 1, OmfVersion.Omf20) { SerializedMessageId = id };
        return (scope, message);
    }

    /// <summary>
    /// Creates and initializes an endpoint manager whose factory returns test-double writers.
    /// </summary>
    /// <param name="facet">The facet to initialize, which selects data or health endpoints.</param>
    /// <returns>The endpoint manager.</returns>
    private OmfEndpointManager CreateManager(string facet)
    {
        ICollection<string> errors = new List<string>();
        var endpoints = _endpoints;
        var buffering = new BufferingConfiguration();
        var configurationProvider = new Mock<IConfigurationProvider>();
        configurationProvider.Setup(provider => provider.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out endpoints, out errors)).Returns(true);
        configurationProvider.Setup(provider => provider.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out buffering, out errors)).Returns(true);

        var factory = new Mock<IOmfWriterFactory>();
        factory
            .Setup(f => f.GetOmfWriterInstance(It.IsAny<IEndpointConfiguration>(), It.IsAny<ILogger>(), It.IsAny<OmfWriterType>(), It.IsAny<IBufferingConfiguration>(), It.IsAny<Action<bool>>()))
            .Returns((IEndpointConfiguration configuration, ILogger _, OmfWriterType _, IBufferingConfiguration _, Action<bool> _) =>
            {
                var writer = new Mock<IOmfWriter>();
                writer.SetupGet(w => w.Id).Returns(configuration.Id);
                writer.SetupGet(w => w.TargetUri).Returns(new Uri(configuration.Endpoint));
                _writers[configuration.Id] = writer;
                return writer.Object;
            });

        var logManager = new Mock<ILogManager>();
        logManager.Setup(manager => manager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>())).Returns(new TestLogger());

        var manager = new OmfEndpointManager(configurationProvider.Object, factory.Object, logManager.Object, new Mock<IConfigurationProtector>().Object, _coordinator);
        manager.Initialize(EdgeSystemConstants.OmfEgressComponentId, facet);
        return manager;
    }
}
