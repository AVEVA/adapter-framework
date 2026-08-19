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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Common.Scheduling;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Scheduling;

public class QueueingSequentialJob_Tests : IDisposable
{
    private const string Id = "Derp";
    private readonly CancellationTokenSource _cts;
    private readonly TimeSpan _timeSpan = TimeSpan.FromMilliseconds(250);
    private readonly TimeSpan _delayTimeSpan = TimeSpan.FromSeconds(1);
    private readonly PeriodicSchedule _schedule;
    private SequentialJob _job;
    private int _funcCallCounter;
    private bool _disposed;

    public QueueingSequentialJob_Tests()
    {
        _schedule = new PeriodicSchedule(Id, _timeSpan, null);
        _cts = new CancellationTokenSource();
    }

    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [Theory]
    public async Task RunAsyncOneCall_Test(int maxQueueSize)
    {
        var logger = new TestLogger();
        _job = new SequentialJob(Id, _schedule, FuncCallback, logger, false, maxQueueSize);
        _cts.Cancel();
        await _job.RunAsync();
        Assert.Equal(1, _funcCallCounter);
        Assert.Equal(0, _job.Statistics.FailedRunCount);
        Assert.Equal(1, _job.Statistics.SuccessfulRunCount);
        Assert.True(_job.Statistics.MaxRunTime > TimeSpan.FromMilliseconds(0));
        Assert.Equal(_job.Statistics.TotalRunTime, _job.Statistics.MaxRunTime);
    }

    [InlineData(0, 2)]
    [InlineData(0, 10)]
    [InlineData(1, 2)]
    [InlineData(1, 10)]
    [InlineData(5, 2)]
    [InlineData(5, 10)]
    [Theory]
    public async Task RunAsyncMultipleCall_Test(int maxQueueSize, int calls)
    {
        var logger = new TestLogger();
        var expectedSkippedMessage = $"Scan skipped for schedule ID {Id}.";
        var expectedMissedMessage = $"Scan missed for schedule ID {Id}.";
        Assert.True(calls >= 2, "Number of calls must be greater than 1. Use the RunAsyncOneCall test if you only want to test one call.");
        _job = new SequentialJob(Id, _schedule, FuncCallback, logger, false, maxQueueSize);

        var tasks = new Task[calls];
        for (int i = 0; i < calls; i++)
        {
            tasks[i] = _job.RunAsync();
        }

        _cts.Cancel();
        await Task.WhenAll(tasks);

        int expectedSuccess = Math.Min(maxQueueSize + 1, calls);
        Assert.True(SpinWait.SpinUntil(() => _job.Statistics.SuccessfulRunCount == expectedSuccess, _delayTimeSpan), $"run count was {_job.Statistics.SuccessfulRunCount}. Expected was {expectedSuccess}.");
        Assert.Equal(expectedSuccess, _funcCallCounter);

        var logMessages = logger.GetLogMessages();
        Assert.Equal(calls - expectedSuccess, _job.Statistics.SkippedRunCount);
        Assert.Equal(_job.Statistics.SkippedRunCount, logMessages.Count(l => l.LogMessage == expectedSkippedMessage && l.LogLevel == LogLevel.Warning));

        Assert.Equal(Math.Min(calls - 1, maxQueueSize), _job.Statistics.MissedRunCount);
        Assert.Equal(_job.Statistics.MissedRunCount, logMessages.Count(l => l.LogMessage == expectedMissedMessage && l.LogLevel == LogLevel.Warning));

        Assert.Equal(_job.Statistics.MissedRunCount + _job.Statistics.SkippedRunCount, logMessages.Count);

        Assert.Equal(expectedSuccess, _job.Statistics.SuccessfulRunCount);
        Assert.True(_job.Statistics.MaxRunTime > TimeSpan.FromMilliseconds(0));
        Assert.True(_job.Statistics.TotalRunTime >= _job.Statistics.MaxRunTime);
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
            _cts?.Dispose();
        }

        _disposed = true;
    }

    private async Task FuncCallback(CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _funcCallCounter);
            try
            {
                await Task.Delay(-1, _cts.Token);
            }
            catch
            {
                // ignored
            }
        }
    }
}
