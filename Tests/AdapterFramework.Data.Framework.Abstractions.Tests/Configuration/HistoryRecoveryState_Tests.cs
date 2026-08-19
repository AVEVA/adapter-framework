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
using System.Globalization;
using AdapterFramework.Data.Framework.Abstractions.Configuration;

using Xunit;

namespace AdapterFramework.Data.Framework.Abstractions.Tests.Configuration;

public class HistoryRecoveryState_Tests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("4:50", "4:50")]
    [InlineData("4:55", "4:50")]
    [InlineData(null, "03/24/1999 18:33:56")]
    [InlineData("03/24/9999 18:33:56", null)]
    public void PostHistoryRecoveryStates_InvalidStates_Test(string start, string end)
    {
        DateTime? startTime;
        DateTime? endTime;

        if (start == null)
        {
            startTime = null;
        }
        else
        {
            startTime = DateTime.Parse(start, CultureInfo.InvariantCulture);
        }

        if (end == null)
        {
            endTime = null;
        }
        else
        {
            endTime = DateTime.Parse(end, CultureInfo.InvariantCulture);
        }

        var historyRecoveryState = new HistoryRecoveryState() { Id = "did1", StartTime = startTime, EndTime = endTime };
        Assert.NotEmpty(historyRecoveryState.Validate());
    }
}
