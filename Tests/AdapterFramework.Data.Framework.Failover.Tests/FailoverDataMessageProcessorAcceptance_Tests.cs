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
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests;

public sealed class FailoverDataMessageProcessorAcceptance_Tests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Verifies an idle Hot primary reports only successful hand-off time, without idle-time advancement or scoped metadata.</summary>
    [Fact]
    public void LastDataProcessedTime_HotIdlePrimary_RetainsLastSuccessfulDispatch()
    {
        using var fixture = new ProcessorFixture();
        fixture.Processor.UpdateMode(FailoverMode.Hot);
        Assert.Equal(DateTime.MinValue, fixture.Processor.LastDataProcessedTime);
        var before = DateTime.UtcNow;
        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));
        var after = DateTime.UtcNow;
        fixture.Processor.UpdateState(FailoverRole.Primary, default);
        fixture.WaitForSent(1);
        var progress = fixture.Processor.LastDataProcessedTime;
        Assert.InRange(progress, before, after);
        Assert.Equal(DateTimeKind.Utc, progress.Kind);
        Assert.Equal(progress, fixture.Processor.LastDataProcessedTime);

        fixture.Processor.UpdateState(FailoverRole.Secondary, default);
        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 2 }, MessageAction.Default));
        Assert.Equal(progress, fixture.Processor.LastDataProcessedTime);
        var sent = Assert.Single(fixture.Sent);
        Assert.Null(sent.SerializedMessageId);
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
        fixture.Processor.UpdateMode(mode);
        fixture.Processor.UpdateState(role, default);
        var before = DateTime.UtcNow;

        fixture.Processor.ProcessOmfMessage(new SerializedOmfMessage(MessageType.Data, new byte[] { 1 }, MessageAction.Default));

        Assert.Single(fixture.Sent);
        Assert.InRange(fixture.Processor.LastDataProcessedTime, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, fixture.Processor.LastDataProcessedTime.Kind);
    }

    /// <summary>Verifies a scope whose body materializes after the switch to hot stays discarded, while the body is buffered, sent, or trimmed like any unscoped hot body.</summary>
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
        AssertTrackingDisabled(discarded);
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
        }

        fixture.Processor.UpdateMode(FailoverMode.Hot);
        var later = await scope.WaitForAcceptanceAsync(TimeSpan.Zero);
        AssertTrackingDisabled(later);
        Assert.Same(discarded.Reason, later.Reason);
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
        Assert.Null(Assert.Single(fixture.Sent).SerializedMessageId);
        fixture.Endpoint.Verify(endpoint => endpoint.SendMessage(It.IsAny<ISerializedOmfMessage>()), Times.Exactly(overflow ? 1 : 2));
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
        Assert.Equal(second.MessageBody, sent.MessageBody);
        Assert.Equal(second.MessageType, sent.MessageType);
        Assert.Equal(second.ItemCount, sent.ItemCount);
        Assert.Equal(second.PartitionKey, sent.PartitionKey);
        Assert.Equal(second.OmfVersion, sent.OmfVersion);
        Assert.Null(sent.SerializedMessageId);
        Assert.Equal(new DateTime(lastTicks, DateTimeKind.Utc), fixture.Processor.LastDataProcessedTime);
    }

    /// <summary>Checks the scope was discarded by hot mode and that no delivery was recorded for its buffered body.</summary>
    private static void AssertTrackingDisabled(OmfAcceptanceResult result)
    {
        Assert.Equal(OmfAcceptanceOutcome.Discarded, result.Outcome);
        Assert.Equal(OmfReasonCode.AcceptanceTrackingDisabled, result.Reason.Code);
        Assert.Empty(result.Deliveries);
    }

    private sealed class ProcessorFixture : IDisposable
    {
        public const string EndpointId = "local";
        public static readonly OmfDeliveryTarget Target = new(EndpointId, new Uri("https://local.example.com/omf"));
        public static readonly OmfIngressReceipt Receipt = new(Guid.NewGuid(), 0, TimeSpan.Zero);
        private readonly Mock<IConfigurationProvider> _configuration = new();

        /// <summary>Creates isolated real failover queues and a local endpoint that registers deliveries before simulating 202 acceptance.</summary>
        public ProcessorFixture(bool persistent = false)
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