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
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Failover.Configuration;
using AdapterFramework.Data.Framework.Failover.Entities;
using AdapterFramework.Data.Framework.Failover.Interfaces;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.Failover.Tests")]
namespace AdapterFramework.Data.Framework.Failover;

public class FailoverEndpointManager : IFailoverEndpointManager
{
    private const string GroupPath = "groups/";
    private const string ForbiddenMessage = "The adapter has insufficient permissions. Check credentials in configurations and the failover server. {ErrorMessage}";
    private const int MinHeartbeatDelaySeconds = 1;

    private readonly ILogger _logger;
    private readonly TimeSpan _httpClientTimeout = TimeSpan.FromSeconds(30);
    private readonly IFailoverDataMessageProcessor _failoverDataMessageProcessor;
    private readonly IApplicationManifest _applicationManifest;
    private readonly IEdgeDataProtector _edgeDataProtector;
    private readonly SemaphoreSlim _stateChangeSemaphore = new(1, 1);
    private readonly ISerializer _serializer;
    private readonly string _sessionName;
    private readonly string _sessionId;
    private readonly int _pendingPrimaryRetries = 10;
    private readonly bool _fakeClient;

    private Action<FailoverRole, FailoverRole> _failoverRoleChangeAction;
    private Func<float> _failoverScoreFunc;
    private Action<DeviceStatus> _deviceStatusAction;
    private ClientFailoverConfiguration _failoverConfiguration;
    private IFailoverEndpointClient _httpClient;
    private Timer _heartbeatTimer;
    private int _sendingHeartbeat;
    private int _pendingPrimaryAttempts;
    private bool _reconnectMessageWritten;
    private bool _disposed;
    private bool _errorMessageWritten;
    private bool _started;

    public FailoverEndpointManager(ILogger logger, IEdgeDataProtector dataProtector, IApplicationManifest manifest,
        IFailoverDataMessageProcessor failoverDataMessageProcessor, ISerializer serializer)
    {
        _logger = logger;
        _edgeDataProtector = dataProtector;
        _applicationManifest = manifest;
        _failoverDataMessageProcessor = failoverDataMessageProcessor;
        _serializer = serializer;
        _sessionId = $"{_applicationManifest.HealthPrefix ?? string.Empty}{_applicationManifest.MachineName}.{_applicationManifest.ServiceName}";
        _sessionName = $"{_applicationManifest.MachineName} {_applicationManifest.ServiceName}";
    }

    /// <summary>
    /// Internal constructor used for testing purposes only.
    /// </summary>
    internal FailoverEndpointManager(ILogger logger, IEdgeDataProtector dataProtector, IApplicationManifest manifest,
        IFailoverDataMessageProcessor failoverDataMessageProcessor, ISerializer serializer, IFailoverEndpointClient client = null)
        : this(logger, dataProtector, manifest, failoverDataMessageProcessor, serializer)
    {
        if (client != null)
        {
            _fakeClient = true;
            _httpClient = client;
        }
    }

    public void Initialize(Action<FailoverRole, FailoverRole> failoverRoleChangeAction, Func<float> failoverScoreFunc,
        Action<DeviceStatus> deviceStatusAction)
    {
        _failoverRoleChangeAction = failoverRoleChangeAction;
        _failoverScoreFunc = failoverScoreFunc;
        _deviceStatusAction = deviceStatusAction;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }

        await _stateChangeSemaphore.WaitAsync(cancellationToken);
        _started = true;
        _logger.LogDebug("Starting failover endpoint manager.");

