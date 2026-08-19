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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Common.Diagnostics.Events;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.EgressComponent.Diagnostics;

public class EgressDiagnosticsService : IEdgeComponentDiagnosticsService
{
    #region Private Constants

    private const int MovingAveragePeriod = 60;
    private const int SendIoRatePeriod = 60;

    #endregion

    #region Private Fields

    private readonly TimeSpan _messageRateSpan = TimeSpan.FromSeconds(1);
    private readonly SemaphoreSlim _stateChangeSemaphore = new SemaphoreSlim(1, 1);
    private readonly Dictionary<string, string> _egressIdToStreamId = new Dictionary<string, string>();
    private readonly Dictionary<string, MovingAverage> _egressIdToIoRateMovingAverage;
    private readonly ILogger _logger;
    private readonly IEdgeComponentHealthService _healthService;
    private readonly IDiagnosticsMessageProcessor _diagnosticsMessageProcessor;
    private readonly EgressDiagnosticsOmfMessageCreator _egressDiagnosticsOmfMessageCreator;
    private readonly IOmfDataEndpointManager _dataEndpointManager;
    private Timer _valueRateTimer;

    private int _updateMessageProcessorStatisticsSync;
    private int _timerTickValueRateCounter;
    private bool _failedToCreateDiagnosticsTypes;
    private bool _failedToUpdateIoRate;
    private bool _disposed;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Instantiates a new instance of the <see cref="EgressDiagnosticsService"/> class.
    /// </summary>
    /// <param name="diagnosticsMessageProcessor">Instance of <see cref="IDiagnosticsMessageProcessor"/> service.</param>
    /// <param name="logger">The adapter logger.</param>
    /// <param name="componentId">The component id.</param>
    /// <param name="dataEndpointManager">Instance of <see cref="IOmfDataEndpointManager"/> service.</param>
    /// <param name="elementNodeLink">The node to link all the diagnostics stream to.</param>
    /// <param name="healthService">Instance of <see cref="IEdgeComponentHealthService"/>.</param>
    public EgressDiagnosticsService(IDiagnosticsMessageProcessor diagnosticsMessageProcessor,
        ILogger logger, string componentId, IOmfDataEndpointManager dataEndpointManager, LinkNode elementNodeLink, IEdgeComponentHealthService healthService)
    {
        ThrowHelper.ThrowIfArgumentNull(diagnosticsMessageProcessor, nameof(diagnosticsMessageProcessor));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNull(dataEndpointManager, nameof(dataEndpointManager));

        _egressDiagnosticsOmfMessageCreator = new EgressDiagnosticsOmfMessageCreator(componentId, elementNodeLink, diagnosticsMessageProcessor.StreamIdPrefix);
        _diagnosticsMessageProcessor = diagnosticsMessageProcessor;
        _logger = logger;
        _dataEndpointManager = dataEndpointManager;
        _healthService = healthService;
        _egressIdToIoRateMovingAverage = new Dictionary<string, MovingAverage>();
    }

    #endregion

    #region Public Methods

