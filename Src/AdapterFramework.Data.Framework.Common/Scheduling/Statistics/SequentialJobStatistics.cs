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
using System.Threading;

namespace AdapterFramework.Data.Framework.Common.Scheduling.Statistics;

public class SequentialJobStatistics : JobStatistics
{
    private long _skippedRunCount;
    private long _missedRunCount;

    public long SkippedRunCount => _skippedRunCount;

    public long MissedRunCount => _missedRunCount;

    internal void OnRunMissed() => Interlocked.Increment(ref _missedRunCount);

    internal void OnRunSkipped() => Interlocked.Increment(ref _skippedRunCount);
}