        try
        {
            if (_failoverConfiguration == null)
            {
                return;
            }

            if (!_fakeClient)
            {
                _httpClient = new FailoverEndpointClient(_failoverConfiguration, _edgeDataProtector, _applicationManifest, _logger, _httpClientTimeout);
            }

            await RegisterAndStartHeartbeatTimerAsync(cancellationToken);
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stateChangeSemaphore.WaitAsync(cancellationToken);

        try
        {
            if (_started)
            {
                await StopHeartbeatTimerAndUnregisterAsync(cancellationToken);
                if (_failoverConfiguration != null && _failoverConfiguration.Mode != FailoverMode.NotConfigured)
                {
                    _deviceStatusAction(DeviceStatus.Shutdown);
                }
            }

            _httpClient?.Dispose();
            _started = false;
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task UpdateConfigurationAsync(ConfigurationChangedEventArgs configurationChangeEvent, CancellationToken cancellationToken)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationChangeEvent, nameof(configurationChangeEvent));

        var oldFailoverConfiguration = configurationChangeEvent.OldValue as ClientFailoverConfiguration;
        var newFailoverConfiguration = configurationChangeEvent.NewValue as ClientFailoverConfiguration;

        if (newFailoverConfiguration == null || newFailoverConfiguration.Mode == FailoverMode.NotConfigured)
        {
            if (_started)
            {
                await StopAsync(cancellationToken);

                var oldRole = _failoverDataMessageProcessor.CurrentFailoverRole;
                if (oldRole != FailoverRole.Secondary)
                {
                    // reset the state to default role of Secondary since we no longer have a client failover config
                    _failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, _failoverDataMessageProcessor.LastDataProcessedTime);
                    _failoverRoleChangeAction.Invoke(oldRole, FailoverRole.Secondary);
                }

                _started = true;
            }

            _failoverConfiguration = null;
            return;
        }

        if (oldFailoverConfiguration != null && !oldFailoverConfiguration.Endpoint.EndsWith('/'))
        {
            oldFailoverConfiguration.Endpoint += "/";
        }

        if (!newFailoverConfiguration.Endpoint.EndsWith('/'))
        {
            newFailoverConfiguration.Endpoint += "/";
        }

