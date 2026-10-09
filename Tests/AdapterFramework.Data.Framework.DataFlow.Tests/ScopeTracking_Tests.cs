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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

/// <summary>
/// Tests how the OMF 2.0 grouping and serialization blocks carry scope identity, forward seal barriers, and report bodies to the coordinator.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Posted messages are disposed by the SerializationBlock handler")]
public class ScopeTracking_Tests
{
    private const int WaitTime = 5000;
    private const int NoFlushTime = 60000;
    private const string EndpointId = "endpoint";

    /// <summary>
    /// Verifies that a flushed instance message records the scope of every scoped value and relationship while unscoped items stay unscoped,
    /// and that a flush-on-seal barrier flushes the buffer and then forwards a materialization barrier.
    /// </summary>
    [Fact]
    public void InstanceGroupingBlock_SealWithFlushOnSeal_FlushesSidecarThenForwardsBarrier()
    {
        var flushed = new ConcurrentQueue<Message>();
        using var block = new InstanceGroupingBlock(new TestLogger(), -1, flushed.Enqueue, _ => { }, 1000, NoFlushTime, CancellationToken.None);
        var a = new TestScope();
        var b = new TestScope();

        block.Post(new DataMessage("s1", Classification.Dynamic, 1, MessageAction.Default) { Scope = a });
        block.Post(new DataMessage("s1", Classification.Dynamic, 2, MessageAction.Default));
        block.Post(new BulkDataMessage("s1", Classification.Dynamic, [3, 4], MessageAction.Default) { Scope = a });
        block.Post(new DataMessage("s2", Classification.Dynamic, 5, MessageAction.Default) { Scope = b });
        block.Post(new RelationshipMessage(new Link(null, null), MessageAction.Default));
        block.Post(new RelationshipMessage(new Link(null, null), MessageAction.Default) { Scope = a });
        block.Post(new ScopeSealBarrier(a, flushOnSeal: true, expectedBarrierCount: 2));

        Assert.True(SpinWait.SpinUntil(() => flushed.Count == 2, WaitTime));
        var messages = flushed.ToArray();
        var instance = Assert.IsType<InstanceMessage>(messages[0]);
        var barrier = Assert.IsType<ScopeMaterializationBarrier>(messages[1]);
        Assert.Same(a, barrier.Scope);
        Assert.Equal(2, barrier.ExpectedBarrierCount);

        var values = Membership(instance.Sidecar, ScopeSidecarKind.StreamingData, instance.StreamingDataObjectCount);
        Assert.Equal(3, values[a]);
        Assert.Equal(1, values[b]);
        var relationships = Membership(instance.Sidecar, ScopeSidecarKind.Relationships, instance.RelationshipCount);
        Assert.Equal(1, relationships[a]);
        Assert.False(relationships.ContainsKey(b));
    }

    /// <summary>
    /// Verifies that a seal barrier for a scope with nothing buffered in the block is forwarded as a materialization barrier at once.
    /// </summary>
    [Fact]
    public void InstanceGroupingBlock_SealForUnbufferedScope_ForwardsBarrierImmediately()
    {
        var flushed = new ConcurrentQueue<Message>();
        using var block = new InstanceGroupingBlock(new TestLogger(), -1, flushed.Enqueue, _ => { }, 1000, NoFlushTime, CancellationToken.None);
        var a = new TestScope();

        block.Post(new DataMessage("s1", Classification.Dynamic, 1, MessageAction.Default) { Scope = new TestScope() });
        block.Post(new ScopeSealBarrier(a, flushOnSeal: false, expectedBarrierCount: 2));

        Assert.True(SpinWait.SpinUntil(() => !flushed.IsEmpty, WaitTime));
        var barrier = Assert.IsType<ScopeMaterializationBarrier>(Assert.Single(flushed));
        Assert.Same(a, barrier.Scope);
    }

    /// <summary>
    /// Verifies that without flush-on-seal the materialization barrier waits for the next flush, and that it follows the partition-key
    /// message when the flush returns early after emitting only partitioned streams.
    /// </summary>
    [Fact]
    public async Task InstanceGroupingBlock_SealWithoutFlushOnSeal_ForwardsBarrierAfterPartitionedFlush()
    {
        var flushed = new ConcurrentQueue<Message>();
        using var block = new InstanceGroupingBlock(new TestLogger(), -1, flushed.Enqueue, _ => { }, 1000, NoFlushTime, CancellationToken.None);
        var a = new TestScope();

        block.Post(new DataMessage("s1", Classification.Dynamic, 1, MessageAction.Default, PartitionKey.Key2) { Scope = a });
        block.Post(new ScopeSealBarrier(a, flushOnSeal: false, expectedBarrierCount: 2));
        await Task.Delay(200);
        Assert.True(flushed.IsEmpty);

        block.Post(new CommandMessage(true));

        Assert.True(SpinWait.SpinUntil(() => flushed.Count == 2, WaitTime));
        var messages = flushed.ToArray();
        var instance = Assert.IsType<InstanceMessage>(messages[0]);
        Assert.Equal(PartitionKey.Key2, instance.PartitionKey);
        Assert.Equal(1, Membership(instance.Sidecar, ScopeSidecarKind.StreamingData, instance.StreamingDataObjectCount)[a]);
        Assert.Same(a, Assert.IsType<ScopeMaterializationBarrier>(messages[1]).Scope);
    }

