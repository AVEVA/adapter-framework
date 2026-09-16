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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Common.Diagnostics.Events;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.AdapterCommon.Diagnostics;

public class AdapterDiagnosticsService : IEdgeComponentDiagnosticsService
{
    #region Private Constants

    private const int MovingAveragePeriod = 60;
    private const int SendStreamCountPeriod = 60;
    private const int SendAssetCountPeriod = 60;
    private const int SendEventCountPeriod = 60;
    private const int SendIoRatePeriod = 60;
    private const int SendErrorRatePeriod = 60;

    #endregion

    #region Private Fields

    private readonly TimeSpan _messageProcessorDiagnosticsSpan = TimeSpan.FromSeconds(1);
    private readonly TimeSpan _errorRateSpan = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _stateChangeSemaphore = new(1, 1);
    private readonly MovingAverage _errorRateMovingAverage;
    private readonly MovingAverage _ioRateMovingAverage;
    private readonly IInstrumentedLogger _instrumentedLogger;
    private readonly IInstrumentedMessageProcessor _instrumentedMessageProcessor;
    private readonly IDiagnosticsMessageProcessor _diagnosticsMessageProcessor;
    private readonly AdapterDiagnosticsOmfMessageCreator _diagnosticsOmfMessageCreator;
    private readonly string _componentId;
    private readonly string _componentType;

    private Timer _errorRateTimer;
    private Timer _messageProcessorStatisticsTimer;

    private int _updateErrorRateSync;
    private int _updateMessageProcessorStatisticsSync;
    private int _errorRateTimerTickCounter;
    private int _timerTickIoRateCounter;
    private int _timerTickStreamCounter;
    private int _timerTickAssetCounter;
    private int _timerTickEventCounter;
    private int _sentStreamCount = -1;
    private int _sentTypeCount = -1;
    private int _sentAssetCount = -1;
    private int _sentEventCount = -1;
    private bool _failedToCreateDiagnosticsTypes;
    private bool _failedToUpdateErrorRate;
    private bool _failedToUpdateMessageProcessorStatistics;
    private bool _disposed;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Instantiates a new instance of the <see cref="AdapterDiagnosticsService"/> class.
    /// </summary>
    /// <param name="diagnosticsMessageProcessor">Instance of <see cref="IDiagnosticsMessageProcessor"/> service.</param>
    /// <param name="instrumentedMessageProcessor">Instance of <see cref="IInstrumentedMessageProcessor"/> service.</param>
    /// <param name="componentId">The adapter component ID.</param>
    /// <param name="componentType">The adapter type.</param>
    /// <param name="elementNode">The node to link all the created streams to.</param>
    /// <param name="instrumentedLogger">Instance of <see cref="IInstrumentedLogger"/> service.</param>
    public AdapterDiagnosticsService(IDiagnosticsMessageProcessor diagnosticsMessageProcessor, IInstrumentedLogger instrumentedLogger,
        string componentId, string componentType, LinkNode elementNode, IInstrumentedMessageProcessor instrumentedMessageProcessor)
    {
        ThrowHelper.ThrowIfArgumentNull(diagnosticsMessageProcessor, nameof(diagnosticsMessageProcessor));
        ThrowHelper.ThrowIfArgumentNull(instrumentedMessageProcessor, nameof(instrumentedMessageProcessor));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentType, nameof(componentType));
        ThrowHelper.ThrowIfArgumentNull(elementNode, nameof(elementNode));
        ThrowHelper.ThrowIfArgumentNull(instrumentedLogger, nameof(instrumentedLogger));

        _diagnosticsOmfMessageCreator = new AdapterDiagnosticsOmfMessageCreator(componentId, diagnosticsMessageProcessor.StreamIdPrefix, elementNode);

        _diagnosticsMessageProcessor = diagnosticsMessageProcessor;
        _instrumentedMessageProcessor = instrumentedMessageProcessor;
        _instrumentedLogger = instrumentedLogger;

        _ioRateMovingAverage = new MovingAverage(MovingAveragePeriod);
        _errorRateMovingAverage = new MovingAverage(MovingAveragePeriod);

