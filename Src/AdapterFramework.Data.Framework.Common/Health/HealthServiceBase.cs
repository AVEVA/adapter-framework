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
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;

namespace AdapterFramework.Data.Framework.Common.Health;

/// <summary>
/// Allows adapters to send health messages by using OMF.
/// </summary>
public abstract class HealthServiceBase : IHealthService, IEdgeComponentHealthService, IFailoverService
{
    #region Private Fields

    private readonly IHealthMessageProcessor _healthMessageProcessor;
    private readonly string _componentId;
    private readonly string _componentType;
    private readonly TimeSpan _heartBeatSpan = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _stateChangeSemSlim = new SemaphoreSlim(1, 1);
    private readonly object _deviceStatusChangeLock = new object();
    private readonly ILogger _logger;
    private bool _isInitialized;
    private int _sendHeartBeatSync;
    private Timer _heartBeatTimer;
    private DeviceStatus? _currentDeviceStatus;
    private Action<DeviceStatus> _deviceStatusUpdateAction;
    private bool _disposed;
    private float _failoverScore;
    private int _dataSelectionCount;
    private bool _deviceStatusEnabled = true;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthServiceBase"/> class.
    /// </summary>
    /// <param name="healthMessageProcessor">Object that will process health OMF messages.</param>
    /// <param name="logger">The adapter logger.</param>
    /// <param name="componentId">The adapter instance name (e.g. ModbusInstance1).</param>
    /// <param name="componentType">The adapter component type (e.g. Modbus).</param>
    protected HealthServiceBase(IHealthMessageProcessor healthMessageProcessor, ILogger logger, string componentId, string componentType)
    {
        _healthMessageProcessor = healthMessageProcessor ?? throw new ArgumentNullException(nameof(healthMessageProcessor));
        _logger = logger;
        _componentId = componentId ?? throw new ArgumentNullException(nameof(componentId));
        _componentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
    }

    #endregion

    #region Protected Properties

    protected abstract HealthOmfMessageCreatorBase HealthMessageCreator { get; }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _stateChangeSemSlim.WaitAsync();
        try
        {
            await InitializeInternalAsync();
        }
        finally
        {
            _stateChangeSemSlim.Release();
        }
    }

    /// <inheritdoc />
    public async Task StartAsync()
    {
        await _stateChangeSemSlim.WaitAsync();
        ActivateDeviceStatusUpdates();

        if (!_isInitialized)
        {
            await InitializeInternalAsync();
        }

        if (_heartBeatTimer == null)
        {
            _heartBeatTimer = new Timer(SendHeartBeat, null, TimeSpan.Zero, _heartBeatSpan);
        }
        else
        {
            _heartBeatTimer.Change(TimeSpan.Zero, _heartBeatSpan);
        }

        _logger?.LogDebug("Health service started for {ComponentId}.", _componentId);
        _stateChangeSemSlim.Release();
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        await _stateChangeSemSlim.WaitAsync();

        lock (_deviceStatusChangeLock)
        {
            _currentDeviceStatus = null;
        }

        _heartBeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        _logger?.LogDebug("Health service stopped for {ComponentId}.", _componentId);
        _stateChangeSemSlim.Release();
    }

    /// <inheritdoc />
    public void SendDeviceStatus(DeviceStatus status, float? failoverScore = null)
    {
        ThrowIfNotInitialized();

        lock (_deviceStatusChangeLock)
        {
            if (_deviceStatusEnabled)
            {
                if (_currentDeviceStatus != status)
                {
                    SendDeviceStatusInternal(status);

                    // Insert an action to callback to history manager
                    _deviceStatusUpdateAction?.Invoke(status);
                    _currentDeviceStatus = status;
                }

                SetFailoverScore(status, failoverScore);
            }
        }
    }

    public void ResendDeviceStatus()
    {
        lock (_deviceStatusChangeLock)
        {
            if (_currentDeviceStatus.HasValue)
            {
                SendDeviceStatusInternal(_currentDeviceStatus.Value);
            }
        }
    }

    public void ResendTypesAndStreams()
    {
        SendTypesAndStreams();
    }

    public void ResendLinks()
    {
        HealthMessageCreator.SendLinks(_healthMessageProcessor);
    }

    public LinkNode GetHealthLinkNode()
    {
        return HealthMessageCreator.GetComponentLink();
    }

    public void SetDeviceStatusUpdateAction(Action<DeviceStatus> action)
    {
        lock (_deviceStatusChangeLock)
        {
            _deviceStatusUpdateAction = action;
        }
    }

    public void SetDataSelectionCount(int count)
    {
        _dataSelectionCount = count;
    }

    public int GetDataSelectionItemCount()
    {
        return _dataSelectionCount;
    }

    /// <inheritdoc />
    public float GetFailoverScore()
    {
        return _failoverScore;
    }

    public DeviceStatus? GetDeviceStatus()
    {
        lock (_deviceStatusChangeLock)
        {
            return _currentDeviceStatus;
        }
    }

    public void ActivateDeviceStatusUpdates()
    {
        lock (_deviceStatusChangeLock)
        {
            _deviceStatusEnabled = true;
        }
    }

    public void UpdateDeviceStatusAndSuppress(DeviceStatus status)
    {
        lock (_deviceStatusChangeLock)
        {
            SendDeviceStatus(status);
            _deviceStatusEnabled = false;
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
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
            _heartBeatTimer?.Dispose();
            _heartBeatTimer = null;
            _stateChangeSemSlim?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private void SetFailoverScore(DeviceStatus status, float? failoverScore)
    {
        if (failoverScore.HasValue)
        {
            if (failoverScore < 0.0f || failoverScore > 100.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(failoverScore), "Value must be between 0.0 and 100.0");
            }

            _failoverScore = failoverScore.Value;
        }
        else
        {
            switch (status)
            {
                case DeviceStatus.Good:
                case DeviceStatus.ConnectedNoData:
                    _failoverScore = 100;
                    break;
                default:
                    _failoverScore = 0;
                    break;
            }
        }
    }

    private void SendDeviceStatusInternal(DeviceStatus status)
    {
        var (streamId, classification, value) = HealthMessageCreator.GetDeviceStatusData(DeviceStatusMapper.DeviceStatusEnumToString(status));
        _healthMessageProcessor?.WriteHealthValue(streamId, classification, value);
    }

    private void SendTypesAndStreams()
    {
        HealthMessageCreator.CreateAndSendHealthStructure(_healthMessageProcessor, _componentId, _componentType);
    }

    private Task InitializeInternalAsync()
    {
        SendTypesAndStreams();

        _isInitialized = true;
        _logger?.LogDebug("Health service initialized for {ComponentId}.", _componentId);

        return Task.CompletedTask;
    }

    private void ThrowIfNotInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("System health service must be initialized before sending messages.");
        }
    }

    private void SendHeartBeat(object state)
    {
        if (Interlocked.CompareExchange(ref _sendHeartBeatSync, 1, 0) == 0)
        {
            try
            {
                var (containerId, classification, eventData) = HealthMessageCreator.GetHeartBeatData(_heartBeatSpan);
                _healthMessageProcessor.WriteHealthValue(containerId, classification, eventData);
            }
            finally
            {
                Interlocked.Exchange(ref _sendHeartBeatSync, 0);
            }
        }
    }

    #endregion
}
