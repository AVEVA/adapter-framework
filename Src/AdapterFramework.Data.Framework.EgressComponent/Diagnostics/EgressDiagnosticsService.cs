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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Common.Diagnostics.Events;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Common.Constants.DiagnosticsConstants;

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
    private readonly ConcurrentDictionary<string, ResourceIoRates> _egressIdToResourceIoRates = new();
    private readonly bool _resourceIoRatesEnabled;
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
    private bool _failedToUpdateResourceIoRates;
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
        : this(diagnosticsMessageProcessor, logger, componentId, dataEndpointManager, elementNodeLink, healthService, OmfVersion.Omf12)
    {
    }

    /// <summary>
    /// Instantiates a new instance of the <see cref="EgressDiagnosticsService"/> class.
    /// </summary>
    /// <param name="diagnosticsMessageProcessor">Instance of <see cref="IDiagnosticsMessageProcessor"/> service.</param>
    /// <param name="logger">The adapter logger.</param>
    /// <param name="componentId">The component id.</param>
    /// <param name="dataEndpointManager">Instance of <see cref="IOmfDataEndpointManager"/> service.</param>
    /// <param name="elementNodeLink">The node to link all the diagnostics stream to.</param>
    /// <param name="healthService">Instance of <see cref="IEdgeComponentHealthService"/>.</param>
    /// <param name="omfVersion">The OMF version the adapter egresses. OMF 2.0 adds the per-resource
    /// <c>StreamIORate</c>, <c>AssetIORate</c> and <c>EventIORate</c> streams for every endpoint.</param>
    public EgressDiagnosticsService(IDiagnosticsMessageProcessor diagnosticsMessageProcessor,
        ILogger logger, string componentId, IOmfDataEndpointManager dataEndpointManager, LinkNode elementNodeLink, IEdgeComponentHealthService healthService,
        OmfVersion omfVersion)
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
        _resourceIoRatesEnabled = omfVersion == OmfVersion.Omf20;
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

            foreach (var resourceIoRates in _egressIdToResourceIoRates.Values)
            {
                resourceIoRates.ClearSamples();
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

        foreach (var resourceIoRates in _egressIdToResourceIoRates.Values)
        {
            CreateResourceIoRateStreamsAndLinks(resourceIoRates);
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
        if (_resourceIoRatesEnabled)
        {
            _dataEndpointManager.GetAndResetEgressedResourceCounters();
        }

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

                var egressResourceCounts = _resourceIoRatesEnabled ? _dataEndpointManager.GetAndResetEgressedResourceCounters() : null;
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

                        if (_resourceIoRatesEnabled)
                        {
                            _egressIdToResourceIoRates[streamId] = new ResourceIoRates(_egressDiagnosticsOmfMessageCreator, streamId);
                        }
                    }

                    _egressIdToIoRateMovingAverage[streamId].AddSample(valueCount);

                    if (_resourceIoRatesEnabled && _egressIdToResourceIoRates.TryGetValue(streamId, out var resourceIoRates))
                    {
                        OmfResourceCounts resourceCounts = default;
                        egressResourceCounts?.TryGetValue(streamId, out resourceCounts);
                        resourceIoRates.AddSamples(resourceCounts);
                    }
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
                        _egressIdToResourceIoRates.TryRemove(streamId, out _);
                    }
                }

                if (resendTypesStreamsLinks)
                {
                    ResendAllTypesAndStreams();
                }

                if (_failedToCreateDiagnosticsTypes || (_failedToUpdateIoRate && !CanSendResourceIoRates()))
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

    private bool CanSendResourceIoRates() => _resourceIoRatesEnabled && !_failedToUpdateResourceIoRates;

    private void SendDataRateEvent()
    {
        if (_timerTickValueRateCounter <= 0)
        {
            _timerTickValueRateCounter = SendIoRatePeriod;

            if (!_failedToUpdateIoRate)
            {
                SendIoRateEvents();
            }

            if (CanSendResourceIoRates())
            {
                SendResourceIoRateEvents();
            }
        }

        _timerTickValueRateCounter--;
    }

    private void SendIoRateEvents()
    {
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

    private void SendResourceIoRateEvents()
    {
        foreach (var resourceIoRates in _egressIdToResourceIoRates.Values)
        {
            foreach (var (streamId, movingAverage) in resourceIoRates.GetRates())
            {
                var ioRateEvent = new IoRateEvent
                {
                    Timestamp = DateTime.UtcNow,
                    IORate = movingAverage.ComputeAverage(),
                };

                try
                {
                    _diagnosticsMessageProcessor.WriteDiagnosticsValue(streamId, Classification.Dynamic, ioRateEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process OMF 2.0 resource IORate diagnostics event. Stopping resource IORate diagnostics data collection.");

                    _failedToUpdateResourceIoRates = true;
                }
            }
        }
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

    private void CreateResourceIoRateStreamsAndLinks(ResourceIoRates resourceIoRates)
    {
        var streams = resourceIoRates.Streams;
        _diagnosticsMessageProcessor.WriteDiagnosticsStreams(streams);

        foreach (var stream in streams)
        {
            var (id, classification, link) = _egressDiagnosticsOmfMessageCreator.CreateLink(stream.Id);
            _diagnosticsMessageProcessor.WriteDiagnosticsValue(id, classification, link);
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// OMF 2.0 per-resource IO rate moving averages and streams for a single egress endpoint.
    /// </summary>
    private sealed class ResourceIoRates
    {
        private readonly MovingAverage _streamingValues = new(MovingAveragePeriod);
        private readonly MovingAverage _assets = new(MovingAveragePeriod);
        private readonly MovingAverage _events = new(MovingAveragePeriod);

        public ResourceIoRates(EgressDiagnosticsOmfMessageCreator messageCreator, string endpointId)
        {
            Streams =
            [
                messageCreator.CreateIoRateStream(endpointId, StreamIoRateStreamName),
                messageCreator.CreateIoRateStream(endpointId, AssetIoRateStreamName),
                messageCreator.CreateIoRateStream(endpointId, EventIoRateStreamName),
            ];
        }

        /// <summary>
        /// Gets the StreamIORate, AssetIORate and EventIORate streams, in that order.
        /// </summary>
        public DataStream[] Streams { get; }

        public void AddSamples(OmfResourceCounts resourceCounts)
        {
            _streamingValues.AddSample(resourceCounts.StreamingValues);
            _assets.AddSample(resourceCounts.Assets);
            _events.AddSample(resourceCounts.Events);
        }

        public void ClearSamples()
        {
            _streamingValues.ClearSamples();
            _assets.ClearSamples();
            _events.ClearSamples();
        }

        public IEnumerable<(string StreamId, MovingAverage MovingAverage)> GetRates()
        {
            yield return (Streams[0].Id, _streamingValues);
            yield return (Streams[1].Id, _assets);
            yield return (Streams[2].Id, _events);
        }
    }

    #endregion
}