        _componentId = componentId;
        _componentType = componentType;
    }

    #endregion

    #region Public Methods

    public async Task InitializeAsync()
    {
        await _stateChangeSemaphore.WaitAsync();
        try
        {
            CreateDiagnosticsTypesStreams();

            _instrumentedLogger.LogDebug("{ServiceName} is initialized.", nameof(AdapterDiagnosticsService));
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task StartAsync()
    {
        await _stateChangeSemaphore.WaitAsync();
        try
        {
            StartTimers();

            _instrumentedLogger.LogDebug("{ServiceName} is started.", nameof(AdapterDiagnosticsService));
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task StopAsync()
    {
        await _stateChangeSemaphore.WaitAsync();
        try
        {
            _errorRateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _messageProcessorStatisticsTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            _instrumentedLogger.GetAndResetErrorCount();
            _instrumentedMessageProcessor.ClearCounters();
            _errorRateMovingAverage.ClearSamples();
            _ioRateMovingAverage.ClearSamples();

            _instrumentedLogger.LogDebug("{ServiceName} is Stopped.", nameof(AdapterDiagnosticsService));
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public void ResendTypesAndStreams()
    {
        CreateDiagnosticsTypesStreams();
        SendStreamCountEvent(_sentStreamCount, _sentTypeCount);
        SendAssetCountEvent(_sentAssetCount);
        SendEventCountEvent(_sentEventCount);
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
            _errorRateTimer?.Dispose();
            _errorRateTimer = null;

            _messageProcessorStatisticsTimer?.Dispose();
            _messageProcessorStatisticsTimer = null;

            _stateChangeSemaphore?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private void StartTimers()
    {
        if (_errorRateTimer == null)
        {
            _errorRateTimer = new Timer(CollectErrorRate, null, TimeSpan.Zero, _errorRateSpan);
        }
        else
        {
            _errorRateTimer.Change(TimeSpan.Zero, _errorRateSpan);
        }

        if (_messageProcessorStatisticsTimer == null)
        {
            _messageProcessorStatisticsTimer = new Timer(CollectMessageProcessorStatistics, null, TimeSpan.Zero, _messageProcessorDiagnosticsSpan);
        }
        else
        {
            _messageProcessorStatisticsTimer.Change(TimeSpan.Zero, _messageProcessorDiagnosticsSpan);
        }
    }

    private void CollectErrorRate(object state)
    {
        if (Interlocked.CompareExchange(ref _updateErrorRateSync, 1, 0) == 0)
        {
            try
            {
                var errorCount = _instrumentedLogger.GetAndResetErrorCount();

                _errorRateMovingAverage.AddSample(errorCount);

                if (_failedToUpdateErrorRate || _failedToCreateDiagnosticsTypes)
                {
                    return;
                }

                SendErrorRateEvent();
            }
            finally
            {
                Interlocked.Exchange(ref _updateErrorRateSync, 0);
            }
        }
    }

    private void CollectMessageProcessorStatistics(object state)
    {
        if (Interlocked.CompareExchange(ref _updateMessageProcessorStatisticsSync, 1, 0) == 0)
        {
            try
            {
                var sentEventsCount = _instrumentedMessageProcessor.GetAndResetEventsCounter();

                _ioRateMovingAverage.AddSample(sentEventsCount);

                if (_failedToUpdateMessageProcessorStatistics || _failedToCreateDiagnosticsTypes)
                {
                    return;
                }

                SendIoRateEvent();
                SendStreamCountEventWhenChanged();
                SendAssetCountEventWhenChanged();
                SendEventCountEventWhenChanged();
            }
            finally
            {
                Interlocked.Exchange(ref _updateMessageProcessorStatisticsSync, 0);
            }
        }
    }

    private void SendErrorRateEvent()
    {
        if (_errorRateTimerTickCounter <= 0)
        {
            _errorRateTimerTickCounter = SendErrorRatePeriod;

            try
            {
                var errorRate = new ErrorRateEvent
                {
                    Timestamp = DateTime.UtcNow,
                    ErrorRate = _errorRateMovingAverage.ComputeAverage(),
                };

                _diagnosticsMessageProcessor.WriteDiagnosticsValue(_diagnosticsOmfMessageCreator.GetErrorRateStreamId(), Classification.Dynamic, errorRate);
            }
            catch (Exception ex)
            {
                _instrumentedLogger.LogError(ex, "Failed to process ErrorRate diagnostics event. Stopping the Error Rate collection.");

                _failedToUpdateErrorRate = true;
            }
        }

        _errorRateTimerTickCounter--;
    }

    private void SendStreamCountEventWhenChanged()
    {
        if (_timerTickStreamCounter <= 0)
        {
            _timerTickStreamCounter = SendStreamCountPeriod;

            var currentStreamCount = _instrumentedMessageProcessor.GetStreamCount();
            var currentTypeCount = _instrumentedMessageProcessor.GetTypeCount();

            if (currentTypeCount != _sentTypeCount || currentStreamCount != _sentStreamCount)
            {
                Interlocked.Exchange(ref _sentStreamCount, currentStreamCount);
                Interlocked.Exchange(ref _sentTypeCount, currentTypeCount);

                SendStreamCountEvent(currentStreamCount, currentTypeCount);
            }
        }

        _timerTickStreamCounter--;
    }

    private void SendStreamCountEvent(int streamCount, int typeCount)
    {
        var streamCountEvent = new StreamCountEvent
        {
            Timestamp = DateTime.UtcNow,
            StreamCount = streamCount,
            TypeCount = typeCount,
        };

        try
        {
            _diagnosticsMessageProcessor.WriteDiagnosticsValue(_diagnosticsOmfMessageCreator.GetStreamCountStreamId(), Classification.Dynamic, streamCountEvent);
        }
        catch (Exception ex)
        {
            _instrumentedLogger.LogError(ex, "Failed to process StreamCount diagnostics event. Stopping IORate and StreamCount diagnostics data collection.");

            _failedToUpdateMessageProcessorStatistics = true;
        }
    }

    private void SendAssetCountEventWhenChanged()
    {
        if (_timerTickAssetCounter <= 0)
        {
            _timerTickAssetCounter = SendAssetCountPeriod;

            var currentAssetCount = _instrumentedMessageProcessor.GetAssetCount();

            if (currentAssetCount != _sentAssetCount)
            {
                Interlocked.Exchange(ref _sentAssetCount, currentAssetCount);

                SendAssetCountEvent(currentAssetCount);
            }
        }

        _timerTickAssetCounter--;
    }

    private void SendEventCountEventWhenChanged()
    {
        if (_timerTickEventCounter <= 0)
        {
            _timerTickEventCounter = SendEventCountPeriod;

            var currentEventCount = _instrumentedMessageProcessor.GetEventCount();

            if (currentEventCount != _sentEventCount)
            {
                Interlocked.Exchange(ref _sentEventCount, currentEventCount);

                SendEventCountEvent(currentEventCount);
            }
        }

        _timerTickEventCounter--;
    }

    private void SendAssetCountEvent(int assetCount)
    {
        var assetCountEvent = new AssetCountEvent
        {
            Timestamp = DateTime.UtcNow,
            AssetCount = assetCount,
        };

        try
        {
            _diagnosticsMessageProcessor.WriteDiagnosticsValue(_diagnosticsOmfMessageCreator.GetAssetCountStreamId(), Classification.Dynamic, assetCountEvent);
        }
        catch (Exception ex)
        {
            _instrumentedLogger.LogError(ex, "Failed to process AssetCount diagnostics event. Stopping AssetCount diagnostics data collection.");

            _failedToUpdateMessageProcessorStatistics = true;
        }
    }

    private void SendEventCountEvent(int eventCount)
    {
        var eventCountEvent = new EventCountEvent
        {
            Timestamp = DateTime.UtcNow,
            EventCount = eventCount,
        };

        try
        {
            _diagnosticsMessageProcessor.WriteDiagnosticsValue(_diagnosticsOmfMessageCreator.GetEventCountStreamId(), Classification.Dynamic, eventCountEvent);
        }
        catch (Exception ex)
        {
            _instrumentedLogger.LogError(ex, "Failed to process EventCount diagnostics event. Stopping EventCount diagnostics data collection.");

            _failedToUpdateMessageProcessorStatistics = true;
        }
    }

    private void SendIoRateEvent()
    {
        if (_timerTickIoRateCounter <= 0)
        {
            _timerTickIoRateCounter = SendIoRatePeriod;

            var dataRateMovingAverageValue = new IoRateEvent
            {
                Timestamp = DateTime.UtcNow,
                IORate = _ioRateMovingAverage.ComputeAverage(),
            };

            try
            {
                _diagnosticsMessageProcessor.WriteDiagnosticsValue(_diagnosticsOmfMessageCreator.GetIoRateStreamId(), Classification.Dynamic, dataRateMovingAverageValue);
            }
            catch (Exception ex)
            {
                _instrumentedLogger.LogError(ex, "Failed to process IORate diagnostics event. Stopping IORate and StreamCount diagnostics data collection.");

                _failedToUpdateMessageProcessorStatistics = true;
            }
        }

        _timerTickIoRateCounter--;
    }

    private void CreateDiagnosticsTypesStreams()
    {
        try
        {
            _diagnosticsOmfMessageCreator.CreateAndSendStructure(_diagnosticsMessageProcessor, _componentId, _componentType);
        }
        catch (Exception ex)
        {
            _instrumentedLogger.LogError(ex, "Failed to process diagnostics containers and types. Stopping diagnostics data collection.");

            _failedToCreateDiagnosticsTypes = true;
        }
    }

    #endregion
}
