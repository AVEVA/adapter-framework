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
using System.Collections.Generic;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public class IntervalComparer : IComparer<Interval>
{
    public int Compare(Interval firstInterval, Interval secondInterval)
    {
        if (firstInterval is null && secondInterval is null)
        {
            return 0;
        }

        if (firstInterval is null)
        {
            return -1;
        }

        if (secondInterval is null)
        {
            return 1;
        }

        return CompareDateTimes(firstInterval.StartTime.GetValueOrDefault(), secondInterval.StartTime.GetValueOrDefault());
    }

    private static int CompareDateTimes(DateTime firstDateTime, DateTime secondDateTime)
    {
        return firstDateTime.CompareTo(secondDateTime);
    }
}