    /// <summary>
    /// Verifies that a schema message records a scoped type and the relationships embedded in it, while an unscoped container stays unscoped.
    /// </summary>
    [Fact]
    public void SchemaGroupingBlock_ScopedTypeWithRelationships_RecordsTypeAndRelationships()
    {
        var flushed = new ConcurrentQueue<Message>();
        using var block = new SchemaGroupingBlock(new TestLogger(), -1, flushed.Enqueue, 1000, 1000, NoFlushTime, CancellationToken.None);
        var a = new TestScope();
        var type = new StaticDataType { Id = "type", Relationships = [new Link(null, null), new Link(null, null)] };

        block.Post(new OmfMessage<DataStream>(1, [new DataStream { Id = "stream" }], MessageAction.Default));
        block.Post(new OmfMessage<DataType>(1, [type], MessageAction.Default) { Scope = a });
        block.Post(new ScopeSealBarrier(a, flushOnSeal: true, expectedBarrierCount: 2));

        Assert.True(SpinWait.SpinUntil(() => flushed.Count == 2, WaitTime));
        var schema = Assert.IsType<SchemaMessage>(flushed.First());
        Assert.Equal(1, Membership(schema.Sidecar, ScopeSidecarKind.Types, schema.TypeCount)[a]);
        Assert.Equal(2, Membership(schema.Sidecar, ScopeSidecarKind.Relationships, schema.RelationshipCount)[a]);
        Assert.Empty(Membership(schema.Sidecar, ScopeSidecarKind.Containers, schema.ContainerCount));
        Assert.Equal(2, type.Relationships.Count);
    }

    /// <summary>
    /// Verifies that when one stream is split into several bodies, each scope's values are registered with the bodies that carry them,
    /// so both scopes are accepted once every body is accepted.
    /// </summary>
    [Fact]
    public async Task SerializationBlock_SplitStream_RegistersEveryScopedValue()
    {
        using var coordinator = CreateCoordinator();
        var a = CreateScope(coordinator);
        var b = CreateScope(coordinator);
        var bodies = new ConcurrentQueue<ISerializedOmfMessage>();
        using var block = CreateSerializationBlock(coordinator, 400, bodies);

        var stream = new StreamingDataInstance { Id = "s", Values = Enumerable.Range(0, 10).Select(_ => (object)new string('x', 100)).ToList() };
        var sidecar = new InstanceScopeSidecar([a, b], [[new ScopeRange(0, 0, 5), new ScopeRange(1, 5, 5)]], null, null, null);
        coordinator.RecordAdmitted(a, 5);
        coordinator.RecordAdmitted(b, 5);
        block.Post(new InstanceMessage([stream], null, null, null, 1, 10, 0, 0, 0, MessageAction.Default) { Sidecar = sidecar });
        block.Post(new ScopeMaterializationBarrier(a, 1));
        block.Post(new ScopeMaterializationBarrier(b, 1));
        a.Seal();
        b.Seal();

        var resultA = await a.WaitForAcceptanceAsync(TimeSpan.FromMilliseconds(WaitTime));
        var resultB = await b.WaitForAcceptanceAsync(TimeSpan.FromMilliseconds(WaitTime));

        Assert.Equal(OmfAcceptanceOutcome.Accepted, resultA.Outcome);
        Assert.Equal(OmfAcceptanceOutcome.Accepted, resultB.Outcome);
        Assert.True(bodies.Count > 1);
        Assert.Equal(10, bodies.Sum(body => body.ItemCount));
        Assert.InRange(resultA.Deliveries.Count + resultB.Deliveries.Count, bodies.Count, bodies.Count + 1);
        await a.DisposeAsync();
        await b.DisposeAsync();
    }

