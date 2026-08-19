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
using AdapterFramework.Data.Framework.Abstractions.Scheduling;
using AdapterFramework.Data.Framework.Common.Scheduling.Statistics;

namespace AdapterFramework.Data.Framework.Common.Scheduling;

/// <summary>
/// Implementation of the <see cref="SequentialJob"/> with notion of missed and skipped scans.
/// </summary>
public sealed class SequentialJob : Job
{
    private readonly int _maxQueueLength;
    private readonly object _lock = new object();
    private Task _lastTask = Task.CompletedTask;
    private int _taskCount;

    public SequentialJob(string id, ISchedule schedule, Func<CancellationToken, Task> function, ILogger logger, bool isSuspended, int maxQueueLength)
        : base(id, schedule, function, new SequentialJobStatistics(), logger, isSuspended)
    {
        _maxQueueLength = maxQueueLength;
    }

    public new SequentialJobStatistics Statistics => (SequentialJobStatistics)base.Statistics;

    protected override async Task RunInternalAsync()
    {
        Task currentTask;
        lock (_lock)
        {
            if (_taskCount == _maxQueueLength + 1)
            {
                Statistics.OnRunSkipped();
                Logger.LogWarning("Scan skipped for schedule ID {JobId}.", Id);

                return;
            }

            if (_taskCount++ > 0)
            {
                Statistics.OnRunMissed();
                Logger.LogWarning("Scan missed for schedule ID {JobId}.", Id);
            }

            _lastTask = currentTask = _lastTask.ContinueWith(_ => base.RunInternalAsync(), TaskScheduler.Current).Unwrap();
        }

        await currentTask;

        lock (_lock)
        {
            --_taskCount;
        }
    }
}
