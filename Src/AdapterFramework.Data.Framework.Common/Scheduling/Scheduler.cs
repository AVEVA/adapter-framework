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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Scheduling;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Common.Scheduling;

public partial class Scheduler : IDisposable
{
    #region Private Fields

    private readonly JobCollection _jobs;
    private readonly object _jobsLock = new object();
    private readonly ILogger _logger;
    private CancellationTokenSource _cts;
    private bool _disposed;

    #endregion

    #region Public Constructor

    public Scheduler(ILogger logger)
    {
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));

        _logger = logger;
        _jobs = new JobCollection();
        _cts = new CancellationTokenSource();

        _ = Task.Run(RunJobsAsync);
    }

    #endregion

    #region Public Methods

    public SequentialJob AddSequentialJob(ISchedule schedule, Func<CancellationToken, Task> function, bool isSuspended, int maxQueueLength = 1)
    {
        ThrowHelper.ThrowIfArgumentNull(schedule, nameof(schedule));
        ThrowHelper.ThrowIfArgumentNull(function, nameof(function));

        var job = new SequentialJob(schedule.Id, schedule, function, _logger, isSuspended, maxQueueLength);
        AddJob(job);

        return job;
    }

    public ParallelJob AddParallelJob(ISchedule schedule, Func<CancellationToken, Task> function, bool isSuspended)
    {
        ThrowHelper.ThrowIfArgumentNull(schedule, nameof(schedule));
        ThrowHelper.ThrowIfArgumentNull(function, nameof(function));

        var job = new ParallelJob(schedule.Id, schedule, function, _logger, isSuspended);
        AddJob(job);

        return job;
    }

    public void RemoveJob(Job jobToRemove)
    {
        ThrowHelper.ThrowIfArgumentNull(jobToRemove, nameof(jobToRemove));

        jobToRemove.CancelRun();
        jobToRemove.Suspend();

        lock (_jobsLock)
        {
            _jobs.Remove(jobToRemove);
        }

        jobToRemove.Dispose();
    }

    public bool ContainsJob(Job job)
    {
        ThrowHelper.ThrowIfArgumentNull(job, nameof(job));

        lock (_jobsLock)
        {
            return _jobs.Contains(job);
        }
    }

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
            lock (_jobsLock)
            {
                _jobs.Dispose();
            }

            _cts?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private static TimeSpan CalculateWaitTime(DateTime? nextRunTime)
    {
        if (nextRunTime == null)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var waitTime = nextRunTime.Value - DateTime.UtcNow;
        if (waitTime < TimeSpan.Zero)
        {
            waitTime = TimeSpan.Zero;
        }

        return waitTime;
    }

    private void AddJob(Job jobToAdd)
    {
        lock (_jobsLock)
        {
            _jobs.Add(jobToAdd);
        }
    }

    private async Task RunJobsAsync()
    {
        var referenceTime = DateTime.UtcNow;
        DateTime? nextRunTime = null;
        ICollection<Job> jobsToRun = null;

        void OnJobsChanged(object o, EventArgs args)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _jobs.Changed += OnJobsChanged;

        while (true)
        {
            try
            {
                lock (_jobsLock)
                {
                    nextRunTime = GetNextRunTime(referenceTime, out jobsToRun);
                }

                var waitTime = CalculateWaitTime(nextRunTime);

                await Task.Delay(waitTime, _cts.Token);

                referenceTime = nextRunTime ?? DateTime.UtcNow;

                lock (_jobsLock)
                {
                    RunJobs(jobsToRun);
                }
            }
            catch (TaskCanceledException)
            {
                var now = DateTime.UtcNow;

                referenceTime = nextRunTime == null ? now : (nextRunTime.Value < now ? nextRunTime.Value : now);

                if (nextRunTime.HasValue && nextRunTime.Value == now)
                {
                    lock (_jobsLock)
                    {
                        RunJobs(jobsToRun);
                    }
                }

                _jobs.Changed -= OnJobsChanged;

                _cts.Dispose();
                _cts = new CancellationTokenSource();

                _jobs.Changed += OnJobsChanged;
            }
        }
    }

    private void RunJobs(IEnumerable<Job> jobs)
    {
        foreach (var job in jobs)
        {
            if (_jobs.Contains(job))
            {
                _logger.LogDebug("Running job: {JobId}.", job.Id);
                _ = job.RunAsync();
            }
        }
    }

    private DateTime? GetNextRunTime(DateTime referenceTime, out ICollection<Job> jobsToRun)
    {
        // think this could have better runtime performance
        jobsToRun = new List<Job>();
        DateTime? minNextRunTime = DateTime.MaxValue;

        foreach (var job in _jobs)
        {
            var nextRunTime = job.GetNextRunTime(referenceTime);
            if (minNextRunTime > nextRunTime)
            {
                minNextRunTime = nextRunTime;
                jobsToRun.Clear();
                jobsToRun.Add(job);
            }
            else if (minNextRunTime == nextRunTime)
            {
                jobsToRun.Add(job);
            }
        }

        return jobsToRun.Count > 0 ? minNextRunTime : null;
    }

    #endregion
}
