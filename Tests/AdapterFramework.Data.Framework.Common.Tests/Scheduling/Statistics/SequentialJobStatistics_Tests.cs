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
using AdapterFramework.Data.Framework.Common.Scheduling.Statistics;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Scheduling.Statistics;

public class SequentialJobStatistics_Tests
{
    private readonly SequentialJobStatistics _jobStatistics;

    public SequentialJobStatistics_Tests()
    {
        _jobStatistics = new SequentialJobStatistics();
    }

    [Fact]
    public void OnRunSkipped_Test()
    {
        _jobStatistics.OnRunSkipped();
        Assert.Equal(1, _jobStatistics.SkippedRunCount);

        _jobStatistics.OnRunSkipped();
        Assert.Equal(2, _jobStatistics.SkippedRunCount);

        _jobStatistics.OnRunSkipped();
        Assert.Equal(3, _jobStatistics.SkippedRunCount);
    }

    [Fact]
    public void OnRunMissed_Test()
    {
        _jobStatistics.OnRunMissed();
        Assert.Equal(1, _jobStatistics.MissedRunCount);

        _jobStatistics.OnRunMissed();
        Assert.Equal(2, _jobStatistics.MissedRunCount);

        _jobStatistics.OnRunMissed();
        Assert.Equal(3, _jobStatistics.MissedRunCount);
    }
}
