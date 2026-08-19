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
using AdapterFramework.Data.Framework.Common.Scheduling.Statistics;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Scheduling.Statistics;

public class JobStatistics_Tests
{
    private const int FirstRunDuration = 1;
    private const int SecondRunDuration = 200;
    private const int ThirdRunDuration = 3000;
    private const int FourthRunDuration = 90;

    private readonly JobStatistics _jobStatistics;
    private long _expectedTotalRunTime;
    private bool _onCompletedTestRun;
    private bool _onFailedTestRun;

    public JobStatistics_Tests()
    {
        _jobStatistics = new JobStatistics();
    }

    [Fact]
    public void OnRunCompleted_Test()
    {
        _onCompletedTestRun = true;

        _jobStatistics.OnRunCompleted(TimeSpan.FromMilliseconds(FirstRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(FirstRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(1, _jobStatistics.SuccessfulRunCount);

        _jobStatistics.OnRunCompleted(TimeSpan.FromMilliseconds(SecondRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(SecondRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(2, _jobStatistics.SuccessfulRunCount);

        _jobStatistics.OnRunCompleted(TimeSpan.FromMilliseconds(ThirdRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(ThirdRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(3, _jobStatistics.SuccessfulRunCount);

        _jobStatistics.OnRunCompleted(TimeSpan.FromMilliseconds(FourthRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(ThirdRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(4, _jobStatistics.SuccessfulRunCount);

        _expectedTotalRunTime = FirstRunDuration + SecondRunDuration + ThirdRunDuration + FourthRunDuration;
        if (_onFailedTestRun)
        {
            _expectedTotalRunTime += _expectedTotalRunTime;
        }

        Assert.Equal(TimeSpan.FromMilliseconds(_expectedTotalRunTime).Ticks, _jobStatistics.TotalRunTime.Ticks);
    }

    [Fact]
    public void OnRunFailed_Test()
    {
        _onFailedTestRun = true;

        _jobStatistics.OnRunFailed(TimeSpan.FromMilliseconds(FirstRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(FirstRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(1, _jobStatistics.FailedRunCount);

        _jobStatistics.OnRunFailed(TimeSpan.FromMilliseconds(SecondRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(SecondRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(2, _jobStatistics.FailedRunCount);

        _jobStatistics.OnRunFailed(TimeSpan.FromMilliseconds(ThirdRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(ThirdRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(3, _jobStatistics.FailedRunCount);

        _jobStatistics.OnRunFailed(TimeSpan.FromMilliseconds(FourthRunDuration));
        Assert.Equal(TimeSpan.FromMilliseconds(ThirdRunDuration).Ticks, _jobStatistics.MaxRunTime.Ticks);
        Assert.Equal(4, _jobStatistics.FailedRunCount);

        _expectedTotalRunTime = FirstRunDuration + SecondRunDuration + ThirdRunDuration + FourthRunDuration;
        if (_onCompletedTestRun)
        {
            _expectedTotalRunTime += _expectedTotalRunTime;
        }

        Assert.Equal(TimeSpan.FromMilliseconds(_expectedTotalRunTime).Ticks, _jobStatistics.TotalRunTime.Ticks);
    }
}
