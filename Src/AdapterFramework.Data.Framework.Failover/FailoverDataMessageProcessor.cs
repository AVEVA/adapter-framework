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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Failover.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;

namespace AdapterFramework.Data.Framework.Failover;

public class FailoverDataMessageProcessor : IFailoverDataMessageProcessor
{
    private readonly ILogger _logger;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IOmfDataEndpointManager _dataEndpointManager;
    private readonly OmfAwaitableCoordinator _awaitableCoordinator;
    private readonly object _lockObject = new();

    private BufferingConfiguration _bufferingConfiguration;

    private FailoverBackedUpOmfMessageQueue _internalBackedUpMessageQueue;
    private FailoverPersistentOmfMessageQueue _internalPersistentMessageQueue;
    private FileQueue _internalFileQueue;

    private CancellationTokenSource _internalDataQueueConsumerCts;
    private Task _internalDataQueueConsumerTask;

    private long _lastReceivedTimeTicks;
    private long _lastDispatchedTimeTicks;
    private long _nonHotProcessedTimeTicks;
    private long? _failedDispatchBoundary;
    private int _internalBodiesLost;
    private bool _enableAcceptanceTrimWatermark;

    private bool _disposed;

    public FailoverDataMessageProcessor(ILogger logger, IConfigurationProvider configurationProvider, IOmfDataEndpointManager dataEndpointManager, OmfAwaitableCoordinator awaitableCoordinator = null)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;
        _dataEndpointManager = dataEndpointManager;
        _awaitableCoordinator = awaitableCoordinator;
    }

    #region Public Properties

    public FailoverMode CurrentFailoverMode { get; private set; }

    public FailoverRole CurrentFailoverRole { get; private set; }

    /// <summary>
    /// Gets legacy hand-off progress by default, or opt-in hot acceptance progress across every data endpoint.
    /// With acceptance trimming enabled, a fully accepted idle primary
    /// advances through idle time, but never past a body still waiting in the failover buffer.
    /// A legacy endpoint manager that cannot report acceptance blocks trimming conservatively.
    /// Warm and cold modes retain their existing hand-off timestamp behavior.
    /// </summary>
    public DateTime LastDataProcessedTime
    {
        get
        {
            lock (_lockObject)
            {
                var ticks = _nonHotProcessedTimeTicks;
                if (CurrentFailoverMode == FailoverMode.Hot && !_enableAcceptanceTrimWatermark)
                {
                    ticks = _lastDispatchedTimeTicks;
                }
                else if (CurrentFailoverMode == FailoverMode.Hot)
                {
                    var acceptanceTicks = _dataEndpointManager.GetAcceptanceWatermark().Ticks;
                    ticks = Math.Min(_lastDispatchedTimeTicks, acceptanceTicks);
                    if (CurrentFailoverRole == FailoverRole.Primary && _lastDispatchedTimeTicks > 0
                        && acceptanceTicks >= _lastDispatchedTimeTicks)
                    {
                        // Otherwise the final batch on a quiet source can never cross the heartbeat's
                        // trim offset, leaving its secondary scope pending forever without new traffic.
                        ticks = Math.Max(ticks, DateTime.UtcNow.Ticks);
                        if (_internalBackedUpMessageQueue.TryPeek(out var pending))
                        {
                            ticks = Math.Min(ticks, Math.Max(pending.ProcessTimeTicks - 1, 0));
                        }
                    }
                    if (_failedDispatchBoundary is { } boundary)
                    {
                        ticks = Math.Min(ticks, boundary);
                    }

                    if (Volatile.Read(ref _internalBodiesLost) != 0)
                    {
                        ticks = 0;
                    }
                }

                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }
    }

    #endregion

    #region Public Methods

    public void Initialize()
    {
        _dataEndpointManager.Initialize(EdgeSystemConstants.OmfEgressComponentId, EdgeSystemConstants.DataEndpointsFacetName);
        _bufferingConfiguration = BufferingConfiguration.GetOrCreateBufferingConfiguration(_configurationProvider, _logger);
    }

    public void ProcessOmfMessage(ISerializedOmfMessage message)
    {
        lock (_lockObject)
        {
            if (message is null)
            {
                return;
            }

            var messageReceivedTime = DateTime.UtcNow;

            if (CurrentFailoverMode == FailoverMode.Hot)
            {
                // Keep enqueue order monotonic even when the wall clock moves backwards or two bodies
                // arrive in the same tick. Every body participates, not only those carrying a scope.
                _lastReceivedTimeTicks = _enableAcceptanceTrimWatermark
                    ? Math.Max(messageReceivedTime.Ticks, _lastReceivedTimeTicks + 1)
                    : messageReceivedTime.Ticks;
                var failoverMessage = new FailoverSerializedOmfMessage(message.MessageType, message.MessageBody,
                    message.MessageAction, message.ItemCount, message.OmfVersion, message.PartitionKey)
                {
                    ProcessTimeTicks = _lastReceivedTimeTicks,
                    SerializedMessageId = message.SerializedMessageId,
                };
                if (failoverMessage.SerializedMessageId is { } bodyId)
                {
                    _awaitableCoordinator?.RecordFailoverBuffered(bodyId, failoverMessage.ProcessTimeTicks);
                }

                try
                {
                    _internalBackedUpMessageQueue.Enqueue(failoverMessage);
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _internalBodiesLost, 1);
                    if (failoverMessage.SerializedMessageId is { } failedId)
                    {
                        _awaitableCoordinator?.RecordFailoverDiscarded(failedId,
                            new OmfOutcomeReason(OmfReasonCode.EnqueueFailed, ex.Message));
                    }

                    throw;
                }
            }
            else
            {
                _nonHotProcessedTimeTicks = messageReceivedTime.Ticks;

                _dataEndpointManager.SendMessage(message);
                if ((CurrentFailoverMode == FailoverMode.Warm || CurrentFailoverMode == FailoverMode.Cold) & CurrentFailoverRole == FailoverRole.Secondary)
                {
                    _logger.LogDebug("Processing transient messages while in secondary role. The current failover mode: {CurrentFailoverMode}.", CurrentFailoverMode);
                }
            }
        }
    }

    public void UpdateState(FailoverRole role, DateTime lastDataProcessedTime)
    {
        lock (_lockObject)
        {
            if (CurrentFailoverMode == FailoverMode.Hot)
            {
                if (CurrentFailoverRole == FailoverRole.Primary && role == FailoverRole.Secondary)
                {
                    StopConsumingInternalDataQueue();
                }
                else if (CurrentFailoverRole == FailoverRole.Secondary && role == FailoverRole.Primary)
                {
                    StartConsumingInternalDataQueue();
                }
                else if (CurrentFailoverRole == FailoverRole.Secondary && role == FailoverRole.Secondary)
                {
                    if (lastDataProcessedTime != default)
                    {
                        TrimInternalDataQueue(lastDataProcessedTime);
                    }
                }
            }

            CurrentFailoverRole = role;
        }
    }

    public void UpdateMode(FailoverMode newFailoverMode)
        => UpdateMode(newFailoverMode, false);

    /// <summary>
    /// Applies failover mode and trimming policy under the same lock as dispatch and trim. The default
    /// is legacy hand-off progress; both nodes must opt in before hot scope acceptance can be trusted.
    /// </summary>
    /// <param name="newFailoverMode">The configured failover mode.</param>
    /// <param name="enableAcceptanceTrimWatermark">Whether hot failover advertises endpoint acceptance progress.</param>
    public void UpdateMode(FailoverMode newFailoverMode, bool enableAcceptanceTrimWatermark)
    {
        lock (_lockObject)
        {
            if (CurrentFailoverMode != FailoverMode.Hot && newFailoverMode == FailoverMode.Hot)
            {
                _lastDispatchedTimeTicks = 0;
                _failedDispatchBoundary = null;
                CreateInternalDataQueue();
                if (CurrentFailoverRole == FailoverRole.Primary)
                {
                    StartConsumingInternalDataQueue();
                }
            }
            else if (CurrentFailoverMode == FailoverMode.Hot && newFailoverMode != FailoverMode.Hot)
            {
                StopConsumingInternalDataQueue();
                DeleteInternalDataQueue();
            }

            CurrentFailoverMode = newFailoverMode;
            _enableAcceptanceTrimWatermark = enableAcceptanceTrimWatermark;
            _awaitableCoordinator?.SetFailoverMode(newFailoverMode, enableAcceptanceTrimWatermark);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Methods

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            lock (_lockObject)
            {
                StopConsumingInternalDataQueue();
                UnsubscribeFromInternalQueueReports();
                _internalBackedUpMessageQueue?.Dispose();
                _internalPersistentMessageQueue?.Dispose();
                _internalFileQueue?.Dispose();
            }
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private void CreateInternalDataQueue()
    {
        if (_bufferingConfiguration.EnablePersistentBuffering)
        {
            var maxBufferFiles = _bufferingConfiguration.MaxBufferSizeMB / BufferingConstants.MaxBufferFileSizeMb;
            if (maxBufferFiles < 1)
            {
                maxBufferFiles = 1;
            }

            var bufferFilesPath = string.IsNullOrWhiteSpace(_bufferingConfiguration.BufferLocation)
                ? Path.Combine(_configurationProvider.GetCommonApplicationDataDirectoryPath(), EdgeSystemConstants.BuffersDirectoryName, FailoverConstants.FailoverKeyword)
                : Path.Combine(_bufferingConfiguration.BufferLocation, FailoverConstants.FailoverKeyword);
            _internalFileQueue = new FileQueue(bufferFilesPath, BufferingConstants.DataBufferFilePrefix, FailoverConstants.FailoverKeyword, BufferingConstants.MaxBufferFileSizeMb, maxBufferFiles, _logger);
            if (_internalFileQueue.HadDataLossAtStartup)
            {
                Interlocked.Exchange(ref _internalBodiesLost, 1);
            }

            _internalFileQueue.ItemsLost += OnInternalBodiesLost;
            _internalPersistentMessageQueue = new FailoverPersistentOmfMessageQueue(FailoverConstants.FailoverKeyword, _internalFileQueue, _logger);
        }

        var maxVolatileMemorySize = _bufferingConfiguration.EnablePersistentBuffering ? _bufferingConfiguration.MaxBufferSizeMB : BufferingConstants.DefaultVolatileMemorySizeMb;
        _internalBackedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(maxVolatileMemorySize, BufferingConstants.DefaultMessageExpirationTime, _internalPersistentMessageQueue, _logger);
        _internalBackedUpMessageQueue.BodiesLost += OnInternalBodiesLost;
        _internalBackedUpMessageQueue.SerializedBodiesDiscarded += OnInternalBodiesDiscarded;
        if (_internalPersistentMessageQueue is not null)
        {
            _internalPersistentMessageQueue.SerializedBodiesDiscarded += OnInternalBodiesDiscarded;
            _internalPersistentMessageQueue.SerializedBodyWriteFailed += OnInternalBodyWriteFailed;
        }
    }

    private void DeleteInternalDataQueue()
    {
        if (_internalBackedUpMessageQueue?.TryPeek(out _) == true)
        {
            Interlocked.Exchange(ref _internalBodiesLost, 1);
        }

        _awaitableCoordinator?.RecordFailoverBuffersDeleted();
        UnsubscribeFromInternalQueueReports();
        _internalBackedUpMessageQueue?.Dispose();
        _internalPersistentMessageQueue?.Dispose();
        _internalFileQueue?.Dispose();

        _internalFileQueue?.DeleteBuffers();
        _internalFileQueue = null;
        _internalPersistentMessageQueue = null;
        _internalBackedUpMessageQueue = null;
    }

    private void StartConsumingInternalDataQueue()
    {
        _internalDataQueueConsumerCts = new CancellationTokenSource();
        _internalDataQueueConsumerTask = Task.Run(() => ConsumeInternalDataQueueAsync());
    }

    private void StopConsumingInternalDataQueue()
    {
        _internalDataQueueConsumerCts?.Cancel();
        if (_internalDataQueueConsumerTask != null)
        {
            try
            {
                _internalDataQueueConsumerTask.GetAwaiter().GetResult();
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex => ex is TaskCanceledException);
            }
            finally
            {
                _internalDataQueueConsumerTask.Dispose();
                _internalDataQueueConsumerTask = null;
            }
        }

        _internalDataQueueConsumerCts?.Dispose();
        _internalDataQueueConsumerCts = null;
    }

    private async Task ConsumeInternalDataQueueAsync()
    {
        _logger.LogDebug("Started failover buffer consumer task.");

        try
        {
            while (!_internalDataQueueConsumerCts.IsCancellationRequested)
            {
                var consumed = false;
                // Mode changes hold this lock while cancelling and joining this task. Never block
                // acquiring it here, otherwise demotion could deadlock with its own consumer.
                if (Monitor.TryEnter(_lockObject))
                {
                    try
                    {
                        if (!_internalDataQueueConsumerCts.IsCancellationRequested
                            && _internalBackedUpMessageQueue.TryDequeue(out var message))
                        {
                            consumed = true;
                            try
                            {
                                // Failover records always need process ticks for local trimming. Legacy
                                // endpoint-writer records do not: retain their original V3 format for
                                // unscoped writes rather than forcing timestamp-bearing V5 records.
                                var dispatched = _enableAcceptanceTrimWatermark ? (ISerializedOmfMessage)message
                                    : new SerializedOmfMessage(message.MessageType, message.MessageBody,
                                        message.MessageAction, message.ItemCount, message.OmfVersion, message.PartitionKey);
                                _dataEndpointManager.SendMessage(dispatched);
                                _lastDispatchedTimeTicks = _enableAcceptanceTrimWatermark
                                    ? Math.Max(_lastDispatchedTimeTicks, message.ProcessTimeTicks)
                                    : message.ProcessTimeTicks;
                            }
                            catch (Exception ex)
                            {
                                var boundary = Math.Max(message.ProcessTimeTicks - 1, 0);
                                _failedDispatchBoundary = Math.Min(_failedDispatchBoundary ?? boundary, boundary);
                                if (message.SerializedMessageId is { } id)
                                {
                                    _awaitableCoordinator?.RecordFailoverDiscarded(id,
                                        new OmfOutcomeReason(OmfReasonCode.EnqueueFailed, ex.Message));
                                }

                                _logger.LogError(ex, "Failed to dispatch a body from the failover buffer.");
                            }
                        }
                    }
                    finally
                    {
                        Monitor.Exit(_lockObject);
                    }
                }

                if (!consumed)
                {
                    await Task.Delay(BufferingConstants.BufferRetryInterval, _internalDataQueueConsumerCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Failover buffer consumer task stopped.");
        }
    }

    private void TrimInternalDataQueue(DateTime receivedLastDataProcessedTime)
    {
        _logger.LogDebug("Trimming messages from the failover buffer older than: {Time:O}", receivedLastDataProcessedTime);

        if (_internalBackedUpMessageQueue != null)
        {
            while (_internalBackedUpMessageQueue.TryPeek(out var message)
                    && message.ProcessTimeTicks <= receivedLastDataProcessedTime.Ticks)
            {
                if (_internalBackedUpMessageQueue.TryDequeue(out var trimmed)
                    && trimmed.SerializedMessageId is { } id)
                {
                    if (_enableAcceptanceTrimWatermark)
                    {
                        _awaitableCoordinator?.RecordPeerCoverage(id);
                    }
                    else
                    {
                        _awaitableCoordinator?.RecordFailoverDiscarded(id,
                            new OmfOutcomeReason(OmfReasonCode.AcceptanceTrackingDisabled,
                                "Legacy hand-off trimming removed the body without proving acceptance."));
                    }
                }
            }
        }
    }

    /// <summary>Unsubscribes before shutdown flushes or deletes the queues.</summary>
    private void UnsubscribeFromInternalQueueReports()
    {
        if (_internalBackedUpMessageQueue is not null)
        {
            _internalBackedUpMessageQueue.BodiesLost -= OnInternalBodiesLost;
            _internalBackedUpMessageQueue.SerializedBodiesDiscarded -= OnInternalBodiesDiscarded;
        }

        if (_internalFileQueue is not null)
        {
            _internalFileQueue.ItemsLost -= OnInternalBodiesLost;
        }

        if (_internalPersistentMessageQueue is not null)
        {
            _internalPersistentMessageQueue.SerializedBodiesDiscarded -= OnInternalBodiesDiscarded;
            _internalPersistentMessageQueue.SerializedBodyWriteFailed -= OnInternalBodyWriteFailed;
        }
    }

    /// <summary>Reports volatile eviction and persisted record loss without reentering queue locks.</summary>
    private void OnInternalBodiesDiscarded(object sender, SerializedBodiesEventArgs e)
    {
        var reason = new OmfOutcomeReason(e.Reason, "The failover buffer lost the body before dispatch or peer coverage.");
        foreach (var id in e.SerializedMessageIds)
        {
            _awaitableCoordinator?.RecordFailoverDiscarded(id, reason);
        }
    }

    /// <summary>
    /// Blocks local coverage after any internal loss, including unscoped bodies. This callback runs
    /// under queue locks and must not acquire the processor lock or call back into a queue.
    /// </summary>
    private void OnInternalBodiesLost(object sender, EventArgs e) => Interlocked.Exchange(ref _internalBodiesLost, 1);

    /// <summary>Leaves failed disk writes pending; the queue retains them for a later flush.</summary>
    private void OnInternalBodyWriteFailed(object sender, SerializedBodiesEventArgs e)
    {
        // The body still has its AwaitingPeerCoverage placeholder and can be sent after promotion.
        _logger.LogWarning("Writing {Count} scoped bodies to the failover buffer failed; they remain pending.", e.SerializedMessageIds.Count);
    }

    #endregion
}