        if (_started)
        {
            await HandleConfigurationChangeAsync(oldFailoverConfiguration, newFailoverConfiguration, cancellationToken);
        }
        else
        {
            _failoverConfiguration = newFailoverConfiguration;
        }
    }

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
            _httpClient?.Dispose();
            _stateChangeSemaphore.Dispose();
            _heartbeatTimer?.Dispose();
        }

        _disposed = true;
    }

    private static DateTime GetAdjustedLastDataProcessedTime(DateTime lastDataProcessedTime, TimeSpan failoverTimeout)
    {
        var offset = failoverTimeout / 2;
        if (DateTime.MinValue.Add(offset) > lastDataProcessedTime)
        {
            return lastDataProcessedTime;
        }

        return lastDataProcessedTime.Subtract(offset);
    }

    private async Task HandleConfigurationChangeAsync(ClientFailoverConfiguration oldFailoverConfiguration, ClientFailoverConfiguration newFailoverConfiguration, CancellationToken cancellationToken)
    {
        if (oldFailoverConfiguration == null || oldFailoverConfiguration.Mode == FailoverMode.NotConfigured)
        {
            await StopAsync(cancellationToken);
            _failoverConfiguration = newFailoverConfiguration;
            await StartAsync(cancellationToken);
        }
        else
        {
            if (oldFailoverConfiguration.Endpoint != newFailoverConfiguration.Endpoint ||
                oldFailoverConfiguration.ValidateEndpointCertificate != newFailoverConfiguration.ValidateEndpointCertificate)
            {
                await StopAsync(cancellationToken);
                _failoverConfiguration = newFailoverConfiguration;
                await StartAsync(cancellationToken);
            }
            else
            {
                if (oldFailoverConfiguration.UserName != newFailoverConfiguration.UserName || 
                    _edgeDataProtector.Unprotect(oldFailoverConfiguration.Password) != _edgeDataProtector.Unprotect(newFailoverConfiguration.Password) ||
                    oldFailoverConfiguration.ClientId != newFailoverConfiguration.ClientId ||
                    _edgeDataProtector.Unprotect(oldFailoverConfiguration.ClientSecret) != _edgeDataProtector.Unprotect(newFailoverConfiguration.ClientSecret))
                {
                    _httpClient.UpdateConfiguration(newFailoverConfiguration);
                }

                if (oldFailoverConfiguration.FailoverGroupId != newFailoverConfiguration.FailoverGroupId)
                {
                    await StopHeartbeatTimerAndUnregisterAsync(cancellationToken);
                    _failoverConfiguration = newFailoverConfiguration;
                    await RegisterAndStartHeartbeatTimerAsync(cancellationToken);
                }
                else if (oldFailoverConfiguration.FailoverTimeout != newFailoverConfiguration.FailoverTimeout)
                {
                    await StopHeatbeatTimerAsync();
                    _failoverConfiguration = newFailoverConfiguration;
                    StartHeartbeatTimer(TimeSpan.FromSeconds(MinHeartbeatDelaySeconds));
                }
                else
                {
                    _failoverConfiguration = newFailoverConfiguration;
                }
            }
        }
    }

    private async Task RegisterAndStartHeartbeatTimerAsync(CancellationToken cancellationToken)
    {
        var heartbeatDueTime = TimeSpan.Zero;

        if (await RegisterGroupAndSessionAsync(cancellationToken))
        {
            _deviceStatusAction(DeviceStatus.Good);

            // Send heartbeat immediately so current role can be confirmed
            await SendHeartbeatAsync(cancellationToken);

            // Delay DueTime to abide by minimum delay required between heartbeats
            heartbeatDueTime = TimeSpan.FromSeconds(MinHeartbeatDelaySeconds);
        }
        else
        {
            _deviceStatusAction(DeviceStatus.DeviceInError);
            _logger.LogError("Unable to register or connect to the failover endpoint at {EndpointUrl}." +
                " The operation will be retried every {HeartBeat}.", _httpClient.Uri, _failoverConfiguration.FailoverTimeout / 2);
        }

        StartHeartbeatTimer(heartbeatDueTime);
    }

    private async Task StopHeartbeatTimerAndUnregisterAsync(CancellationToken cancellationToken)
    {
        await StopHeatbeatTimerAsync();
        await UnregisterSessionAsync(cancellationToken);
    }

    private void StartHeartbeatTimer(TimeSpan heartbeatDueTime)
    {
        if (_heartbeatTimer == null)
        {
            _heartbeatTimer = new Timer(SendHeartbeatAsync, null, heartbeatDueTime, _failoverConfiguration.FailoverTimeout / 2);
        }
    }

    private async Task StopHeatbeatTimerAsync()
    {
        if (_heartbeatTimer != null)
        {
            await _heartbeatTimer.DisposeAsync();
            _heartbeatTimer = null;
        }
    }

    private async void SendHeartbeatAsync(object state)
    {
        if (Interlocked.CompareExchange(ref _sendingHeartbeat, 1, 0) == 0)
        {
            try
            {
                await SendHeartbeatAsync(CancellationToken.None);
            }
            finally
            {
                _sendingHeartbeat = 0;
            }
        }
    }

    private Task<EndpointResponse> SendMessageAsync(string requestUri, byte[] messageBody, CancellationToken cancellationToken, HttpVerb verb = HttpVerb.Post)
    {
        return _httpClient.SendMessageAsync(requestUri, messageBody, cancellationToken, verb);
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return;
        }

        var failoverRequest = new FailoverRequest
        {
            FailoverScore = _failoverScoreFunc(),
            LastDataProcessedTime = _failoverDataMessageProcessor.LastDataProcessedTime,
        };

        _logger.LogDebug("Sending heartbeat message to the endpoint {Endpoint}. Failover score: {FailoverScore}. LastDataProcessedTime: {LastDataProcessedTime:O}.",
            _httpClient.Uri, failoverRequest.FailoverScore, failoverRequest.LastDataProcessedTime);

        var result = await SendMessageAsync($"{GroupPath}{_failoverConfiguration.FailoverGroupId}/clientsessions/{_sessionId}/heartbeat",
            _serializer.Serialize(failoverRequest), cancellationToken);

        switch (result.ResponseStatus)
        {
            case ResponseStatusEnum.Success:
                {
                    await HandleFailoverEndpointResponseAsync(result, cancellationToken);
                    return;
                }

            case ResponseStatusEnum.NotFound:
                {
                    _deviceStatusAction(DeviceStatus.DeviceInError);
                    if (!_errorMessageWritten)
                    {
                        _errorMessageWritten = true;
                        _logger.LogWarning("Group or session could not be found on the failover endpoint. {Message} Reattempting to create group and/or session.",
                            result.Message);
                    }

                    await RegisterGroupAndSessionAsync(cancellationToken);
                    break;
                }

            case ResponseStatusEnum.Fail:
                {
                    _deviceStatusAction(DeviceStatus.DeviceInError);

                    if (!_errorMessageWritten)
                    {
                        _errorMessageWritten = true;
                        _logger.LogError("Could not send heartbeat to endpoint {Endpoint}. {ErrorMessage}", _httpClient.Uri, result.Message);
                    }

                    break;
                }

            default:
                {
                    if (!_errorMessageWritten)
                    {
                        _errorMessageWritten = true;
                        _logger.LogWarning("Unsuccessful result {Result} received as a result to heartbeat send operation to endpoint {Endpoint}. {Message}",
                        result.ResponseStatus, _httpClient.Uri, result.Message);
                    }

                    break;
                }
        }
    }

    private async Task HandleFailoverEndpointResponseAsync(EndpointResponse result, CancellationToken cancellationToken)
    {
        if (!DeserializePayload<FailoverMessage>(result.Message, out var message))
        {
            _logger.LogError("Unable to deserialize failover message.");
            return;
        }

        _logger.LogDebug("Heartbeat message sent. Response received from the failover endpoint: role: '{Role}' and lastDataProcessedTime: {LastDataProcessedTime:O}.",
            message.Role, message.LastDataProcessedTime);

        if (message.Role == FailoverRole.PendingPrimary)
        {
            if (_pendingPrimaryAttempts >= _pendingPrimaryRetries)
            {
                _logger.LogError("Failover did not assign primary role to the adapter after {NumAttempts}. Stopping sending primary confirmation.",
                    _pendingPrimaryRetries);
            }
            else
            {
                _pendingPrimaryAttempts++;
                _logger.LogInformation("Sending primary confirmation.");
                await SendHeartbeatAsync(cancellationToken);
            }
        }
        else
        {
            var oldRole = _failoverDataMessageProcessor.CurrentFailoverRole;

            var adjustedLastDataProcessedTime = GetAdjustedLastDataProcessedTime(message.LastDataProcessedTime, _failoverConfiguration.FailoverTimeout);

            _logger.LogDebug("Received lastDataProcessedTime was adjusted to: {Time:O}", adjustedLastDataProcessedTime);

            _failoverDataMessageProcessor.UpdateState(message.Role, adjustedLastDataProcessedTime);

            if (oldRole != message.Role)
            {
                _failoverRoleChangeAction.Invoke(oldRole, message.Role);
            }
        }

        _pendingPrimaryAttempts = 0;
        _deviceStatusAction(DeviceStatus.Good);
        _errorMessageWritten = false;
    }

    private async Task<bool> RegisterGroupAndSessionAsync(CancellationToken cancellationToken)
    {
        return await RegisterGroupAsync(cancellationToken) && await RegisterSessionAsync(cancellationToken);
    }

    private async Task<bool> RegisterGroupAsync(CancellationToken cancellationToken)
    {
        var groupConfiguration = new FailoverGroupRegistration()
        {
            Id = _failoverConfiguration.FailoverGroupId,
            FailoverTimeout = _failoverConfiguration.FailoverTimeout,
        };

        var response = await _httpClient.SendMessageAsync(GroupPath, _serializer.Serialize(groupConfiguration), cancellationToken);

        switch (response.ResponseStatus)
        {
            case ResponseStatusEnum.Success:
                {
                    _logger.LogInformation("Registered failover group {GroupId}.", _failoverConfiguration.FailoverGroupId);
                    _reconnectMessageWritten = false;
                    return true;
                }

            case ResponseStatusEnum.Fail:
                {
                    if (!_reconnectMessageWritten)
                    {
                        _logger.LogError("Unable to connect to {Endpoint}.", new Uri(new Uri(_failoverConfiguration.Endpoint), GroupPath).ToString());
                        _reconnectMessageWritten = true;
                    }

                    return false;
                }

            case ResponseStatusEnum.Conflict:
                {
                    response = await _httpClient.SendMessageAsync($"{GroupPath}{_failoverConfiguration.FailoverGroupId}", null, cancellationToken, HttpVerb.Get);
                    if (response.ResponseStatus == ResponseStatusEnum.Success)
                    {
                        if (!DeserializePayload<FailoverGroupRegistration>(response.Message, out var serverGroupConfiguration))
                        {
                            _logger.LogError("Unable to deserialize failover group information message.");
                            return false;
                        }

                        if (serverGroupConfiguration.FailoverTimeout != _failoverConfiguration.FailoverTimeout)
                        {
                            _logger.LogWarning("Server had a different failover timeout ({ServerTimeout}) than the configured one ({ClientTimeout})." +
                                               " Server timeout will be used.", serverGroupConfiguration.FailoverTimeout, _failoverConfiguration.FailoverTimeout);
                            _heartbeatTimer?.Change(TimeSpan.Zero, serverGroupConfiguration.FailoverTimeout / 2);
                            _failoverConfiguration.FailoverTimeout = serverGroupConfiguration.FailoverTimeout;
                        }

                        return true;
                    }

                    WriteGroupRegistrationErrorMessage(_failoverConfiguration.FailoverGroupId, response);
                    return false;
                }

            default:
                {
                    if (!_reconnectMessageWritten)
                    {
                        WriteGroupRegistrationErrorMessage(_failoverConfiguration.FailoverGroupId, response);
                        _logger.LogError("Unable to register group {Group} to endpoint {Endpoint}. {Message}", _failoverConfiguration.FailoverGroupId,
                            _failoverConfiguration.Endpoint, response.Message);
                        _reconnectMessageWritten = true;
                    }

                    return false;
                }
        }
    }

    private async Task<bool> RegisterSessionAsync(CancellationToken cancellationToken)
    {
        var registrationMessage = new FailoverSessionRegistration() { Id = _sessionId, Name = _sessionName };

        var response = await _httpClient.SendMessageAsync($"{GroupPath}{_failoverConfiguration.FailoverGroupId}/clientsessions",
            _serializer.Serialize(registrationMessage), cancellationToken);

        if (response.ResponseStatus == ResponseStatusEnum.Fail)
        {
            if (!_reconnectMessageWritten)
            {
                _logger.LogError("Unable to connect to {Endpoint}.", new Uri(new Uri(_failoverConfiguration.Endpoint),
                    $"{GroupPath}{_failoverConfiguration.FailoverGroupId}/clientsessions"));

                _reconnectMessageWritten = true;
            }

            return false;
        }

        if (response.ResponseStatus != ResponseStatusEnum.Success)
        {
            WriteSessionRegistrationErrorMessage(_failoverConfiguration.FailoverGroupId, response);
            _logger.LogError("Unable to register client {ClientId} to endpoint {Endpoint}. {Message}",
                _sessionId, _failoverConfiguration.Endpoint, response.Message);

            _reconnectMessageWritten = true;
            return false;
        }

        _logger.LogInformation("Added session {SessionId} to failover group {GroupId}.", _sessionId, _failoverConfiguration.FailoverGroupId);
        _reconnectMessageWritten = false;
        return true;
    }

    private async Task UnregisterSessionAsync(CancellationToken cancellationToken)
    {
        if (_failoverConfiguration == null || _failoverConfiguration.Mode == FailoverMode.NotConfigured)
        {
            return;
        }

        var response = await _httpClient.SendMessageAsync($"{GroupPath}{_failoverConfiguration.FailoverGroupId}/clientsessions/{_sessionId}",
            null, cancellationToken, HttpVerb.Delete);

        if (response.ResponseStatus != ResponseStatusEnum.Success)
        {
            if (!_reconnectMessageWritten)
            {
                _logger.LogError("Unable to connect to {ClientSessionPath}.", new Uri(new Uri(_failoverConfiguration.Endpoint),
                    $"{GroupPath}{_failoverConfiguration.FailoverGroupId}/clientsessions"));

                _reconnectMessageWritten = true;
            }
        }

        _reconnectMessageWritten = false;

        if (response.ResponseStatus != ResponseStatusEnum.Success)
        {
            WriteClientUnRegistrationErrorMessage(_failoverConfiguration.FailoverGroupId, _sessionId, response);
        }
        else
        {
            _logger.LogInformation("Unregistered session {SessionId}.", _sessionId);
        }
    }

    private void WriteGroupRegistrationErrorMessage(string failoverGroupId, EndpointResponse response)
    {
        switch (response.ResponseStatus)
        {
            case ResponseStatusEnum.BadRequest:
                _logger.LogError("The failover timeout is outside the allowed range for group id {GroupId}. {ErrorMessage}",
                    failoverGroupId, response.Message);
                break;
            case ResponseStatusEnum.Forbidden:
                _logger.LogError(ForbiddenMessage, response.Message);
                break;
            case ResponseStatusEnum.Conflict:
                _logger.LogError("The group id {GroupId} has already been registered with different configurations. {ErrorMessage}",
                    failoverGroupId, response.Message);
                break;
            case ResponseStatusEnum.Fail:
            default:
                _logger.LogError("The register request for group id {GroupId} failed. {ErrorMessage}", failoverGroupId, response.Message);
                break;
        }
    }

    private void WriteSessionRegistrationErrorMessage(string failoverGroupId, EndpointResponse response)
    {
        switch (response.ResponseStatus)
        {
            case ResponseStatusEnum.BadRequest:
                _logger.LogError("The failover server rejected the session registration to group {GroupId}. {ErrorMessage}",
                    failoverGroupId, response.Message);
                break;
            case ResponseStatusEnum.Forbidden:
                _logger.LogError(ForbiddenMessage, response.Message);
                break;
            case ResponseStatusEnum.NotFound:
                _logger.LogError("The group with id {GroupId} was not found on the failover server. {ErrorMessage}", failoverGroupId, response.Message);
                break;
            case ResponseStatusEnum.Fail:
            default:
                _logger.LogError("The session registration request to group id {GroupId} failed. {ErrorMessage}", failoverGroupId, response.Message);
                break;
        }
    }

    private void WriteClientUnRegistrationErrorMessage(string failoverGroupId, string sessionId, EndpointResponse response)
    {
        switch (response.ResponseStatus)
        {
            case ResponseStatusEnum.Forbidden:
                _logger.LogError(ForbiddenMessage, response.Message);
                break;
            case ResponseStatusEnum.NotFound:
                _logger.LogError("The group id {GroupdId} or session id {SessionId} was not found on the failover server. {ErrorMessage}",
                    failoverGroupId, sessionId, response.Message);
                break;
            case ResponseStatusEnum.Fail:
            default:
                _logger.LogError("Failed to unregister client with Id {SessionId}. {ErrorMessage}", sessionId, response.Message);
                break;
        }
    }

    private bool DeserializePayload<T>(string content, out T deserializedPayload) where T : class
    {
        deserializedPayload = null;

        try
        {
            deserializedPayload = _serializer.Deserialize<T>(content);
        }
        catch (Exception ex) when (ex is ArgumentNullException or JsonException)
        {
            _logger.LogError(ex, "Unable to deserialize content from endpoint {Endpoint}", _failoverConfiguration.Endpoint);
            return false;
        }

        return deserializedPayload != null;
    }
}
