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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Failover.Configuration;
using AdapterFramework.Data.Framework.Failover.Health;

namespace AdapterFramework.Data.Framework.Failover;

public class FailoverManager : IFailoverManager
{
    public const int BadToGoodTimeMultiplier = 10;
    public const int StartupToGoodTimeMultiplier = 2;

    private readonly ILogger _logger;
    private readonly IApplicationManifest _applicationManifest;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IDiagnosticsMessageProcessor _diagnosticsMessageProcessor;
    private readonly IHealthMessageProcessor _healthMessageProcessor;
    private readonly IFailoverDataMessageProcessor _failoverDataMessageProcessor;
    private readonly IFailoverEndpointManager _failoverEndpointManager;
    private readonly ConcurrentDictionary<string, IFailoverService> _components;
    private readonly ConcurrentDictionary<string, DateTime> _componentsUnavailable;

    private readonly ConcurrentDictionary<string, Func<FailoverMode, FailoverMode, Task>> _failoverModeChangeCallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Func<FailoverRole, FailoverRole, Task>> _failoverRoleChangeCallbacks = new(StringComparer.OrdinalIgnoreCase);

    private FailoverMode _supportedFailoverModes;
    private FailoverDiagnosticsService _failoverDiagnosticsService;
    private FailoverHealthService _failoverHealthService;
    private ClientFailoverConfiguration _currentConfiguration;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _endpointRegistrationTask;
    private volatile bool _starting;
    private volatile bool _initialized;
    private volatile bool _started;
    private bool _disposed;
    private float _failoverScore;

    #region Public Constructor

