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
using AdapterFramework.Data.Framework.Common.Scheduling;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Scheduling;

public class PeriodicSchedule_Tests
{
    private const string Id = "Derp";
    private readonly TimeSpan _timeSpan = TimeSpan.FromMilliseconds(250);
    private readonly PeriodicSchedule _periodicSchedule;

    public PeriodicSchedule_Tests()
    {
        _periodicSchedule = new PeriodicSchedule(Id, _timeSpan, null);
    }

    [Fact]
    public void GetNextRunTime_Test()
    {
        var time = DateTime.UtcNow;
        var nextRun = _periodicSchedule.GetNextRunTime(time);
        Assert.True(nextRun > time);
        var nextNextRun = _periodicSchedule.GetNextRunTime(nextRun);

        var interval = nextNextRun.Subtract(nextRun);
        Assert.True(Math.Abs(interval.Ticks - _timeSpan.Ticks) < 100);
    }
}
