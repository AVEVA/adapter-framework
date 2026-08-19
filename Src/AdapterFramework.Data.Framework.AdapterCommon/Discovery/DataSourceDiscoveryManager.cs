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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.AdapterCommon.Discovery.Extensions;
using AdapterFramework.Data.Framework.Extensions;
using static System.Net.HttpStatusCode;
using static AdapterFramework.Data.Framework.AdapterCommon.Discovery.Constants.DiscoveryConstants;

namespace AdapterFramework.Data.Framework.AdapterCommon.Discovery;

public class DataSourceDiscoveryManager<TDataSource, TSelection> : IDisposable, IDataSourceDiscoveryManager
    where TDataSource : class, IDataSourceConfiguration
    where TSelection : class, IDataSelectionConfiguration
{
    private readonly string _componentId;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly ILogger _logger;
    private readonly string _baseAddress;
    private readonly ConcurrentDictionary<string, DiscoveryState> _discoveryStateCollection = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Func<TDataSource> _getDataSourceFunction;
    private readonly Func<string, TDataSource, DataSourceDiscoveryService<TSelection>, CancellationToken, Task> _dataSourceDiscoveryFunction;
    private readonly Action<ConfigurationChangedEventArgs> _dataSelectionUpdateFunction;
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    private bool _disposed;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _dataSourceDiscoveryTask;
    private string _outstandingDiscoveryId;

    /// <summary>
    /// Instantiates a new instance of <see cref="DataSourceDiscoveryManager{TDataSource,TSelection}"/> class.
    /// </summary>
    /// <param name="componentId">ID of a component the class is created for.</param>
    /// <param name="baseAddress">Base address of the host application.</param>
    /// <param name="configurationProvider"><see cref="IConfigurationProvider"/>Configuration Provider instance.</param>
    /// <param name="logger"><see cref="ILogger"/>Logger instance.</param>
    /// <param name="dataSourceDiscoveryFunction">DataSource discovery callback function.</param>
    /// <param name="dataSelectionUpdateFunction">DataSelection discovery callback function.</param>
    /// <param name="getDataSourceFunction">Callback function to get DataSource from AdapterMainBase.</param>
    public DataSourceDiscoveryManager(string componentId, string baseAddress,
        IConfigurationProvider configurationProvider, ILogger logger,
        Func<string, TDataSource, DataSourceDiscoveryService<TSelection>, CancellationToken, Task> dataSourceDiscoveryFunction,
        Action<ConfigurationChangedEventArgs> dataSelectionUpdateFunction, Func<TDataSource> getDataSourceFunction)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(baseAddress, nameof(baseAddress));
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));
        ThrowHelper.ThrowIfArgumentNull(dataSourceDiscoveryFunction, nameof(dataSourceDiscoveryFunction));
        ThrowHelper.ThrowIfArgumentNull(dataSelectionUpdateFunction, nameof(dataSelectionUpdateFunction));
        ThrowHelper.ThrowIfArgumentNull(getDataSourceFunction, nameof(getDataSourceFunction));

        _componentId = componentId;
        _configurationProvider = configurationProvider;
        _logger = logger;
        _baseAddress = baseAddress + UriSeparator + RouteConstants.BaseConfigurationRoute + componentId + UriSeparator + CommonConstants.DiscoveriesConfigurationName + UriSeparator;
        _dataSourceDiscoveryFunction = dataSourceDiscoveryFunction;
        _dataSelectionUpdateFunction = dataSelectionUpdateFunction;
        _getDataSourceFunction = getDataSourceFunction;

        InitializeDiscoveryStateCollection();
    }

    public MvcResult GetDiscoveryResult(string discoveryId, DiscoveryOptions discoveryOptions)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryId, nameof(discoveryId));

        if (discoveryOptions != null)
        {
            if (discoveryOptions.Skip < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(discoveryOptions), InvalidOptionsSkipValue);
            }

            if (discoveryOptions.Count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(discoveryOptions), InvalidOptionsCountValue);
            }
        }

        if (TryGetDiscoveryResult<TSelection[]>(discoveryId, out var discoveryResult, out var errorResult))
        {
            if (discoveryOptions == null || (discoveryOptions.Skip == 0 && discoveryOptions.Count == 0))
            {
                return new MvcResult(OK, discoveryResult);
            }

            var start = discoveryOptions.Skip;
            var end = discoveryOptions.Count.Equals(0) ? discoveryResult.Length : Math.Min(start + discoveryOptions.Count, discoveryResult.Length);

            if (end > start)
            {
                return new MvcResult(OK, discoveryResult[start..end]);
            }
        }
        else
        {
            return errorResult;
        }

        return new MvcResult(NotFound, new RestApiErrorResponse(string.Format(_culture, DiscoveryResultNotFoundMessage, _componentId, discoveryId)));
    }

    public MvcResult DeleteDiscoveryResult(string discoveryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryId, nameof(discoveryId));

        lock (_lock)
        {
            var status = OK;
            if (!TryDeleteDiscoveryResultInternal(discoveryId, out var errorMessage))
            {
                status = NotFound;
            }
            else
            {
                if (!TrySaveDiscoveryStateConfiguration(out var saveErrorMessage))
                {
                    errorMessage = saveErrorMessage;
                }
            }

            var content = errorMessage == null ? null : new RestApiErrorResponse(errorMessage);
            return new MvcResult(status, content);
        }
    }

    public MvcResult DeleteDiscovery(string discoveryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryId, nameof(discoveryId));

        lock (_lock)
        {
            var status = OK;
            string errorMessage = null;
            var deleted = TryDeleteDiscoveryResultInternal(discoveryId, out var deleteErrorMessage);
            if (deleted)
            {
                _discoveryStateCollection.TryRemove(discoveryId, out _);
                if (!TrySaveDiscoveryStateConfiguration(out var saveErrorMessage))
                {
                    status = InternalServerError;
                    errorMessage = saveErrorMessage;
                }
            }
            else
            {
                status = NotFound;
                errorMessage = deleteErrorMessage;
            }

            var content = errorMessage == null ? null : new RestApiErrorResponse(errorMessage);
            return new MvcResult(status, content);
        }
    }

    public MvcResult DeleteDiscoveries()
    {
        lock (_lock)
        {
            var status = OK;
            var discoveryIds = _discoveryStateCollection.Keys;
            foreach (var discoveryId in discoveryIds)
            {
                TryDeleteDiscoveryResultInternal(discoveryId, out _);
            }

            _discoveryStateCollection.Clear();

            if (!TrySaveDiscoveryStateConfiguration(out var errorMessage))
            {
                status = InternalServerError;
            }

            var content = errorMessage == null ? null : new RestApiErrorResponse(errorMessage);
            return new MvcResult(status, content);
        }
    }

    public MvcResult StartDiscovery(DiscoveryState discoveryState, DiscoveryOptions discoveryOptions)
    {
        lock (_lock)
        {
            ThrowHelper.ThrowIfArgumentNull(discoveryState, nameof(discoveryState));
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryState.Id, nameof(discoveryState));

            if (_discoveryStateCollection.TryGetValue(discoveryState.Id, out _))
            {
                var tryGetErrorMessage = string.Format(_culture, DiscoveryIdAlreadyExists, discoveryState.Id);
                return new MvcResult(Conflict, new RestApiErrorResponse(tryGetErrorMessage));
            }

            if (!TryStartDiscovery(discoveryState, discoveryOptions, out var error))
            {
                _logger.LogWarning(error);
                return new MvcResult(BadRequest, new RestApiErrorResponse(error));
            }

            return new MvcResult(Accepted, discoveryState);
        }
    }

    public MvcResult CancelDiscovery(string discoveryId)
    {
        lock (_lock)
        {
            var status = OK;
            RestApiErrorResponse content = null;
            if (_discoveryStateCollection.TryGetValue(discoveryId, out var _))
            {
                if (!TryCancelDiscoveryInternal(discoveryId, out var errorMessage))
                {
                    status = Conflict;
                    content = new RestApiErrorResponse(errorMessage);
                }
            }
            else
            {
                status = NotFound;
                content = new RestApiErrorResponse(string.Format(_culture, DiscoveryStateNotFoundMessage, _componentId, discoveryId));
            }

            return new MvcResult(status, content);
        }
    }

    public MvcResult MergeWithDataSelection(string discoveryId, bool selected)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryId, nameof(discoveryId));

        if (!TryGetDiscoveryResult<TSelection[]>(discoveryId, out var discoveryResult, out var errorResult))
        {
            return errorResult;
        }

        if (!TryGetCurrentDataSelectionConfiguration(out var currentDataSelectionConfiguration, out errorResult))
        {
            return errorResult;
        }

        var mergedResult = DiscoveryUtilities.MergeConfigurations(currentDataSelectionConfiguration, discoveryResult, selected);
        var mergedResultArray = mergedResult.ToArray();

        if (!TrySaveNewDataSelectionConfiguration(discoveryId, mergedResultArray, out errorResult))
        {
            return errorResult;
        }

        try
        {
            _dataSelectionUpdateFunction(new ConfigurationChangedEventArgs(currentDataSelectionConfiguration, mergedResultArray));
        }
        catch (Exception e)
        {
            _logger.LogError(MergeWithDataSelectionLogException, discoveryId, _componentId, e.Message);
            return new MvcResult(InternalServerError, new RestApiErrorResponse(string.Format(_culture, MergeWithDataSelectionResponseException, discoveryId, _componentId)));
        }

        return new MvcResult(OK, new MergeOperationResult(mergedResultArray.Length, mergedResultArray.Length - currentDataSelectionConfiguration.Length));
    }

    public MvcResult GetDataSelectionDifference(string discoveryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryId, nameof(discoveryId));

        if (!TryGetDiscoveryResult<TSelection[]>(discoveryId, out var discoveryResult, out var errorResult))
        {
            return errorResult;
        }

        if (!TryGetCurrentDataSelectionConfiguration(out var currentDataSelectionConfiguration, out errorResult))
        {
            return errorResult;
        }

        var comparisonResult = DiscoveryUtilities.CompareConfigurations(currentDataSelectionConfiguration, discoveryResult);

        return new MvcResult(OK, comparisonResult);
    }

    public MvcResult GetDiscoveriesDifference(string discoveryIdA, string discoveryIdB)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryIdA, nameof(discoveryIdA));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryIdB, nameof(discoveryIdB));

        if (!TryGetDiscoveryResult<TSelection[]>(discoveryIdA, out var discoveryResultA, out var errorResult))
        {
            return errorResult;
        }

        if (!TryGetDiscoveryResult<TSelection[]>(discoveryIdB, out var discoveryResultB, out errorResult))
        {
            return errorResult;
        }

        var comparisonResult = DiscoveryUtilities.CompareConfigurations(discoveryResultA, discoveryResultB);

        return new MvcResult(OK, comparisonResult);
    }

    public MvcResult GetDiscoveryStates()
    {
        return new MvcResult(OK, _discoveryStateCollection.Values);
    }

    public MvcResult GetDiscoveryState(string discoveryId)
    {
        if (_discoveryStateCollection.TryGetValue(discoveryId, out var discoveryState))
        {
            return new MvcResult(OK, discoveryState);
        }

        return new MvcResult(NotFound, new RestApiErrorResponse(string.Format(_culture, DiscoveryStateNotFoundMessage, _componentId, discoveryId)));
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

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _disposed = true;
    }

    private static string GetScheduleId(DiscoveryOptions discoveryOptions) => discoveryOptions == null ? null : discoveryOptions.ScheduleId.IsNullOrEmpty() ? null : discoveryOptions.ScheduleId;

    private string GetResultUrl(DiscoveryState discoveryState) => _baseAddress + discoveryState.Id + UriSeparator + ResultKeyword;

    private bool TryGetDiscoveryResult<T>(string discoveryId, out T discoveryResult, out MvcResult errorResult) where T : class
    {
        errorResult = null;
        discoveryResult = null;

        if (_discoveryStateCollection.TryGetValue(discoveryId, out var discoveryState))
        {
            if (!_configurationProvider.TryGetDiscoveryResult(_componentId, discoveryState.Id, out discoveryResult, out var errors))
            {
                if (errors.IsNullOrEmpty())
                {
                    errorResult = new MvcResult(NotFound, new RestApiErrorResponse(string.Format(_culture, DiscoveryResultNotFoundMessage, _componentId, discoveryId)));
                    return false;
                }

                errorResult = new MvcResult(InternalServerError, new RestApiErrorResponse(string.Format(_culture, UnableToSerializeDiscoveryResult, _componentId, discoveryId, string.Join(Environment.NewLine, errors))));
                return false;
            }
        }
        else
        {
            errorResult = new MvcResult(NotFound, new RestApiErrorResponse(string.Format(_culture, DiscoveryResultNotFoundMessage, _componentId, discoveryId)));
            return false;
        }

        return true;
    }

    private bool TryGetCurrentDataSelectionConfiguration(out TSelection[] currentDataSelectionConfiguration, out MvcResult errorResult)
    {
        errorResult = null;

        if (!_configurationProvider.TryGetConfiguration(_componentId, CommonConstants.DataSelectionConfigurationName, out currentDataSelectionConfiguration, out var errors))
        {
            if (errors.IsNullOrEmpty())
            {
                currentDataSelectionConfiguration = Array.Empty<TSelection>();
                return true;
            }

            errorResult = new MvcResult(InternalServerError, new RestApiErrorResponse(string.Format(_culture, UnableToSerializeDataSelectionConfiguration, _componentId)));
            return false;
        }

        return true;
    }

    private bool TrySaveNewDataSelectionConfiguration<T>(string discoveryId, T newDataSelectionConfiguration, out MvcResult errorResult) where T : class
    {
        errorResult = null;

        if (!_configurationProvider.TrySaveConfiguration(_componentId, CommonConstants.DataSelectionConfigurationName, newDataSelectionConfiguration, out var errors))
        {
            errorResult = new MvcResult(InternalServerError, new RestApiErrorResponse(string.Format(_culture, UnableToSaveDataSelection, _componentId, discoveryId, string.Join(Environment.NewLine, errors))));
            return false;
        }

        return true;
    }

    private void InitializeDiscoveryStateCollection()
    {
        if (!_configurationProvider.TryGetConfiguration<DiscoveryState[]>(_componentId, CommonConstants.DiscoveriesConfigurationName, out var discoveryConfiguration, out var errors))
        {
            if (errors?.Count > 0)
            {
                _logger.LogWarning(InvalidDiscoveriesConfiguration, errors);
            }

            return;
        }

        foreach (var discoveryState in discoveryConfiguration)
        {
            if (!_discoveryStateCollection.TryAdd(discoveryState.Id, discoveryState))
            {
                _logger.LogWarning(FailedToAddDiscovery, discoveryState.Id);
            }
        }
    }

    private bool TryStartDiscovery(DiscoveryState discoveryState, DiscoveryOptions discoveryOptions, out string error)
    {
        error = string.Empty;

        if (_dataSourceDiscoveryTask == null || _dataSourceDiscoveryTask.IsCompleted)
        {
            if (!_discoveryStateCollection.TryAdd(discoveryState.Id, discoveryState))
            {
                error = string.Format(_culture, FailedToAddDiscovery, discoveryState.Id);
                return false;
            }

            _dataSourceDiscoveryTask?.Dispose();
            _cancellationTokenSource?.Dispose();

            _outstandingDiscoveryId = discoveryState.Id;
            _cancellationTokenSource = new CancellationTokenSource();
            _dataSourceDiscoveryTask = Task.Run(() => StartDataSourceDiscoveryAsync(discoveryState, discoveryOptions, _cancellationTokenSource.Token));

            return true;
        }

        error = string.Format(_culture, ExistingDiscoveryInProcess, _outstandingDiscoveryId);
        return false;
    }

    private async Task StartDataSourceDiscoveryAsync(DiscoveryState discoveryState, DiscoveryOptions discoveryOptions, CancellationToken cancellationToken)
    {
        try
        {
            discoveryState.OnStarted(DateTime.Now);

            var dataSourceConfiguration = _getDataSourceFunction.Invoke();
            if (dataSourceConfiguration == null)
            {
                var error = string.Format(_culture, CannotStartWithoutDataSource, discoveryState.Id);
                discoveryState.OnFailed(DateTime.Now, error);
                _logger.LogError(error);
                return;
            }

            _logger.LogInformation(StartingDiscovery, discoveryState.Id, discoveryState.Query);

            var dataSourceDiscoveryService = new DataSourceDiscoveryService<TSelection>(discoveryState);

            await _dataSourceDiscoveryFunction.Invoke(discoveryState.Query, dataSourceConfiguration, dataSourceDiscoveryService, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(CompletedDiscovery, discoveryState.Id);

            var discoveredResults = dataSourceDiscoveryService.DiscoveredItems;
            var discoveredConfiguration = discoveredResults.Values;

            if (discoveryState.AutoSelect)
            {
                _logger.LogInformation(DiscoveredItemsAreToBeAdded);
            }

            _configurationProvider.TryGetConfiguration<TSelection[]>(_componentId, CommonConstants.DataSelectionConfigurationName, out var originalConfiguration, out _);

            originalConfiguration ??= Array.Empty<TSelection>();

            var differences = DiscoveryUtilities.CompareConfigurations(originalConfiguration, discoveredConfiguration);
            var newConfiguration = DiscoveryUtilities.MergeConfigurations(originalConfiguration, discoveredConfiguration, discoveryState.AutoSelect, GetScheduleId(discoveryOptions));

            if (!_configurationProvider.TrySaveDiscoveryResult(_componentId, discoveryState.Id, discoveredResults.Values.ToArray(), out var errors))
            {
                discoveryState.OnFailed(DateTime.Now, string.Join(Environment.NewLine, errors));
                _logger.LogError(FailedToSaveDiscoveryResult, discoveryState.Id);
                return;
            }

            if (discoveryState.AutoSelect)
            {
                var newConfigurationArray = newConfiguration.ToArray();
                if (!_configurationProvider.TrySaveConfiguration(_componentId, CommonConstants.DataSelectionConfigurationName, newConfigurationArray, out errors))
                {
                    var message = string.Format(_culture, UnableToSaveDataSelection, _componentId, discoveryState.Id, string.Join(Environment.NewLine, errors));
                    discoveryState.OnFailed(DateTime.Now, message);
                    _logger.LogError(message);
                    return;
                }

                _dataSelectionUpdateFunction.Invoke(new ConfigurationChangedEventArgs(originalConfiguration, newConfigurationArray));
            }

            discoveryState.OnCompleted(DateTime.Now, discoveredResults.Count, differences.Count(), GetResultUrl(discoveryState));
        }
        catch (OperationCanceledException)
        {
            discoveryState.OnCanceled(DateTime.Now, DiscoveryCanceledMessage);
            _logger.LogWarning(DiscoveryCanceled, discoveryState.Id);
        }
        catch (Exception ex)
        {
            discoveryState.OnFailed(DateTime.Now, string.Format(_culture, DiscoveryFailedWithException, ex.GetExceptionTypeAndMessages()));
            _logger.LogError(ex, DiscoveryFailed, discoveryState.Id);
        }
        finally
        {
            if (!TrySaveDiscoveryStateConfiguration(out var errorMessage))
            {
                _logger.LogError(errorMessage);
            }

            _outstandingDiscoveryId = null;
        }
    }

    private bool TrySaveDiscoveryStateConfiguration(out string errorMessage)
    {
        var result = true;
        errorMessage = null;
        var discoveryStateArray = _discoveryStateCollection.IsEmpty ? Array.Empty<DiscoveryState>() : [.. _discoveryStateCollection.Values];
        if (!_configurationProvider.TrySaveConfiguration(_componentId, CommonConstants.DiscoveriesConfigurationName, discoveryStateArray, out var errors))
        {
            result = false;
            errorMessage = string.Format(_culture, SaveDiscoveryStatesFailed, string.Join(Environment.NewLine, errors));
        }

        return result;
    }

    private bool TryDeleteDiscoveryResultInternal(string discoveryId, out string errorMessage)
    {
        var result = true;
        errorMessage = null;

        if (_discoveryStateCollection.TryGetValue(discoveryId, out var discoveryState))
        {
            if (!TryCancelDiscoveryInternal(discoveryId, out _))
            {
                discoveryState.Errors = DiscoveryResultDeleted;
                discoveryState.ResultUri = null;
            }

            _configurationProvider.DeleteDiscoveryResult(_componentId, discoveryState.Id);
        }
        else
        {
            result = false;
            errorMessage = string.Format(_culture, DiscoveryIdNotFound, discoveryId);
        }

        return result;
    }

    private bool TryCancelDiscoveryInternal(string discoveryId, out string errorMessage)
    {
        var discoveryCancelled = false;
        errorMessage = null;
        if (_outstandingDiscoveryId != null && _outstandingDiscoveryId.Equals(discoveryId, StringComparison.OrdinalIgnoreCase))
        {
            if (_dataSourceDiscoveryTask is { IsCompleted: false })
            {
                _cancellationTokenSource.Cancel();
                _dataSourceDiscoveryTask.GetAwaiter().GetResult();

                _dataSourceDiscoveryTask.Dispose();
                _dataSourceDiscoveryTask = null;

                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;

                discoveryCancelled = true;
            }
            else
            {
                errorMessage = string.Format(_culture, "The discovery operation with ID {0} for component {1} is no longer active.", discoveryId, _componentId);
            }
        }
        else
        {
            errorMessage = string.Format(_culture, "No outstanding discovery with ID {0} for component {1} to be cancelled.", discoveryId, _componentId);
        }

        return discoveryCancelled;
    }
}
