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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Scheduling;
using AdapterFramework.Data.Framework.Common.Scheduling.Statistics;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.Common.Tests")]
namespace AdapterFramework.Data.Framework.Common.Scheduling;

public abstract class Job : IDisposable
{
    private readonly Func<CancellationToken, Task> _function;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly ISchedule _schedule;
    private volatile bool _isSuspended;
    private bool _disposed;

    protected Job(string id, ISchedule schedule, Func<CancellationToken, Task> function, JobStatistics statistics, ILogger logger, bool isSuspended)
    {
        Id = id;
        _schedule = schedule;
        _function = function;
        _isSuspended = isSuspended;
        _cancellationTokenSource = new CancellationTokenSource();
        Statistics = statistics;
        Logger = logger;
    }

    public JobStatistics Statistics { get; }

    public string Id { get; }

    public DateTime LastRunTime { get; private set; }

    public bool IsSuspended => _isSuspended;

    protected ILogger Logger { get; }

    public void Suspend()
    {
        _isSuspended = true;
    }

    public void Resume()
    {
        _isSuspended = false;
    }

    public DateTime GetNextRunTime(DateTime time)
    {
        return _schedule.GetNextRunTime(time);
    }

    public void CancelRun()
    {
        _cancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal async Task RunAsync()
    {
        if (_isSuspended)
        {
            return;
        }

        LastRunTime = DateTime.UtcNow;
        await RunInternalAsync();
    }

    protected virtual async Task RunInternalAsync()
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();

        try
        {
            if (_isSuspended)
            {
                return;
            }

            await _function(_cancellationTokenSource.Token);

            stopWatch.Stop();
            Statistics.OnRunCompleted(stopWatch.Elapsed);
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            stopWatch.Stop();
            Statistics.OnRunFailed(stopWatch.Elapsed);

            Logger.LogError(ex, "Execution of schedule with ID {JobId} failed.", Id);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cancellationTokenSource?.Dispose();
        }

        _disposed = true;
    }
}