    public async Task InitializeAsync()
    {
        await _stateChangeSemaphore.WaitAsync();
        try
        {
            ResendTypesAndStreams();

            _logger.LogDebug("{ServiceName} is initialized.", nameof(EgressDiagnosticsService));
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

            _logger.LogDebug("{ServiceName} is started.", nameof(EgressDiagnosticsService));
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
            _valueRateTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var movingAverage in _egressIdToIoRateMovingAverage.Values)
            {
                movingAverage.ClearSamples();
            }

            _logger.LogDebug("{ServiceName} is stopped.", nameof(EgressDiagnosticsService));
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public void ResendTypesAndStreams()
    {
        CreateDiagnosticsTypesStreams();

        foreach (var item in _egressIdToIoRateMovingAverage)
        {
            CreateIoRateStreamAndLink(item.Key);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _valueRateTimer?.Dispose();
            _valueRateTimer = null;
            _stateChangeSemaphore?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private void StartTimers()
    {
        // toss old data because it may skew average.
        _dataEndpointManager.GetAndResetEgressedValuesCounters();

        if (_valueRateTimer == null)
        {
            _valueRateTimer = new Timer(CollectMessageProcessorStatistics, null, _messageRateSpan, _messageRateSpan);
        }
        else
        {
            _valueRateTimer.Change(_messageRateSpan, _messageRateSpan);
        }
    }

    private void CollectMessageProcessorStatistics(object state)
    {
        if (Interlocked.CompareExchange(ref _updateMessageProcessorStatisticsSync, 1, 0) == 0)
        {
            try
            {
                var egressDataCount = _dataEndpointManager.GetAndResetEgressedValuesCounters();
                if (egressDataCount == null)
                {
                    return;
                }

                var resendTypesStreamsLinks = false;

                foreach (var (streamId, valueCount) in egressDataCount)
                {
                    if (!_egressIdToIoRateMovingAverage.ContainsKey(streamId))
                    {
                        resendTypesStreamsLinks = true;
                        var stream = _egressDiagnosticsOmfMessageCreator.CreateIoRateStream(streamId);

                        _egressIdToIoRateMovingAverage[streamId] = new MovingAverage(MovingAveragePeriod);
                        _egressIdToStreamId[streamId] = stream.Id;
                        _timerTickValueRateCounter = 0;
                    }

                    _egressIdToIoRateMovingAverage[streamId].AddSample(valueCount);
                }

                // an egress was removed.
                if (egressDataCount.Count < _egressIdToIoRateMovingAverage.Count)
                {
                    resendTypesStreamsLinks = true;
                    var removedItems = _egressIdToIoRateMovingAverage.Keys.Except(egressDataCount.Keys);
                    foreach (var streamId in removedItems)
                    {
                        _egressIdToIoRateMovingAverage.Remove(streamId);
                        _egressIdToStreamId.Remove(streamId);
                    }
                }

                if (resendTypesStreamsLinks)
                {
                    ResendAllTypesAndStreams();
                }

                if (_failedToUpdateIoRate || _failedToCreateDiagnosticsTypes)
                {
                    return;
                }

                SendDataRateEvent();
            }
            finally
            {
                Interlocked.Exchange(ref _updateMessageProcessorStatisticsSync, 0);
            }
        }
    }

    private void SendDataRateEvent()
    {
        if (_timerTickValueRateCounter <= 0)
        {
            _timerTickValueRateCounter = SendIoRatePeriod;

            foreach (var item in _egressIdToIoRateMovingAverage)
            {
                var dataRateMovingAverageValue = new IoRateEvent
                {
                    Timestamp = DateTime.UtcNow,
                    IORate = item.Value.ComputeAverage(),
                };

                try
                {
                    _diagnosticsMessageProcessor.WriteDiagnosticsValue(_egressIdToStreamId[item.Key],
                        Classification.Dynamic, dataRateMovingAverageValue);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process IORate diagnostics event. Stopping IORate diagnostics data collection.");

                    _failedToUpdateIoRate = true;
                }
            }
        }

        _timerTickValueRateCounter--;
    }

    private void CreateDiagnosticsTypesStreams()
    {
        try
        {
            var egressTypes = EgressDiagnosticsOmfMessageCreator.GetTypes();
            _diagnosticsMessageProcessor.WriteDiagnosticsTypes(egressTypes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process diagnostics containers and types. Stopping diagnostics data collection.");

            _failedToCreateDiagnosticsTypes = true;
        }
    }

    private DataStream CreateIoRateStreamAndLink(string streamId)
    {
        var stream = _egressDiagnosticsOmfMessageCreator.CreateIoRateStream(streamId);
        _diagnosticsMessageProcessor.WriteDiagnosticsStreams(new[] { stream, });

        var (id, classification, link) = _egressDiagnosticsOmfMessageCreator.CreateLink(stream.Id);
        _diagnosticsMessageProcessor.WriteDiagnosticsValue(id, classification, link);

        return stream;
    }

    private void ResendAllTypesAndStreams()
    {
        _healthService.ResendTypesAndStreams();
        ResendTypesAndStreams();
    }

    #endregion
}
