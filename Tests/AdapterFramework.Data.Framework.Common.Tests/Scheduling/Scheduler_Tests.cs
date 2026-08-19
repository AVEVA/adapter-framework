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
using AdapterFramework.Data.Framework.Abstractions.Scheduling;
using AdapterFramework.Data.Framework.Common.Scheduling;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AdapterFramework.Data.Framework.Common.Tests.Scheduling;

public class Scheduler_Tests : IDisposable
{
    private const string Id = "Derp";
    private readonly Scheduler _scheduler;
    private readonly ISchedule _schedule;
    private readonly TimeSpan _timeSpan = TimeSpan.FromMilliseconds(250);
    private int _funcCallCounter;
    private bool _disposed;

    public Scheduler_Tests()
    {
        var logger = new TestLogger();
        _scheduler = new Scheduler(logger);
        _schedule = new PeriodicSchedule(Id, _timeSpan, null);
    }

    [InlineData(true)]
    [InlineData(false)]
    [Theory]
    public void AddQueuingSequentialJob_Test(bool isSuspended)
    {
        var job = _scheduler.AddSequentialJob(_schedule, FuncCallback, isSuspended);
        Assert.Equal(Id, job.Id);
        Assert.Equal(isSuspended, job.IsSuspended);

        if (isSuspended)
        {
            Assert.Equal(default, job.LastRunTime);
        }
    }

    [InlineData(true)]
    [InlineData(false)]
    [Theory]
    public void AddParallelJob_Test(bool isSuspended)
    {
        var job = _scheduler.AddParallelJob(_schedule, FuncCallback, isSuspended);
        Assert.Equal(Id, job.Id);
        Assert.Equal(isSuspended, job.IsSuspended);

        if (isSuspended)
        {
            Assert.Equal(default, job.LastRunTime);
        }
    }

    [Fact]
    public void RemoveJob_Test()
    {
        string schedule2 = "#2";
        var job = _scheduler.AddParallelJob(_schedule, FuncCallback, true);
        var job2 = _scheduler.AddSequentialJob(new PeriodicSchedule(schedule2, TimeSpan.FromSeconds(1), null), FuncCallback, true);

        _scheduler.RemoveJob(job);
        Assert.True(_scheduler.ContainsJob(job2));
        Assert.False(_scheduler.ContainsJob(job));

        _scheduler.RemoveJob(job2);
        Assert.False(_scheduler.ContainsJob(job2));
        Assert.False(_scheduler.ContainsJob(job));
    }

    [Fact]
    public async Task AddedJobsAreRun_Test()
    {
        var job = _scheduler.AddParallelJob(_schedule, FuncCallback, false);
        await Task.Delay(_timeSpan * 5.2);
        job.Suspend();
        Assert.True(_funcCallCounter >= 5);
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
            _scheduler?.Dispose();
        }

        _disposed = true;
    }

    private async Task FuncCallback(CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _funcCallCounter);
            await Task.CompletedTask;
        }
    }
}
