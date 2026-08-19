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
using AdapterFramework.Data.Framework.Common.Scheduling;

namespace AdapterFramework.Data.Framework.AdapterCommon.Scheduling;

internal class AdapterScheduleManager<TSelection> : IDisposable
    where TSelection : IDataSelectionConfiguration
{
    private readonly ConcurrentDictionary<string, AdapterSchedule<TSelection>> _schedulesDict = new ConcurrentDictionary<string, AdapterSchedule<TSelection>>(StringComparer.InvariantCultureIgnoreCase);
    private readonly ILogger _logger;
    private readonly Func<string, IReadOnlyList<TSelection>, CancellationToken, Task> _sampleDataCallback;
    private readonly Scheduler _scheduler;
    private readonly object _stateChangeLock = new object();

    private bool _disposed;

    public AdapterScheduleManager(ILogger logger, Func<string, IReadOnlyList<TSelection>, CancellationToken, Task> sampleDataCallback)
    {
        _logger = logger;
        _sampleDataCallback = sampleDataCallback;
        _scheduler = new Scheduler(logger);
    }

    public bool Enabled { get; private set; } = true;

    public void ProcessDataSelectionChanges(TSelection[] dataSelectionConfiguration)
    {
        lock (_stateChangeLock)
        {
            if (dataSelectionConfiguration == null)
            {
                foreach (var schedule in _schedulesDict.Values)
                {
                    schedule.Stop();
                    schedule.TryUpdateSamplingGroup(null);
                }
            }
            else if (dataSelectionConfiguration is IScanDataSelectionConfiguration[] scanDataSelectionConfigurations)
            {
                // Construct the all sampling groups
                var allSamples = new Dictionary<string, HashSet<TSelection>>(StringComparer.InvariantCultureIgnoreCase);
                foreach (var dataItem in scanDataSelectionConfigurations)
                {
                    if (!dataItem.Selected)
                    {
                        continue;
                    }

                    if (dataItem.ScheduleId == null)
                    {
                        continue;
                    }

                    if (allSamples.TryGetValue(dataItem.ScheduleId, out var itemList))
                    {
                        itemList.Add((TSelection)dataItem);
                    }
                    else
                    {
                        allSamples[dataItem.ScheduleId] = new HashSet<TSelection>() 
                        { 
                            (TSelection)dataItem,
                        };
                    }
                }

                // Find the sampling groups removed by data selection update
                var scheduleIdsToRemove = new List<string>();
                foreach (var id in _schedulesDict.Keys)
                {
                    if (!allSamples.TryGetValue(id, out _))
                    {
                        scheduleIdsToRemove.Add(id);
                    }
                }

                // Stop and remove the schedules removed by data selection update
                foreach (var id in scheduleIdsToRemove)
                {
                    if (_schedulesDict.TryGetValue(id, out var schedule))
                    {
                        schedule.Stop();
                        schedule.TryUpdateSamplingGroup(null);
                    }
                }

                // Check the newly created sample groups for add or update
                foreach (var (scheduleId, samplingGroup) in allSamples)
                {
                    // Check if the schedule is already created
                    if (_schedulesDict.TryGetValue(scheduleId, out var schedule))
                    {
                        // Schedule already created, try updating the sampling group
                        if (schedule.TryUpdateSamplingGroup(samplingGroup))
                        {
                            // New changes in the sampling group, restart the job
                            if (schedule.CanStart && Enabled)
                            {
                                schedule.Stop();
                                schedule.Start(); 
                            }
                        }
                    }
                    else
                    {
                        // Create a new schedule
                        _logger.LogWarning($"No schedule configuration associated with schedule ID {scheduleId}. Data Sampling will start on this schedule when schedule configuration is in place. ");
                        var newSchedule = new AdapterSchedule<TSelection>(_scheduler, _sampleDataCallback);
                        newSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = scheduleId, Period = null, Offset = null });
                        newSchedule.TryUpdateSamplingGroup(samplingGroup);
                        _schedulesDict.TryAdd(scheduleId, newSchedule);
                    }
                }
            }
        }
    }

    public void ProcessSchedulesConfigurationChanges(ScheduleConfiguration[] schedulesConfiguration)
    {
        lock (_stateChangeLock)
        {
            if (schedulesConfiguration == null)
            {
                foreach (var schedule in _schedulesDict.Values)
                {
                    schedule.Stop();
                    schedule.TryUpdateConfiguration(null);
                }
            }
            else
            {
                // Store the configurations to a dictionary for fast access
                var schedules = new Dictionary<string, ScheduleConfiguration>(StringComparer.InvariantCultureIgnoreCase);
                foreach (var config in schedulesConfiguration)
                {
                    schedules.Add(config.Id, config);
                }

                // Find the sampling groups removed by schedule configuration update
                var scheduleIdsToRemove = new List<string>();
                foreach (var id in _schedulesDict.Keys)
                {
                    if (!schedules.TryGetValue(id, out _))
                    {
                        scheduleIdsToRemove.Add(id);
                    }
                }

                // Stop and remove the scheduled jobs removed by schedule configuration update
                foreach (var id in scheduleIdsToRemove)
                {
                    if (_schedulesDict.TryGetValue(id, out var schedule))
                    {
                        schedule.Stop();
                        schedule.TryUpdateConfiguration(null);
                    }
                }

                // Check the configurations for add or update
                foreach (var config in schedules.Values)
                {
                    // Check if the schedule is already created
                    if (_schedulesDict.TryGetValue(config.Id, out var schedule))
                    {
                        // Schedule already created, try updating the configuration
                        if (schedule.TryUpdateConfiguration(config))
                        {
                            // New changes in the configuration, restart the job
                            if (schedule.CanStart && Enabled)
                            {
                                schedule.Stop();
                                schedule.Start();
                            }
                        }
                    }
                    else
                    {
                        // Create a new schedule
                        var newSchedule = new AdapterSchedule<TSelection>(_scheduler, _sampleDataCallback);
                        newSchedule.TryUpdateConfiguration(config);
                        _schedulesDict.TryAdd(config.Id, newSchedule);
                    }
                }
            }
        }
    }

    public void Disable()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_stateChangeLock)
        {
            foreach (var schedule in _schedulesDict.Values)
            {
                schedule.Stop();
            }

            Enabled = false;
        }
    }

    public void Enable()
    {
        if (Enabled)
        {
            return;
        }

        lock (_stateChangeLock)
        {
            foreach (var schedule in _schedulesDict.Values)
            {
                if (schedule.CanStart)
                {
                    schedule.Start();
                }
            }

            Enabled = true;
        }
    }

    public void ClearAllSchedules()
    {
        lock (_stateChangeLock)
        {
            foreach (var schedule in _schedulesDict.Values)
            {
                schedule.Stop();
            }

            _schedulesDict.Clear();
        }
    }

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
            _scheduler?.Dispose();
        }

        _disposed = true;
    }

    #endregion
}
