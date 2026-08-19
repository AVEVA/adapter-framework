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

namespace AdapterFramework.Data.Framework.Common.Scheduling.Statistics;

public class JobStatistics
{
    private readonly object _lockObj = new object();
    private long _successfulRunCount;
    private long _failedRunCount;

    public long SuccessfulRunCount => _successfulRunCount;

    public long FailedRunCount => _failedRunCount;

    public TimeSpan MaxRunTime { get; private set; }

    public TimeSpan TotalRunTime { get; private set; }

    internal void OnRunCompleted(TimeSpan runDuration)
    {
        UpdateRunDurationStatistics(runDuration);

        Interlocked.Increment(ref _successfulRunCount);
    }

    internal void OnRunFailed(TimeSpan runDuration)
    {
        UpdateRunDurationStatistics(runDuration);

        Interlocked.Increment(ref _failedRunCount);
    }

    private void UpdateRunDurationStatistics(TimeSpan runDuration)
    {
        lock (_lockObj)
        {
            if (MaxRunTime < runDuration)
            {
                MaxRunTime = runDuration;
            }

            TotalRunTime += runDuration;
        }
    }
}
