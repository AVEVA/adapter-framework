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
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Failover.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;

namespace AdapterFramework.Data.Framework.Failover;

public class FailoverDataMessageProcessor : IFailoverDataMessageProcessor
{
    private readonly ILogger _logger;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IOmfDataEndpointManager _dataEndpointManager;
    private readonly object _lockObject = new();

    private BufferingConfiguration _bufferingConfiguration;

    private FailoverBackedUpOmfMessageQueue _internalBackedUpMessageQueue;
    private FailoverPersistentOmfMessageQueue _internalPersistentMessageQueue;
    private FileQueue _internalFileQueue;

    private CancellationTokenSource _internalDataQueueConsumerCts;
    private Task _internalDataQueueConsumerTask;

    private bool _disposed;

    public FailoverDataMessageProcessor(ILogger logger, IConfigurationProvider configurationProvider, IOmfDataEndpointManager dataEndpointManager)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;
        _dataEndpointManager = dataEndpointManager;
    }

    #region Public Properties

    public FailoverMode CurrentFailoverMode { get; private set; }

    public FailoverRole CurrentFailoverRole { get; private set; }

    public DateTime LastDataProcessedTime { get; private set; }

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

            // convert the ISerializedOmfMessage to a failover message so we can add process time ticks
            var failoverMessage = new FailoverSerializedOmfMessage(message.MessageType, message.MessageBody, message.MessageAction, message.ItemCount);

            if (CurrentFailoverMode == FailoverMode.Hot)
            {
                failoverMessage.ProcessTimeTicks = messageReceivedTime.Ticks;
                _internalBackedUpMessageQueue.Enqueue(failoverMessage);
            }
            else
            {
                LastDataProcessedTime = messageReceivedTime;

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
    {
        lock (_lockObject)
        {
            if (CurrentFailoverMode != FailoverMode.Hot && newFailoverMode == FailoverMode.Hot)
            {
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
            _internalDataQueueConsumerCts?.Cancel();

            if (_internalDataQueueConsumerTask != null)
            {
                try
                {
                    _internalDataQueueConsumerTask?.GetAwaiter().GetResult();
                }
                catch (AggregateException ae)
                {
                    ae.Handle(ex => ex is TaskCanceledException);
                }
                finally
                {
                    _internalDataQueueConsumerTask.Dispose();
                }
            }

            _internalBackedUpMessageQueue?.Dispose();
            _internalPersistentMessageQueue?.Dispose();
            _internalFileQueue?.Dispose();
            _internalDataQueueConsumerCts?.Dispose();
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
            _internalPersistentMessageQueue = new FailoverPersistentOmfMessageQueue(FailoverConstants.FailoverKeyword, _internalFileQueue, _logger);
        }

        var maxVolatileMemorySize = _bufferingConfiguration.EnablePersistentBuffering ? _bufferingConfiguration.MaxBufferSizeMB : BufferingConstants.DefaultVolatileMemorySizeMb;
        _internalBackedUpMessageQueue = new FailoverBackedUpOmfMessageQueue(maxVolatileMemorySize, BufferingConstants.DefaultMessageExpirationTime, _internalPersistentMessageQueue, _logger);
    }

    private void DeleteInternalDataQueue()
    {
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
                if (_internalBackedUpMessageQueue.TryDequeue(out var message))
                {
                    _dataEndpointManager.SendMessage(message);
                    LastDataProcessedTime = new DateTime(message.ProcessTimeTicks, DateTimeKind.Utc);
                }
                else
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
                _internalBackedUpMessageQueue.TryDequeue(out _);
            }
        }
    }

    #endregion
}
