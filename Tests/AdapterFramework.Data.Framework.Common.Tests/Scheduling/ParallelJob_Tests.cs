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
using AdapterFramework.Data.Framework.Common.Scheduling;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Scheduling;

public class ParallelJob_Tests : IDisposable
{
    private const string Id = "TestId";
    private readonly ParallelJob _job;
    private readonly TimeSpan _timeSpan = TimeSpan.FromMilliseconds(250);
    private int _funcCallCounter;
    private bool _disposed;

    public ParallelJob_Tests()
    {
        var logger = new TestLogger();
        var schedule = new PeriodicSchedule(Id, _timeSpan, null);
        _job = new ParallelJob(Id, schedule, FuncCallback, logger, false);
    }

    [Fact]
    public async Task RunAsyncOneCall_Test()
    {
        await _job.RunAsync();
        Assert.Equal(1, _funcCallCounter);
        Assert.Equal(0, _job.Statistics.FailedRunCount);
        Assert.Equal(1, _job.Statistics.SuccessfulRunCount);
        Assert.True(_job.Statistics.MaxRunTime > TimeSpan.FromMilliseconds(0));
        Assert.Equal(_job.Statistics.TotalRunTime, _job.Statistics.MaxRunTime);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task RunAsyncMultipleCall_Test(int calls)
    {
        var tasks = new Task[calls];
        for (int i = 0; i < calls; i++)
        {
            tasks[i] = _job.RunAsync();
        }

        await Task.WhenAll(tasks);
        Assert.Equal(calls, _funcCallCounter);
        Assert.Equal(0, _job.Statistics.FailedRunCount);
        Assert.Equal(calls, _job.Statistics.SuccessfulRunCount);
        Assert.True(_job.Statistics.MaxRunTime > TimeSpan.FromMilliseconds(0));
        Assert.True(_job.Statistics.TotalRunTime >= _job.Statistics.MaxRunTime);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Protected implementation of Dispose pattern.
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

    private async Task FuncCallback(CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _funcCallCounter);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
