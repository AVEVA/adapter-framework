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

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Counts repeated failures so they can be reported at most once per interval, with the number of occurrences since the last report.
/// The first occurrence is always reported immediately. Thread-safe.
/// </summary>
internal sealed class RateLimitedCounter
{
    internal const int DefaultIntervalMilliseconds = 60 * 1000;

    private long _count;
    private long _nextReport = long.MinValue;

    internal RateLimitedCounter(int intervalMilliseconds = DefaultIntervalMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intervalMilliseconds);
        IntervalMilliseconds = intervalMilliseconds;
    }

    internal int IntervalMilliseconds { get; }

    /// <summary>
    /// Records one occurrence.
    /// </summary>
    /// <param name="count">When a report is due, the number of occurrences since the last report, including this one.</param>
    /// <returns><see langword="true"/> if the caller should report now.</returns>
    internal bool Increment(out long count)
    {
        Interlocked.Increment(ref _count);

        var now = Environment.TickCount64;
        var due = Interlocked.Read(ref _nextReport);
        if (now < due || Interlocked.CompareExchange(ref _nextReport, now + IntervalMilliseconds, due) != due)
        {
            count = 0;
            return false;
        }

        count = Interlocked.Exchange(ref _count, 0);
        return count > 0;
    }
}
