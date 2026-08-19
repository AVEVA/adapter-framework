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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery.Constants;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public class AutomaticHistoryRecoveryProcessor<TDataSource, TSelection> : HistoryRecoveryProcessorBase<TDataSource, TSelection>, IAutomaticHistoryRecoveryProcessor
    where TDataSource : class, IDataSourceConfiguration
    where TSelection : class, IDataSelectionConfiguration
{
    private const string UnableToReadConfigurationError = "Unable to read existing {IntervalsToProcess} configuration. Only new intervals will be added: {Error}";
    private const string BadFormatIntervalWarning = "The following interval to recover is ill-formed and will be removed: {Interval}.";
    private const string IntervalIsNullError = "The interval is null and cannot be used for history recovery. The interval will be removed.";

    private const int InitialRetryDelay = 10_000;
    private const int InitialRecoveryDelay = 10_000;
    private const int MaxRetryDelay = 300_000;
    private const int JitterRange = 10_000;

    private readonly IConfigurationProvider _configurationProvider;
    private readonly Func<Interval, TDataSource, IReadOnlyList<TSelection>, CancellationToken, Task> _automaticHistoryRecoveryFunction;
    private readonly Func<DateTime?> _getLastReadTimeFunction;
    private readonly TimeSpan _maxAutomaticHistoryIntervalLength = TimeSpan.FromDays(HistoryRecoveryConstants.MaxAutomaticHistoryIntervalLengthDays);
    private readonly HashSet<DeviceStatus> _automaticHistoryRecoveryIntervalStates;
    private readonly object _lock = new();

    private IntervalList _intervals;
    private bool _disposed;
    private bool _intervalInProgress;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _currentRecoveryTask;
    private DeviceStatus? _lastDeviceStatus;
    private int _currentRetryDelay;
    private FailoverMode _currentFailoverMode;
    private FailoverRole _currentFailoverRole;

    public AutomaticHistoryRecoveryProcessor(string componentId,
        ILogger logger,
        IConfigurationProvider configurationProvider,
        HealthServiceBase healthService,
        HashSet<DeviceStatus> automaticHistoryRecoveryIntervalStates,
        Func<Interval, TDataSource, IReadOnlyList<TSelection>, CancellationToken, Task> automaticHistoryRecoveryFunction,
        Func<DateTime?> getLastReadTimeFunction) : base(componentId, logger)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(healthService, nameof(healthService));
        ThrowHelper.ThrowIfArgumentNullOrEmpty(automaticHistoryRecoveryIntervalStates, nameof(automaticHistoryRecoveryIntervalStates));
        ThrowHelper.ThrowIfArgumentNull(automaticHistoryRecoveryFunction, nameof(automaticHistoryRecoveryFunction));
        ThrowHelper.ThrowIfArgumentNull(getLastReadTimeFunction, nameof(getLastReadTimeFunction));

        if (automaticHistoryRecoveryIntervalStates.Contains(DeviceStatus.Good))
        {
            throw new ArgumentException($"Automatic history recovery intervals set cannot contain device status {DeviceStatus.Good}.", nameof(automaticHistoryRecoveryIntervalStates));
        }

        _configurationProvider = configurationProvider;
        healthService.SetDeviceStatusUpdateAction(OnDeviceStatusUpdated);
        _automaticHistoryRecoveryFunction = automaticHistoryRecoveryFunction;
        _getLastReadTimeFunction = getLastReadTimeFunction;
        _automaticHistoryRecoveryIntervalStates = automaticHistoryRecoveryIntervalStates;
    }

    public override void Start(IDataSourceConfiguration dataSourceConfiguration)
    {
        lock (_lock)
        {
            UpdateDataSourceConfiguration(dataSourceConfiguration);
            _currentRetryDelay = InitialRetryDelay;

            LoadIntervalsConfiguration();
            Started = true;

            if (_lastDeviceStatus != null)
            {
                OnDeviceStatusUpdated(_lastDeviceStatus.Value);
            }
        }
    }

    public override void Stop()
    {
        lock (_lock)
        {
            Started = false;
            CancelCurrentRecoveryTask();
        }
    }

    public void ProcessIntervalsToRecoverChanges(Interval[] intervals)
    {
        lock (_lock)
        {
            if (intervals != null)
            {
                throw new InvalidOperationException("Only the deletion functionality is currently supported.");
            }

            CancelCurrentRecoveryTask();

            _intervals = new IntervalList();
            _intervalInProgress = false;
        }
    }

    public void UpdateFailoverMode(FailoverMode failoverMode)
    {
        lock (_lock)
        {
            var oldFailoverMode = _currentFailoverMode;
            _currentFailoverMode = failoverMode;

            if (_currentFailoverRole == FailoverRole.Secondary)
            {
                if (oldFailoverMode == FailoverMode.NotConfigured && _currentFailoverMode != FailoverMode.NotConfigured)
                {
                    CancelRecoveryAndCloseInterval();
                }
                else if (oldFailoverMode != FailoverMode.NotConfigured && _currentFailoverMode == FailoverMode.NotConfigured)
                {
                    if (Started && _lastDeviceStatus != null)
                    {
                        OnDeviceStatusUpdated(_lastDeviceStatus.Value);
                    }
                }
            }
        }
    }

    public void UpdateFailoverRole(FailoverRole failoverRole)
    {
        lock (_lock)
        {
            var oldFailoverRole = _currentFailoverRole;
            _currentFailoverRole = failoverRole;

            if (_currentFailoverMode != FailoverMode.NotConfigured)
            {
                if (oldFailoverRole == FailoverRole.Primary && _currentFailoverRole == FailoverRole.Secondary)
                {
                    CancelRecoveryAndCloseInterval();
                }
                else if (oldFailoverRole == FailoverRole.Secondary && _currentFailoverRole == FailoverRole.Primary)
                {
                    if (Started && _lastDeviceStatus != null)
                    {
                        OnDeviceStatusUpdated(_lastDeviceStatus.Value);
                    }
                }
            }
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

        CancelCurrentRecoveryTask();
        _disposed = true;
    }

    private static bool IsIntervalValid(Interval interval)
    {
        if (interval is null)
        {
            return false;
        }

        return interval.StartTime != null && interval.EndTime != null;
    }

    private static int AdvanceRetryDelay(int currentDelay)
    {
        var random = new Random();
        var jitter = random.Next(-JitterRange, JitterRange);

        currentDelay = (currentDelay * 2) + jitter;

        if (currentDelay > MaxRetryDelay)
        {
            currentDelay = MaxRetryDelay;
        }

        return currentDelay > InitialRetryDelay ? currentDelay : InitialRetryDelay;
    }

    private bool IsAutomaticHistoryRecoveryIntervalState(DeviceStatus deviceStatus)
    {
        return _automaticHistoryRecoveryIntervalStates.Contains(deviceStatus);
    }

    private void EnforceMaxIntervalLength(Interval interval)
    {
        if (interval == null)
        {
            return;
        }

        interval.EndTime ??= DateTime.UtcNow;

        if (interval.StartTime is null || interval.EndTime - interval.StartTime > _maxAutomaticHistoryIntervalLength)
        {
            interval.StartTime = interval.EndTime - _maxAutomaticHistoryIntervalLength;
        }

        if (interval.LastReadTime != null && interval.EndTime - interval.LastReadTime > _maxAutomaticHistoryIntervalLength)
        {
            interval.LastReadTime = interval.EndTime - _maxAutomaticHistoryIntervalLength;
        }
    }

    private async Task RecoverNextIntervalAsync(Interval interval, CancellationToken cancellationToken)
    {
        if (IsIntervalValid(interval))
        {
            try
            {
                await Task.Delay(InitialRecoveryDelay, cancellationToken);

                Logger.LogDebug("Beginning history recovery for interval from {StartTime} to {EndTime}. Last read time: {LastReadTime}.", interval.StartTime, interval.EndTime, interval.LastReadTime);

                _currentRecoveryTask = _automaticHistoryRecoveryFunction(interval, DataSourceConfiguration, DataSelectionItems, cancellationToken);
                await _currentRecoveryTask;

                Logger.LogInformation("Finished performing history recovery for interval from {StartTime} to {EndTime}.", interval.StartTime, interval.EndTime);

                _intervals.Remove(interval);
                SaveIntervalsConfiguration();
                _currentRetryDelay = InitialRetryDelay;
            }
            catch (TaskCanceledException)
            {
                Logger.LogDebug("History recovery for interval from {StartTime} to {EndTime} has been canceled.", interval.StartTime, interval.EndTime);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to recover history for interval from {StartTime} to {EndTime}. This interval will be retried.", interval.StartTime, interval.EndTime);

                await Task.Delay(_currentRetryDelay, cancellationToken);
                _currentRetryDelay = AdvanceRetryDelay(_currentRetryDelay);
            }

            if (_intervals.Count > 0)
            {
                var nextInterval = _intervals.GetFirstInterval();
                if (nextInterval is null)
                {
                    _intervals.Remove(nextInterval);
                    Logger.LogWarning(IntervalIsNullError);
                }
                else
                {
                    _ = Task.Run(() => RecoverNextIntervalAsync(nextInterval, cancellationToken), cancellationToken);
                }
            }
        }
        else
        {
            _intervals.Remove(interval);
            Logger.LogError("An attempt to recover the following invalid interval was made. This interval will be removed: {StartTime} to {EndTime}.", interval.StartTime, interval.EndTime);
        }
    }

    private void OnDeviceStatusUpdated(DeviceStatus deviceStatus)
    {
        lock (_lock)
        {
            _lastDeviceStatus = deviceStatus;
            Logger.LogDebug("Device status is updated to {DeviceStatus}.", deviceStatus);

            if (!IsEnabled())
            {
                return;
            }

            if (deviceStatus == DeviceStatus.Good)
            {
                CloseCurrentInterval();

                if (_intervals.Count > 0)
                {
                    var firstInterval = _intervals.GetFirstInterval();
                    if (firstInterval is null)
                    {
                        _intervals.Remove(firstInterval);
                        Logger.LogWarning(IntervalIsNullError);
                    }
                    else
                    {
                        _cancellationTokenSource = new CancellationTokenSource();
                        var cancellationToken = _cancellationTokenSource.Token;
                        _ = Task.Run(() => RecoverNextIntervalAsync(firstInterval, cancellationToken), cancellationToken);
                    }
                }
            }
            else if (IsAutomaticHistoryRecoveryIntervalState(deviceStatus))
            {
                CancelCurrentRecoveryTask();

                if (deviceStatus == DeviceStatus.Shutdown && _currentFailoverMode != FailoverMode.NotConfigured)
                {
                    CloseCurrentInterval();
                }
                else
                {
                    if (!_intervalInProgress)
                    {
                        var startTime = DateTime.UtcNow;
                        DateTime? lastReadTime = null;

                        try
                        {
                            lastReadTime = _getLastReadTimeFunction();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Unable to get {PropertyName} from the adapter component.", nameof(Interval.LastReadTime));
                        }

                        CreateInterval(startTime, null, lastReadTime);
                        var lastInterval = _intervals.GetLastInterval();
                        if (lastInterval is null)
                        {
                            _intervals.Remove(lastInterval);
                            Logger.LogWarning(IntervalIsNullError);
                        }
                        else
                        {
                            PersistInterval(lastInterval);
                            _intervalInProgress = true;
                        }
                    }
                }
            }
        }
    }

    private void CreateInterval(DateTime? startTime, DateTime? endTime = null, DateTime? lastReadTime = null)
    {
        var newInterval = new Interval
        {
            StartTime = startTime,
            LastReadTime = lastReadTime,
            EndTime = endTime,
        };

        _intervals.Add(newInterval);

        Logger.LogDebug("A new interval to recover has been added. Start time: {StartTime}.", startTime);
    }

    private void PersistInterval(Interval intervalToUpdate)
    {
        if (intervalToUpdate == null)
        {
            return;
        }

        if (intervalToUpdate.StartTime is null || _intervals.Contains(intervalToUpdate.StartTime))
        {
            CreateInterval(intervalToUpdate.StartTime, intervalToUpdate.EndTime, intervalToUpdate.LastReadTime);
        }
        else
        {
            var targetInterval = _intervals.FindIntervalByStartTime(intervalToUpdate.StartTime);
            _intervals.AddOrUpdate(targetInterval, intervalToUpdate.EndTime, intervalToUpdate.LastReadTime);
        }

        SaveIntervalsConfiguration();
    }

    private void SaveIntervalsConfiguration()
    {
        if (!_configurationProvider.TrySaveConfiguration(ComponentId, CommonConstants.IntervalsToRecoverConfigurationName, _intervals.ToArray(), out var errors))
        {
            Logger.LogError("Failed to save {IntervalsToRecover}. Automatic history recovery will be disabled: {Error}", CommonConstants.IntervalsToRecoverConfigurationName, errors);
            Started = false;
        }
    }

    private void LoadIntervalsConfiguration()
    {
        _intervals = new IntervalList();

        if (_configurationProvider.TryGetConfiguration<Interval[]>(ComponentId, CommonConstants.IntervalsToRecoverConfigurationName, out var intervals, out var errors))
        {
            if (intervals.IsNullOrEmpty())
            {
                return;
            }

            Array.Sort(intervals, new IntervalComparer());

            var nullEndTimeFound = false;
            foreach (var interval in intervals)
            {
                if (interval == null)
                {
                    continue;
                }

                if (interval.EndTime != null)
                {
                    if (interval.StartTime is null)
                    {
                        Logger.LogWarning(BadFormatIntervalWarning, interval);
                        continue;
                    }

                    EnforceMaxIntervalLength(interval);
                    PersistInterval(interval);
                }
                else if (!nullEndTimeFound && interval.StartTime != null)
                {
                    nullEndTimeFound = true;
                    _intervalInProgress = true;
                    PersistInterval(interval);
                }
                else
                {
                    Logger.LogWarning(BadFormatIntervalWarning, interval);
                }
            }
        }
        else if (!errors.IsNullOrEmpty())
        {
            Logger.LogWarning(UnableToReadConfigurationError, CommonConstants.IntervalsToRecoverConfigurationName, errors);
        }
    }

    private void CloseCurrentInterval()
    {
        if (!_intervalInProgress)
        {
            return;
        }

        var interval = _intervals.GetLastInterval();
        if (interval is null)
        {
            _intervals.Remove(interval);
            Logger.LogWarning(IntervalIsNullError);
        }
        else
        {
            interval.EndTime = DateTime.UtcNow;
            EnforceMaxIntervalLength(interval);

            _intervals.AddOrUpdate(interval);
            PersistInterval(interval);
        }

        _intervalInProgress = false;
    }

    private void CancelCurrentRecoveryTask()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_currentRecoveryTask != null && _currentRecoveryTask.Status != TaskStatus.RanToCompletion)
            {
                _currentRecoveryTask.GetAwaiter().GetResult();
            }
        }
        catch (TaskCanceledException) { }
        catch
        {
            // ignored
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
        }
    }

    private bool IsEnabled()
    {
        return Started && 
               (_currentFailoverMode == FailoverMode.NotConfigured ||
               (_currentFailoverMode != FailoverMode.NotConfigured && _currentFailoverRole == FailoverRole.Primary));
    }

    private void CancelRecoveryAndCloseInterval()
    {
        if (Started)
        {
            CancelCurrentRecoveryTask();
            CloseCurrentInterval();
        }
    }
}
