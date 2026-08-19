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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Common;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery.Extensions;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Extensions;
using static System.Net.HttpStatusCode;
using static AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery.Constants.HistoryRecoveryConstants;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public class OnDemandHistoryRecoveryProcessor<TDataSource, TSelection> : HistoryRecoveryProcessorBase<TDataSource, TSelection>, IOnDemandHistoryRecoveryProcessor
    where TDataSource : class, IDataSourceConfiguration
    where TSelection : class, IDataSelectionConfiguration
{
    private const string ProcessorNotEnabledString = "On-demand history recovery can be initiated only when DataCollection mode is set to HistoryOnly mode.";

    private readonly string _componentType;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IEdgeDataProtector _edgeDataProtector;
    private readonly IInstrumentedMessageProcessor _messageProcessor;
    private readonly OmfVersion _omfVersion;
    private readonly HealthServiceBase _healthService;
    private readonly Func<HistoryRecoveryDetails, TDataSource, IReadOnlyList<TSelection>, IAdapterHistoryRecoveryService, CancellationToken, Task> _onDemandHistoryRecoveryFunction;
    private readonly object _lock = new();
    private readonly object _ctsLock = new();
    private readonly ConcurrentDictionary<string, HistoryRecoveryState> _historyRecoveryStateCollection = new(StringComparer.OrdinalIgnoreCase);
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    private HistoryRecoveryDetails _historyRecoveryDetails;
    private Task _historyRecoveryTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private HistoryRecoveryAdapterMessageProcessor _historyRecoveryAdapterMessageProcessor;
    private bool _disposed;

    public OnDemandHistoryRecoveryProcessor(string componentId,
        string componentType,
        ILogger logger,
        IConfigurationProvider configurationProvider,
        IEdgeDataProtector edgeDataProtector,
        IInstrumentedMessageProcessor messageProcessor,
        HealthServiceBase healthService,
        Func<HistoryRecoveryDetails, TDataSource, IReadOnlyList<TSelection>, IAdapterHistoryRecoveryService, CancellationToken, Task> onDemandHistoryRecoveryFunction,
        OmfVersion omfVersion = OmfVersion.Omf12) : base(componentId, logger)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentType, nameof(componentType));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(edgeDataProtector, nameof(edgeDataProtector));
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));
        ThrowHelper.ThrowIfArgumentNull(healthService, nameof(healthService));
        ThrowHelper.ThrowIfArgumentNull(onDemandHistoryRecoveryFunction, nameof(onDemandHistoryRecoveryFunction));

        _componentType = componentType;
        _configurationProvider = configurationProvider;
        _edgeDataProtector = edgeDataProtector;
        _messageProcessor = messageProcessor;
        _omfVersion = omfVersion;
        _healthService = healthService;
        _onDemandHistoryRecoveryFunction = onDemandHistoryRecoveryFunction;

        InitializeOnDemandHistoryRecoveryStateCollection();
    }

    public string ActiveRecoveryId { get; private set; }

    public override void Start(IDataSourceConfiguration dataSourceConfiguration)
    {
        lock (_lock)
        {
            UpdateDataSourceConfiguration((TDataSource)dataSourceConfiguration);
            Started = true;
        }
    }

    public override void Stop()
    {
        lock (_lock)
        {
            CancelRecovery();
            Started = false;
        }
    }

    public MvcResult GetHistoryRecoveryState(string historyRecoveryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(historyRecoveryId, nameof(historyRecoveryId));

        if (_historyRecoveryStateCollection.TryGetValue(historyRecoveryId, out var value))
        {
            return new MvcResult(OK, value);
        }

        return new MvcResult(NotFound, new RestApiErrorResponse(string.Format(_culture, HistoryRecoveryStateNotFoundMessage, ComponentId, historyRecoveryId)));
    }

    public MvcResult GetHistoryRecoveryStates()
    {
        return new MvcResult(OK, _historyRecoveryStateCollection.Values);
    }

    public MvcResult StartOnDemandHistoryRecovery(HistoryRecoveryState historyRecoveryState)
    {
        ThrowHelper.ThrowIfArgumentNull(historyRecoveryState, nameof(historyRecoveryState));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(historyRecoveryState.Id, nameof(historyRecoveryState));
        lock (_lock)
        {
            if (!Started)
            {
                return new MvcResult(NotFound, ProcessorNotEnabledString);
            }

            if (DataSourceConfiguration == null)
            {
                return new MvcResult(BadRequest, string.Format(_culture, CannotStartWithoutDataSource, historyRecoveryState.Id));
            }

            if (_historyRecoveryStateCollection.TryGetValue(historyRecoveryState.Id, out _))
            {
                var tryGetErrorMessage = string.Format(_culture, HistoryRecoveryIdAlreadyExists, historyRecoveryState.Id);
                return new MvcResult(Conflict, new RestApiErrorResponse(tryGetErrorMessage));
            }

            if (!TryStartHistoryRecovery(historyRecoveryState, out var error))
            {
                Logger.LogWarning(error);
                return new MvcResult(Conflict, new RestApiErrorResponse(error));
            }

            return new MvcResult(Accepted, historyRecoveryState);
        }
    }

    public MvcResult CancelHistoryRecovery(string historyRecoveryId)
    {
        lock (_lock)
        {
            if (string.Equals(historyRecoveryId, ActiveRecoveryId, StringComparison.OrdinalIgnoreCase))
            {
                CancelRecovery();
                return TrySaveHistoryRecoveryStateConfigurationMvcOkOrServerError();
            }

            if (_historyRecoveryStateCollection.ContainsKey(historyRecoveryId))
            {
                var errorMessage = string.Format(_culture, "History recovery with ID {0} cannot be canceled because it is not in progress.", historyRecoveryId);
                return new MvcResult(Conflict, new RestApiErrorResponse(errorMessage));
            }

            return new MvcResult(NotFound, new RestApiErrorResponse(string.Format(CultureInfo.InvariantCulture, HistoryRecoveryIdNotFound, historyRecoveryId)));
        }
    }

    public MvcResult ResumeHistoryRecovery(string historyRecoveryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(historyRecoveryId, nameof(historyRecoveryId));
        lock (_lock)
        {
            if (!Started)
            {
                return new MvcResult(NotFound, ProcessorNotEnabledString);
            }

            if (DataSourceConfiguration == null)
            {
                return new MvcResult(BadRequest, string.Format(_culture, CannotStartWithoutDataSource, historyRecoveryId));
            }

            if (!_historyRecoveryStateCollection.TryGetValue(historyRecoveryId, out var historyRecoveryState))
            {
                var errorMessage = string.Format(_culture, HistoryRecoveryIdNotFound, historyRecoveryId);
                return new MvcResult(NotFound, new RestApiErrorResponse(errorMessage));
            }

            if (!(historyRecoveryState.Status == OperationStatus.Canceled || historyRecoveryState.Status == OperationStatus.Failed))
            {
                var errorMessage = string.Format(_culture, "History recovery with ID {0} cannot be resumed because it is not canceled or failed.", historyRecoveryId);
                return new MvcResult(Conflict, new RestApiErrorResponse(errorMessage));
            }

            if (historyRecoveryState.Checkpoint != null)
            {
                historyRecoveryState.StartTime = historyRecoveryState.Checkpoint;
            }

            if (!TryStartHistoryRecovery(historyRecoveryState, out var error))
            {
                Logger.LogWarning(error);
                return new MvcResult(Conflict, new RestApiErrorResponse(error));
            }

            return new MvcResult(Accepted, historyRecoveryState);
        }
    }

    public MvcResult DeleteHistoryRecoveries()
    {
        lock (_lock)
        {
            CancelRecovery();
            _historyRecoveryStateCollection.Clear();
            return TrySaveHistoryRecoveryStateConfigurationMvcOkOrServerError();
        }
    }

    public MvcResult DeleteHistoryRecovery(string historyRecoveryId)
    {
        lock (_lock)
        {
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(historyRecoveryId, nameof(historyRecoveryId));

            if (string.Equals(historyRecoveryId, ActiveRecoveryId, StringComparison.OrdinalIgnoreCase))
            {
                CancelRecovery();
            }

            if (_historyRecoveryStateCollection.TryRemove(historyRecoveryId, out _))
            {
                return TrySaveHistoryRecoveryStateConfigurationMvcOkOrServerError();
            }

            return new MvcResult(NotFound, new RestApiErrorResponse(string.Format(CultureInfo.InvariantCulture, HistoryRecoveryIdNotFound, historyRecoveryId)));
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        CancelRecovery();
        if (!TrySaveHistoryRecoveryStateConfiguration(out var errorMessage))
        {
            Logger.LogError(errorMessage);
        }

        _disposed = true;
    }

    private bool TryStartHistoryRecovery(HistoryRecoveryState historyRecoveryState, out string error)
    {
        error = string.Empty;

        if (ActiveRecoveryId == null || _historyRecoveryTask.IsCompleted)
        {
            _historyRecoveryStateCollection[historyRecoveryState.Id] = historyRecoveryState;

            ActiveRecoveryId = historyRecoveryState.Id;
            _cancellationTokenSource = new CancellationTokenSource();
            _historyRecoveryDetails = new HistoryRecoveryDetails(historyRecoveryState.Id,
                    historyRecoveryState.StartTime.GetValueOrDefault(),
                    historyRecoveryState.EndTime ?? DateTime.UtcNow);

            var dataSource = DataSourceConfiguration;
            var dataSelection = DataSelectionItems;
            if (dataSelection == null)
            {
                error = string.Format(_culture, MissingDataSelection);
                return false;
            }

            _historyRecoveryTask = Task.Run(() => StartOnDemandHistoryRecoveryAsync(historyRecoveryState, dataSource, dataSelection, _cancellationTokenSource.Token));

            return true;
        }

        error = string.Format(_culture, ExistingHistoryRecoveryInProcess, ActiveRecoveryId);
        return false;
    }

    private async Task StartOnDemandHistoryRecoveryAsync(HistoryRecoveryState historyRecoveryState, TDataSource dataSource, TSelection[] dataSelection, CancellationToken ct)
    {
        try
        {
            historyRecoveryState.OnStarted(_historyRecoveryDetails.StartTime, _historyRecoveryDetails.EndTime, dataSelection.Count(x => x.Selected));
            TrySaveHistoryRecoveryStateConfiguration(out _);

            Logger.LogInformation(StartingHistoryRecovery, historyRecoveryState.Id);

            _historyRecoveryAdapterMessageProcessor = new HistoryRecoveryAdapterMessageProcessor(_messageProcessor, _omfVersion);
            var adapterHistoryRecoveryService = new AdapterHistoryRecoveryService(historyRecoveryState, Logger, _configurationProvider, _historyRecoveryAdapterMessageProcessor, _edgeDataProtector, _componentType, ComponentId, _healthService);
            _historyRecoveryAdapterMessageProcessor.SetEventCountUpdateAction(adapterHistoryRecoveryService.UpdateRecoveredEvents);
            adapterHistoryRecoveryService.SetStreamIdPrefix(DataSourceConfiguration);
            _messageProcessor.SetStreamIdPrefix(adapterHistoryRecoveryService.StreamIdPrefix);

            await _onDemandHistoryRecoveryFunction.Invoke(_historyRecoveryDetails, dataSource, dataSelection, adapterHistoryRecoveryService, ct);

            Logger.LogInformation(CompletedHistoryRecovery, historyRecoveryState.Id);

            historyRecoveryState.OnCompleted();
        }
        catch (OperationCanceledException)
        {
            historyRecoveryState.OnCanceled(HistoryRecoveryCanceledMessage);
            Logger.LogWarning(HistoryRecoveryCanceled, historyRecoveryState.Id);
        }
        catch (Exception ex)
        {
            historyRecoveryState.OnFailed(string.Format(_culture, HistoryRecoveryFailedWithException, ex.GetExceptionTypeAndMessages()));
            Logger.LogError(ex, HistoryRecoveryFailedWithException, historyRecoveryState.Id);
        }
        finally
        {
            if (!TrySaveHistoryRecoveryStateConfiguration(out var errorMessage))
            {
                Logger.LogError(errorMessage);
            }

            CleanUp();
        }
    }

    private void CancelRecovery()
    {
        lock (_ctsLock)
        {
            _cancellationTokenSource?.Cancel();
        }

        if (_historyRecoveryTask != null)
        {
            _historyRecoveryTask?.GetAwaiter().GetResult();
            _historyRecoveryTask?.Dispose();
            _historyRecoveryTask = null;
        }

        CleanUp();
    }

    private void CleanUp()
    {
        lock (_ctsLock)
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        ActiveRecoveryId = null;
    }

    private MvcResult TrySaveHistoryRecoveryStateConfigurationMvcOkOrServerError()
    {
        if (TrySaveHistoryRecoveryStateConfiguration(out var errors))
        {
            return new MvcResult(OK);
        }

        Logger.LogError(FailedToSaveHistoryRecoveryResult, errors);
        return new MvcResult(InternalServerError, new RestApiErrorResponse(errors));
    }

    private bool TrySaveHistoryRecoveryStateConfiguration(out string errorMessage)
    {
        var result = true;
        errorMessage = null;
        if (!_configurationProvider.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName, _historyRecoveryStateCollection.Values, out var errors))
        {
            result = false;
            errorMessage = string.Format(_culture, SaveHistoryRecoveryStatesFailed, string.Join(Environment.NewLine, errors));
        }

        return result;
    }

    private void InitializeOnDemandHistoryRecoveryStateCollection()
    {
        if (!_configurationProvider.TryGetConfiguration<HistoryRecoveryState[]>(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
                out var historyRecoveryConfiguration, out var errors))
        {
            if (errors?.Count > 0)
            {
                Logger.LogWarning(InvalidHistoryRecoveriesConfiguration, errors);
            }

            return;
        }

        foreach (var historyRecovery in historyRecoveryConfiguration)
        {
            if (!_historyRecoveryStateCollection.TryAdd(historyRecovery.Id, historyRecovery))
            {
                Logger.LogWarning(FailedToAddHistoryRecovery, historyRecovery.Id);
            }
        }

        foreach (var historyRecovery in _historyRecoveryStateCollection.Values)
        {
            if (historyRecovery.Status == OperationStatus.Active)
            {
                historyRecovery.Status = OperationStatus.Failed;
                historyRecovery.Errors = "History recovery was terminated before it was completed.";
            }
        }

        if (!TrySaveHistoryRecoveryStateConfiguration(out var saveErrors))
        {
            Logger.LogError(SaveHistoryRecoveryStatesFailed, saveErrors);
        }
    }
}