    public FailoverManager(ILogger logger, IApplicationManifest applicationManifest, IConfigurationProvider configurationProvider,
        IDiagnosticsMessageProcessor diagnosticsMessageProcessor, IHealthMessageProcessor healthMessageProcessor,
        IFailoverDataMessageProcessor failoverDataMessageProcessor, IFailoverEndpointManager failoverEndpointManager)
    {
        _logger = logger;
        _applicationManifest = applicationManifest;
        _configurationProvider = configurationProvider;
        _diagnosticsMessageProcessor = diagnosticsMessageProcessor;
        _healthMessageProcessor = healthMessageProcessor;
        _failoverDataMessageProcessor = failoverDataMessageProcessor;
        _failoverEndpointManager = failoverEndpointManager;
        _components = new(StringComparer.OrdinalIgnoreCase);
        _componentsUnavailable = new(StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Public Methods

    public void AddComponentHealthService(string componentId, IFailoverService healthService)
    {
        _components.AddOrUpdate(componentId, healthService, (_, _) => healthService);
    }

    public void RemoveComponentHealthService(string componentId)
    {
        _components.TryRemove(componentId, out _);
        _componentsUnavailable.TryRemove(componentId, out _);
    }

    public void Initialize(FailoverMode supportedFailoverModes)
    {
        _supportedFailoverModes = supportedFailoverModes;
        _failoverEndpointManager.Initialize(FailoverRoleChangeHandler, CalculateFailoverScore, SendDeviceStatusCallback);
        _initialized = true;
    }

    public void RegisterFailoverModeChangeCallback(string componentId, Func<FailoverMode, FailoverMode, Task> failoverModeChangeCallback)
    {
        try
        {
            ThrowHelper.ThrowIfArgumentNull(failoverModeChangeCallback, nameof(failoverModeChangeCallback));

            _logger.LogDebug("Registered {ComponentId} to failover mode change callbacks.", componentId);
            _failoverModeChangeCallbacks.TryAdd(componentId, failoverModeChangeCallback);

            if (_failoverDataMessageProcessor.CurrentFailoverMode != FailoverMode.NotConfigured)
            {
                InvokeFailoverModeChangeCallback(failoverModeChangeCallback, FailoverMode.NotConfigured, _failoverDataMessageProcessor.CurrentFailoverMode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register failover mode change callback.");
        }
    }

    public void UnregisterFailoverModeChangeCallback(string componentId)
    {
        _logger.LogDebug("Unregistered {ComponentId} from failover mode change callbacks.", componentId);
        _failoverModeChangeCallbacks.TryRemove(componentId, out _);
    }

    public void RegisterFailoverRoleChangeCallback(string componentId, Func<FailoverRole, FailoverRole, Task> failoverRoleChangeCallback)
    {
        try
        {
            ThrowHelper.ThrowIfArgumentNull(failoverRoleChangeCallback, nameof(failoverRoleChangeCallback));

            _logger.LogDebug("Registered {ComponentId} to failover role change callbacks.", componentId);
            _failoverRoleChangeCallbacks.TryAdd(componentId, failoverRoleChangeCallback);

            if (_failoverDataMessageProcessor.CurrentFailoverRole == FailoverRole.Primary)
            {
                InvokeFailoverRoleChangeCallback(failoverRoleChangeCallback, FailoverRole.Secondary, _failoverDataMessageProcessor.CurrentFailoverRole);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register failover role change callback.");
        }
    }

    public void UnregisterFailoverRoleChangeCallback(string componentId)
    {
        _logger.LogDebug("Unregistered {ComponentId} from failover role change callbacks.", componentId);
        _failoverRoleChangeCallbacks.TryRemove(componentId, out _);
    }

    public void ResendHealthAndDiagnostics()
    {
        _failoverHealthService?.ResendTypesAndStreams();
        _failoverHealthService?.ResendDeviceStatus();
        _failoverDiagnosticsService?.ResendTypesAndStreams();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        _starting = true;
        _started = true;

        if (!_configurationProvider.TryGetConfiguration<ClientFailoverConfiguration>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ClientFailoverFacetName,
            out var configuration, out var getErrors))
        {
            if (!getErrors.IsNullOrEmpty())
            {
                await StartHealthAndDiagnosticsAsync(DeviceStatus.DeviceInError, new ClientFailoverConfiguration());
                _logger.LogWarning(EdgeSystemConstants.ConfigurationInvalidMessage, EdgeSystemConstants.ClientFailoverFacetName, getErrors);
            }

            _starting = false;
            return;
        }

        await UpdateConfigurationInternalAsync(new ConfigurationChangedEventArgs(null, configuration), cancellationToken);

        if (!_configurationProvider.TryGetConfiguration<FailoverState>(EdgeSystemConstants.SystemComponentId,
            EdgeSystemConstants.FailoverStateFacetName, out var lastFailoverState, out getErrors))
        {
            lastFailoverState = new FailoverState();
            if (!getErrors.IsNullOrEmpty())
            {
                _logger.LogWarning(EdgeSystemConstants.ConfigurationInvalidMessage, EdgeSystemConstants.ClientFailoverFacetName, getErrors);
                _failoverHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
            }
        }

        if (lastFailoverState.AdapterState == AdapterState.Running)
        {
            // Handle ungraceful shutdown or non-existing failover state
            _failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, lastFailoverState.LastDataProcessedTime);
        }
        else
        {
            _failoverDataMessageProcessor.UpdateState(lastFailoverState.Role, lastFailoverState.LastDataProcessedTime);
        }

        SaveFailoverStateOnChange(AdapterState.Running);
        await _failoverEndpointManager.StartAsync(cancellationToken);
        _starting = false;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        CancelOngoingEndpointRegistrationTask();

        await _failoverEndpointManager.StopAsync(cancellationToken);

        if (_currentConfiguration != null)
        {
            SaveFailoverStateOnChange(AdapterState.Shutdown);

            _failoverHealthService.SendDeviceStatus(DeviceStatus.Shutdown);

            await _failoverHealthService.StopAsync();
            await _failoverDiagnosticsService.StopAsync();
        }

        _started = false;
    }

    public void UpdateFailoverConfiguration(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationChangeEvent, nameof(configurationChangeEvent));

        if (_initialized && _started)
        {
            CancelOngoingEndpointRegistrationTask();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new();

            _endpointRegistrationTask = Task.Run(() =>
                UpdateConfigurationInternalAsync(configurationChangeEvent, _cancellationTokenSource.Token));
        }
    }

    public ICollection<string> ValidateFailoverConfiguration(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        var errors = new List<string>();
        if (configurationChangeEvent == null)
        {
            errors.Add("The configuration change event is null.");
            return errors;
        }

        if (configurationChangeEvent.NewValue is ClientFailoverConfiguration configuration)
        {
            var currentFailoverMode = configuration.Mode;
            if (!_supportedFailoverModes.HasFlag(currentFailoverMode))
            {
                errors.Add($"{currentFailoverMode} failover mode is not supported. Supported failover modes: {string.Join(',', _supportedFailoverModes)}.");
            }
        }

        return errors;
    }

    public FailoverState GetCurrentFailoverState()
    {
        if (_supportedFailoverModes == FailoverMode.NotConfigured)
        {
            return null;
        }

        return new FailoverState()
        {
            Role = _failoverDataMessageProcessor.CurrentFailoverRole,
            LastDataProcessedTime = _failoverDataMessageProcessor.LastDataProcessedTime,
            FailoverScore = _failoverScore,
            AdapterState = AdapterState.Running,
        };
    }

    #endregion

    #region Disposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        _cancellationTokenSource?.Cancel();
        _endpointRegistrationTask?.GetAwaiter().GetResult();
        _cancellationTokenSource?.Dispose();

        _failoverHealthService?.Dispose();
        _failoverDiagnosticsService?.Dispose();

        _disposed = true;
    }

    #endregion

    #region Private methods

    private async Task StartHealthAndDiagnosticsAsync(DeviceStatus deviceStatus, ClientFailoverConfiguration configuration)
    {
        InitializeHealthDiagnosticsServices(configuration);

        await _failoverHealthService.InitializeAsync();
        await _failoverHealthService.StartAsync();
        await _failoverDiagnosticsService.InitializeAsync();
        await _failoverDiagnosticsService.StartAsync();

        _failoverHealthService.SendDeviceStatus(deviceStatus);
    }

    private void FailoverRoleChangeHandler(FailoverRole oldRole, FailoverRole newRole)
    {
        try
        {
            SaveFailoverStateOnChange(AdapterState.Running);
            if (_failoverRoleChangeCallbacks.IsEmpty())
            {
                // if we have yet to register a component we should still log messages for failover role changes
                _logger.LogInformation("Failover role changed from {OldRole} to {NewRole}.", oldRole, newRole);
            }

            foreach (var callback in _failoverRoleChangeCallbacks.Values)
            {
                InvokeFailoverRoleChangeCallback(callback, oldRole, newRole);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle failover role change.");
        }
    }

    private async Task UpdateConfigurationInternalAsync(ConfigurationChangedEventArgs configurationChangeEvent, CancellationToken cancellationToken)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationChangeEvent, nameof(configurationChangeEvent));

        if (configurationChangeEvent.OldValue == null && configurationChangeEvent.NewValue == null)
        {
            return;
        }

        try
        {
            _currentConfiguration = configurationChangeEvent.NewValue as ClientFailoverConfiguration;

            if (configurationChangeEvent.OldValue == null && configurationChangeEvent.NewValue != null)
            {
                // Received new configuration -> start services
                await _failoverEndpointManager.StartAsync(cancellationToken);
                await StartHealthAndDiagnosticsAsync(DeviceStatus.Starting, _currentConfiguration);
            }

            if (_currentConfiguration != null)
            {
                _failoverHealthService.SendClientConfiguration(_currentConfiguration);
                _failoverHealthService.SendDeviceStatus(DeviceStatus.Good);
            }
            else
            {
                UpdateFailoverMode(FailoverMode.NotConfigured);
                await _failoverEndpointManager.UpdateConfigurationAsync(configurationChangeEvent, cancellationToken);

                _failoverHealthService.ResetFailoverAsset();
                _failoverHealthService.SendDeviceStatus(DeviceStatus.NotConfigured);
                _failoverDiagnosticsService.ResetFailoverState();

                await _failoverHealthService.StopAsync();
                await _failoverDiagnosticsService.StopAsync();

                // Delete the last state
                _configurationProvider.DeleteConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.FailoverStateFacetName);

                return;
            }

            await _failoverEndpointManager.UpdateConfigurationAsync(configurationChangeEvent, cancellationToken);

            if (!_starting && configurationChangeEvent.OldValue != null)
            {
                _failoverHealthService.ResendLinks();
                _failoverDiagnosticsService.ResendLinks();
            }

            UpdateFailoverMode(_currentConfiguration.Mode);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client failover group update has been canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception occurred while updating client failover group.");
        }
    }

    private void UpdateFailoverMode(FailoverMode newMode)
    {
        var oldFailoverMode = _failoverDataMessageProcessor.CurrentFailoverMode;
        _failoverDataMessageProcessor.UpdateMode(newMode);

        if (oldFailoverMode != newMode)
        {
            _componentsUnavailable.Clear();
            foreach (var callback in _failoverModeChangeCallbacks.Values)
            {
                InvokeFailoverModeChangeCallback(callback, oldFailoverMode, newMode);
            }
        }
    }

    private void SaveFailoverStateOnChange(AdapterState adapterState)
    {
        var failoverStateToSave = new FailoverState()
        {
            Role = _failoverDataMessageProcessor.CurrentFailoverRole,
            LastDataProcessedTime = _failoverDataMessageProcessor.LastDataProcessedTime,
            FailoverScore = _failoverScore,
            AdapterState = adapterState,
        };

        _failoverDiagnosticsService?.SendFailoverState(failoverStateToSave);

        if (!_configurationProvider.TrySaveConfiguration(EdgeSystemConstants.SystemComponentId,
            EdgeSystemConstants.FailoverStateFacetName, failoverStateToSave, out var saveErrors))
        {
            _logger.LogError(EdgeSystemConstants.FailedToSaveConfigurationMessage, EdgeSystemConstants.FailoverStateFacetName, saveErrors);
        }
    }

    private void SendDeviceStatusCallback(DeviceStatus deviceStatus)
    {
        _failoverHealthService?.SendDeviceStatus(deviceStatus);
    }

    private float CalculateFailoverScore()
    {
        var newScore = 0f;
        var adapterState = AdapterState.Running;

        if (_failoverDataMessageProcessor.CurrentFailoverMode == FailoverMode.Cold &&
                      _failoverDataMessageProcessor.CurrentFailoverRole != FailoverRole.Primary)
        {
            var numberOfAvailableComponents = 0;
            foreach (var (id, _) in _components)
            {
                if (_componentsUnavailable.TryGetValue(id, out var componentUnavailabeUntil))
                {
                    if (DateTime.UtcNow > componentUnavailabeUntil)
                    {
                        numberOfAvailableComponents++;
                    }
                }
                else
                {
                    var failoverTimeout = _currentConfiguration?.FailoverTimeout ?? TimeSpan.Zero;
                    _componentsUnavailable[id] = DateTime.UtcNow.Add(failoverTimeout * StartupToGoodTimeMultiplier);
                }
            }

            newScore = _components.IsEmpty ? 0 : (float)numberOfAvailableComponents / _components.Count;
            adapterState = AdapterState.Shutdown;
        }
        else
        {
            var scoreAndWeights = new List<(float Health, int Weights)>();
            long totalSelections = 0;

            foreach (var (id, healthService) in _components)
            {
                var score = healthService.GetFailoverScore();
                var selectCount = healthService.GetDataSelectionItemCount();

                scoreAndWeights.Add((score, selectCount));
                totalSelections += selectCount;

                if (_failoverDataMessageProcessor.CurrentFailoverMode == FailoverMode.Cold)
                {
                    var deviceStatus = healthService.GetDeviceStatus();
                    if (deviceStatus == DeviceStatus.DeviceInError || deviceStatus == null)
                    {
                        var failoverTimeout = _currentConfiguration?.FailoverTimeout ?? TimeSpan.Zero;
                        _componentsUnavailable[id] = DateTime.UtcNow.Add(failoverTimeout * BadToGoodTimeMultiplier);
                    }
                    else
                    {
                        _componentsUnavailable.TryRemove(id, out _);
                    }
                }
            }

            if (totalSelections == 0)
            {
                newScore = 0;
            }
            else
            {
                foreach (var (score, weights) in scoreAndWeights)
                {
                    newScore += score * weights / totalSelections;
                }
            }
        }

        var oldScore = _failoverScore;
        newScore = Math.Clamp(newScore, 0, 100);
        _failoverScore = newScore;
        
        if (oldScore != newScore)
        {
            SaveFailoverStateOnChange(adapterState);
        }

        return newScore;
    }

    private void InitializeHealthDiagnosticsServices(ClientFailoverConfiguration clientConfiguration)
    {
        var failoverState = GetCurrentFailoverState();

        _failoverHealthService = new FailoverHealthService(_healthMessageProcessor, _logger, _applicationManifest, clientConfiguration);

        _failoverDiagnosticsService = new FailoverDiagnosticsService(_diagnosticsMessageProcessor, failoverState, _logger,
            _failoverHealthService.GetHealthLinkNode(), _applicationManifest);
    }

    private void InvokeFailoverRoleChangeCallback(Func<FailoverRole, FailoverRole, Task> callback, FailoverRole oldRole, FailoverRole newRole)
    {
        try
        {
            callback.Invoke(oldRole, newRole);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception occurred while invoking failover role change callback");
        }
    }

    private void InvokeFailoverModeChangeCallback(Func<FailoverMode, FailoverMode, Task> callback, FailoverMode oldMode, FailoverMode newMode)
    {
        try
        {
            callback.Invoke(oldMode, newMode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected exception occurred while invoking failover mode change callback");
        }
    }

    private void CancelOngoingEndpointRegistrationTask()
    {
        if (_cancellationTokenSource != null && !_endpointRegistrationTask.IsCompleted)
        {
            _cancellationTokenSource.Cancel();
            _endpointRegistrationTask.GetAwaiter().GetResult();
        }
    }

    #endregion
}