    /// <summary>
    /// Verifies that a single value too large to send is discarded with <see cref="OmfReasonCode.ItemTooLarge"/> for its scope.
    /// </summary>
    [Fact]
    public async Task SerializationBlock_OversizedValue_DiscardsWithItemTooLarge()
    {
        using var coordinator = CreateCoordinator();
        var a = CreateScope(coordinator);
        var bodies = new ConcurrentQueue<ISerializedOmfMessage>();
        using var block = CreateSerializationBlock(coordinator, 300, bodies);

        var stream = new StreamingDataInstance { Id = "s", Values = ["small", new string('x', 1000)] };
        var sidecar = new InstanceScopeSidecar([a], [[new ScopeRange(0, 0, 2)]], null, null, null);
        coordinator.RecordAdmitted(a, 2);
        block.Post(new InstanceMessage([stream], null, null, null, 1, 2, 0, 0, 0, MessageAction.Default) { Sidecar = sidecar });
        block.Post(new ScopeMaterializationBarrier(a, 1));
        a.Seal();

        var result = await a.WaitForAcceptanceAsync(TimeSpan.FromMilliseconds(WaitTime));

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.ItemTooLarge, result.Reason.Code);
        await a.DisposeAsync();
    }

    /// <summary>
    /// Verifies that a single entity too large to send is discarded with <see cref="OmfReasonCode.ItemTooLarge"/> instead of being split forever.
    /// </summary>
    [Fact]
    public async Task SerializationBlock_OversizedEntity_DiscardsWithItemTooLarge()
    {
        using var coordinator = CreateCoordinator();
        var a = CreateScope(coordinator);
        var bodies = new ConcurrentQueue<ISerializedOmfMessage>();
        using var block = CreateSerializationBlock(coordinator, 300, bodies);

        var entities = new[] { new StaticStreamData { Id = "small" }, new StaticStreamData { Id = "large", Value = new string('x', 1000) } };
        coordinator.RecordAdmitted(a, 2);
        block.Post(new InstanceMessage(null, entities, null, null, 0, 0, 2, 0, 0, MessageAction.Default) { Sidecar = new InstanceScopeSidecar([a], null, [0, 0], null, null) });
        block.Post(new ScopeMaterializationBarrier(a, 1));
        a.Seal();

        var result = await a.WaitForAcceptanceAsync(TimeSpan.FromMilliseconds(WaitTime));

        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.ItemTooLarge, result.Reason.Code);
        Assert.Single(bodies);
        await a.DisposeAsync();
    }

    /// <summary>
    /// Verifies that the serialization block closes materialization only after the expected number of materialization barriers arrive.
    /// </summary>
    [Fact]
    public async Task SerializationBlock_MaterializationBarriers_CloseAfterExpectedCount()
    {
        using var coordinator = CreateCoordinator();
        var a = CreateScope(coordinator);
        var bodies = new ConcurrentQueue<ISerializedOmfMessage>();
        using var block = CreateSerializationBlock(coordinator, 10000, bodies);

        var stream = new StreamingDataInstance { Id = "s", Values = [1] };
        coordinator.RecordAdmitted(a, 1);
        block.Post(new InstanceMessage([stream], null, null, null, 1, 1, 0, 0, 0, MessageAction.Default) { Sidecar = new InstanceScopeSidecar([a], [[new ScopeRange(0, 0, 1)]], null, null, null) });
        block.Post(new ScopeMaterializationBarrier(a, 2));
        a.Seal();

        var wait = a.WaitForAcceptanceAsync(TimeSpan.FromMilliseconds(WaitTime));
        await Task.Delay(300);
        Assert.False(wait.IsCompleted);

        block.Post(new ScopeMaterializationBarrier(a, 2));

        Assert.Equal(OmfAcceptanceOutcome.Accepted, (await wait).Outcome);
        await a.DisposeAsync();
    }

    private static Dictionary<ScopeToken, int> Membership(ScopeSidecar sidecar, ScopeSidecarKind kind, int count)
    {
        Assert.NotNull(sidecar);
        var membership = new Dictionary<ScopeToken, int>();
        sidecar.AddEntries(kind, 0, count, membership);
        return membership;
    }

    private static OmfAwaitableCoordinator CreateCoordinator()
    {
        var coordinator = new OmfAwaitableCoordinator();
        coordinator.SetOmfVersion(OmfVersion.Omf20);
        return coordinator;
    }

    private static OmfAwaitableScopeState CreateScope(OmfAwaitableCoordinator coordinator)
    {
        Assert.True(coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out var scope));
        return scope;
    }

    private static SerializationBlock CreateSerializationBlock(OmfAwaitableCoordinator coordinator, int maxByteCount, ConcurrentQueue<ISerializedOmfMessage> bodies) =>
        new(null, null, maxByteCount, new TestLogger(), -1, new OmfJsonSerializer(), null,
            body =>
            {
                bodies.Enqueue(body);
                if (body is SerializedOmfMessage { SerializedMessageId: { } id })
                {
                    coordinator.RegisterDeliveries(id, [new OmfDeliveryTarget(EndpointId, new Uri("https://example.com/omf"))]);
                    coordinator.RecordDisposition(id, EndpointId, OmfDeliveryState.Accepted, HttpStatusCode.Accepted, null);
                }
            },
            CancellationToken.None,
            OmfVersion.Omf20,
            coordinator);

    private sealed class TestScope : ScopeToken
    {
    }
}
