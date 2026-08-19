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

public class ScheduleConfiguration_Tests
{
    [Theory]
    [InlineData("0:00:00.5")]
    [InlineData("1:00:00")]
    [InlineData("0:50:00")]
    public void Schedules_ValidPeriod_Test(string period)
    {
        TimeSpan? periodTimeSpan;

        if (period == null)
        {
            periodTimeSpan = null;
        }
        else
        {
            periodTimeSpan = TimeSpan.Parse(period, CultureInfo.InvariantCulture);
        }

        var scheduleConfig = new ScheduleConfiguration
        {
            Id = "1",
            Period = periodTimeSpan,
            Offset = TimeSpan.FromSeconds(1),
        };

        var validationResults = scheduleConfig.Validate();
        Assert.Empty(validationResults);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("-1:00:00")]
    [InlineData("0:00:00")]
    public void Schedules_InvalidPeriod_Test(string period)
    {
        TimeSpan? periodTimeSpan;

        if (period == null)
        {
            periodTimeSpan = null;
        }
        else
        {
            periodTimeSpan = TimeSpan.Parse(period, CultureInfo.InvariantCulture);
        }

        var scheduleConfig = new ScheduleConfiguration
        {
            Id = "1",
            Period = periodTimeSpan,
            Offset = TimeSpan.FromSeconds(1),
        };

        var validationResults = scheduleConfig.Validate();
        Assert.NotEmpty(validationResults);
    }

    [Theory]
    [InlineData("0:00:00")]
    [InlineData("1:00:00")]
    [InlineData("0:50:00")]
    [InlineData(null)]
    public void Schedules_ValidOffset_Test(string offset)
    {
        TimeSpan? offsetTimeSpan;

        if (offset == null)
        {
            offsetTimeSpan = null;
        }
        else
        {
            offsetTimeSpan = TimeSpan.Parse(offset, CultureInfo.InvariantCulture);
        }

        var scheduleConfig = new ScheduleConfiguration
        {
            Id = "1",
            Period = TimeSpan.FromSeconds(1),
            Offset = offsetTimeSpan,
        };

        var validationResults = scheduleConfig.Validate();
        Assert.Empty(validationResults);
    }

    [Theory]
    [InlineData("-1:00:00")]
    [InlineData("-0:00:00.01")]
    public void Schedules_InvalidOffset_Test(string offset)
    {
        TimeSpan? offsetTimeSpan;

        if (offset == null)
        {
            offsetTimeSpan = null;
        }
        else
        {
            offsetTimeSpan = TimeSpan.Parse(offset, CultureInfo.InvariantCulture);
        }

        var scheduleConfig = new ScheduleConfiguration
        {
            Id = "1",
            Period = TimeSpan.FromSeconds(1),
            Offset = offsetTimeSpan,
        };

        var validationResults = scheduleConfig.Validate();
        Assert.NotEmpty(validationResults);
    }
}
