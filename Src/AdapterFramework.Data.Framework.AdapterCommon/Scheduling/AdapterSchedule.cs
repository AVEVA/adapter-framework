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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Common.Scheduling;

namespace AdapterFramework.Data.Framework.AdapterCommon.Scheduling;

internal class AdapterSchedule<TSelection> where TSelection : IDataSelectionConfiguration
{
    private readonly Scheduler _scheduler;
    private readonly Func<string, IReadOnlyList<TSelection>, CancellationToken, Task> _sampleDataCallback;
    private ScheduleConfiguration _configuration;
    private HashSet<TSelection> _samplingGroupSet;
    private SequentialJob _runningJob;

    internal AdapterSchedule(Scheduler scheduler, Func<string, IReadOnlyList<TSelection>, CancellationToken, Task> sampleDataCallback)
    {
        _scheduler = scheduler;
        _sampleDataCallback = sampleDataCallback;
    }

    public bool CanStart
    {
        get
        {
            return _configuration?.Period != null && _samplingGroupSet != null && _samplingGroupSet.Count != 0;
        }
    }

    public bool TryUpdateConfiguration(ScheduleConfiguration newConfiguration)
    {
        bool updated;
        if (_configuration == null)
        {
            updated = newConfiguration != null;
        }
        else
        {
            updated = !_configuration.Equals(newConfiguration);
        }

        _configuration = updated ? newConfiguration : _configuration;
        return updated;
    }

    public bool TryUpdateSamplingGroup(HashSet<TSelection> newSamplingGroup)
    {
        bool updated;
        if (_samplingGroupSet == null)
        {
            updated = newSamplingGroup != null;
        }
        else
        {
            if (newSamplingGroup == null)
            {
                updated = true;
            }
            else
            {
                updated = !newSamplingGroup.SetEquals(_samplingGroupSet);
            }
        }

        _samplingGroupSet = updated ? newSamplingGroup : _samplingGroupSet;
        return updated;
    }

    public void Start()
    {
        if (_runningJob == null)
        {
            if (!CanStart)
            {
                throw new InvalidOperationException("Failed to start. Adapter schedule is not properly configured.");
            }

            var periodicSchedule = new PeriodicSchedule(_configuration.Id, _configuration.Period.Value, _configuration.Offset);
            _runningJob = _scheduler.AddSequentialJob(periodicSchedule, (ct) => SampleDataInternalAsync(_configuration.Id, _samplingGroupSet.ToList(), ct), false);
        }
    }

    public void Stop()
    {
        if (_runningJob != null)
        {
            _scheduler.RemoveJob(_runningJob);
            _runningJob = null;
        }
    }

    private async Task SampleDataInternalAsync(string scheduleId, IReadOnlyList<TSelection> selections, CancellationToken cancellationToken)
    {
        if (selections == null || selections.Count < 0)
        {
            return;
        }

        await _sampleDataCallback(scheduleId, selections, cancellationToken);
    }
}
