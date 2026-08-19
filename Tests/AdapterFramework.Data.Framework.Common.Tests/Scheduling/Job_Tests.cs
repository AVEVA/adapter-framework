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
using AdapterFramework.Data.Framework.Common.Scheduling;
using AdapterFramework.Data.Framework.Common.Scheduling.Statistics;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Scheduling;

public class Job_Tests : IDisposable
{
    private const string Id = "Derp";
    private readonly ISchedule _schedule;
    private readonly Job _job;
    private readonly TimeSpan _timeSpan = TimeSpan.FromSeconds(1);
    private int _funcCallCounter;
    private bool _disposed;

    public Job_Tests()
    {
        var logger = new TestLogger();
        _schedule = new PeriodicSchedule(Id, _timeSpan, null);
        _job = new FakeJob(Id, _schedule, FuncCallbackAsync, true, logger);
    }

    [Fact]
    public async Task RunAsync_SuspendedNoFuncCall_Test()
    {
        await _job.RunAsync();
        Assert.Equal(0, _funcCallCounter);
        Assert.Equal(0, _job.Statistics.SuccessfulRunCount);
        Assert.True(_job.IsSuspended);
    }

    [Fact]
    public async Task RunAsync_ResumedCall_Test()
    {
        _job.Resume();
        await _job.RunAsync();
        Assert.Equal(1, _funcCallCounter);
        Assert.Equal(1, _job.Statistics.SuccessfulRunCount);
        Assert.False(_job.IsSuspended);
    }

    [Fact]
    public async Task RunAsync_ResumedCallWithException_Test()
    {
        FakeJob badJob = null;
        try
        {
            var logger = new TestLogger();
            badJob = new FakeJob(Id, _schedule, FuncCallbackExceptionAsync, true, logger);

            badJob.Resume();
            await badJob.RunAsync();
            Assert.Equal(1, _funcCallCounter);
            Assert.Equal(1, badJob.Statistics.FailedRunCount);
            Assert.False(badJob.IsSuspended);
            Assert.True(logger.AreErrorsWarningsInLog());
        }
        finally
        {
            badJob?.Dispose();
        }
    }

    [Fact]
    public void GetNextRun_GetsFutureTime_Test()
    {
        var time = DateTime.UtcNow;
        var nextRun = _job.GetNextRunTime(time);
        Assert.True(nextRun > time);
        var nextNextRun = _job.GetNextRunTime(nextRun);

        var interval = nextNextRun.Subtract(nextRun);
        Assert.True(Math.Abs(interval.Ticks - _timeSpan.Ticks) < 1000);
    }

    [Fact]
    public async Task CancelRun_NoFuncCall_Test()
    {
        _job.Resume();
        _job.CancelRun();
        await _job.RunAsync();
        Assert.Equal(0, _funcCallCounter);
        Assert.Equal(1, _job.Statistics.SuccessfulRunCount);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _job?.Dispose();
        }

        _disposed = true;
    }

    private async Task FuncCallbackAsync(CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _funcCallCounter);
            await Task.CompletedTask;
        }
    }

    private Task FuncCallbackExceptionAsync(CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _funcCallCounter);
            throw new Exception("Ded");
        }

        return Task.CompletedTask;
    }

    private class FakeJob : Job
    {
        public FakeJob(string id, ISchedule schedule, Func<CancellationToken, Task> function, bool isSuspended, ILogger logger) : base(id, schedule, function, new JobStatistics(), logger, isSuspended)
        {
        }
    }
}
