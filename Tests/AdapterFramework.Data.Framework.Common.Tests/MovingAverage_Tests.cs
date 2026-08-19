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
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests;

public class MovingAverage_Tests
{
    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    public void MovingAverage_Constructor_InvalidInput_Test(int invalidPeriod)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovingAverage(invalidPeriod));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void MovingAverage_AddSample_Test(int period)
    {
        var testMovingAverage = new MovingAverage(period);

        testMovingAverage.AddSample(long.MaxValue);

        for (var i = period - 1; i >= 0; i--)
        {
            testMovingAverage.AddSample(i);
        }

        Assert.True(testMovingAverage.ComputeAverage() < period);
    }

    [Fact]
    public void MovingAverage_ComputeAverage_NoValueAdded_Test()
    {
        var testMovingAverage = new MovingAverage(100);

        Assert.Equal(0, testMovingAverage.ComputeAverage());
    }

    [Fact]
    public void MovingAverage_ClearSamples_Test()
    {
        var period = 100;
        var testMovingAverage = new MovingAverage(period);

        for (var i = period - 1; i >= 0; i--)
        {
            testMovingAverage.AddSample(i);
        }

        Assert.NotEqual(0, testMovingAverage.ComputeAverage());

        testMovingAverage.ClearSamples();

        Assert.Equal(0, testMovingAverage.ComputeAverage());
    }

    [Fact]
    public void MovingAverage_ClearSamples_NoValueAdded_Test()
    {
        var testMovingAverage = new MovingAverage(100);
        testMovingAverage.ClearSamples();

        Assert.Equal(0, testMovingAverage.ComputeAverage());
    }
}
