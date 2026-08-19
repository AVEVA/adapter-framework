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
using AdapterFramework.Data.Framework.AdapterCommon.Discovery;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Discovery;

public class DataSelectionConfigurationComparer_Tests
{
    private readonly DataSelectionConfigurationComparer<TestDataSelectionWithId> _dataSelectionComparer;

    public DataSelectionConfigurationComparer_Tests()
    {
        _dataSelectionComparer = new DataSelectionConfigurationComparer<TestDataSelectionWithId>();
    }

    [Theory]
    [InlineData(false, "streamIdA", "scheduleA", "streamIdB", "scheduleB")]
    [InlineData(false, "streamIdA", "scheduleA", "streamIdB", "scheduleA")]
    [InlineData(true, "streamIdA", "scheduleA", "streamIdA", "scheduleB")]
    [InlineData(true, "streamIdA", "scheduleA", "streamIdA", "scheduleA")]
    [InlineData(true, "streamIdA", "scheduleA", "STReamIdA", "scheduleB")]
    [InlineData(true, "streamIdA", "scheduleA", "strEAmIdA", "ScheduleA")]
    public void Equals_Tests(bool areEqual, string streamIdA, string scheduleA, string streamIdB, string scheduleB)
    {
        var dataSelectionItemX = new TestDataSelectionWithId(streamIdA, scheduleA);
        var dataSelectionItemY = new TestDataSelectionWithId(streamIdB, scheduleB);
        Assert.Equal(areEqual, _dataSelectionComparer.Equals(dataSelectionItemX, dataSelectionItemY));
    }

    [Theory]
    [InlineData(false, "streamIdA", "scheduleA", true, "streamIdB", "scheduleB", false)]
    [InlineData(false, "streamIdA", "scheduleA", true, "streamIdB", "scheduleB", true)]
    [InlineData(false, "streamIdA", "scheduleA", true, "streamIdB", "scheduleA", true)]
    [InlineData(true, "streamIdA", "scheduleA", true, "streamIdA", "scheduleB", false)]
    public void GetHashCode_Tests(bool areEqual, string streamIdA, string scheduleA, bool selectedA, string streamIdB, string scheduleB, bool selectedB)
    {
        var dataSelectionItemA = new TestDataSelectionWithId(streamIdA, scheduleA, selectedA);
        var dataSelectionItemB = new TestDataSelectionWithId(streamIdB, scheduleB, selectedB);
        var hashCodesAreEqual = _dataSelectionComparer.GetHashCode(dataSelectionItemA) == _dataSelectionComparer.GetHashCode(dataSelectionItemB);
        Assert.Equal(areEqual, hashCodesAreEqual);
    }
}
