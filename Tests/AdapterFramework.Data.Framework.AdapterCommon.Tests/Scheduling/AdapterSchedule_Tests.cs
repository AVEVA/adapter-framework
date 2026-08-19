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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.AdapterCommon.Scheduling;
using AdapterFramework.Data.Framework.Common.Scheduling;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Scheduling;

public class AdapterSchedule_Tests : IDisposable
{
    private readonly Scheduler _testScheduler;
    private readonly AdapterSchedule<TestDataSelectionWithId> _testSchedule;
    private bool _disposed;

    private string _startedScheduleId;
    private IReadOnlyList<TestDataSelectionWithId> _startedSelectionItems;

    #region Test Initialize

    public AdapterSchedule_Tests()
    {
        var mockLogger = new Mock<ILogger>();
        _testScheduler = new Scheduler(mockLogger.Object);
        _testSchedule = new AdapterSchedule<TestDataSelectionWithId>(_testScheduler, TestSampleDataCallback);
    }

    #endregion

    #region Test Cleanup

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Test Methods

    [Fact]
    public void CanStart_NoConfigOrSamplingGroup_Test()
    {
        Assert.False(_testSchedule.CanStart);
    }

    [Fact]
    public void CanStart_ConfigNoPeriod_Test()
    {
        _testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = "id", Period = null, Offset = null });
        _testSchedule.TryUpdateSamplingGroup(new HashSet<TestDataSelectionWithId>() 
        { 
            new TestDataSelectionWithId("id", "schedule1"),
        });

        Assert.False(_testSchedule.CanStart);
    }

    [Fact]
    public void CanStart_NoSamplingGroup_Test()
    {
        _testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = "id", Period = TimeSpan.FromSeconds(1), Offset = null });
        Assert.False(_testSchedule.CanStart);
    }

    [Fact]
    public void CanStart_SamplingGroupEmpty_Test()
    {
        _testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = "id", Period = TimeSpan.FromSeconds(1), Offset = null });
        _testSchedule.TryUpdateSamplingGroup(new HashSet<TestDataSelectionWithId>());
        Assert.False(_testSchedule.CanStart);
    }

    [Fact]
    public void CanStart_ValidConfigAndSamplingGroup_Test()
    {
        _testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = "id", Period = TimeSpan.FromSeconds(1), Offset = null });
        _testSchedule.TryUpdateSamplingGroup(new HashSet<TestDataSelectionWithId>() 
        { 
            new TestDataSelectionWithId("id", "schedule1"),
        });

        Assert.True(_testSchedule.CanStart);
    }

    [Fact]
    public void TryUpdateConfiguration_Null_OriginalNull_Test()
    {
        Assert.False(_testSchedule.TryUpdateConfiguration(null));
    }

    [Fact]
    public void TryUpdateConfiguration_NotNull_OriginalNull_Test()
    {
        Assert.True(_testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = "id", Period = TimeSpan.FromSeconds(1), Offset = null }));
    }

    [Fact]
    public void TryUpdateConfiguration_Null_OriginalNotNull_Test()
    {
        var originalConfig = new ScheduleConfiguration() { Id = "id", Period = TimeSpan.FromSeconds(1), Offset = null };
        Assert.True(_testSchedule.TryUpdateConfiguration(originalConfig));
        Assert.True(_testSchedule.TryUpdateConfiguration(null));
    }

    [Fact]
    public void TryUpdateConfiguration_DifferentConfigs_Test()
    {
        var originalConfig = new ScheduleConfiguration() { Id = "id1", Period = TimeSpan.FromSeconds(1), Offset = null };
        Assert.True(_testSchedule.TryUpdateConfiguration(originalConfig));
        var newConfig = new ScheduleConfiguration() { Id = "id1", Period = TimeSpan.FromSeconds(2), Offset = null };
        Assert.True(_testSchedule.TryUpdateConfiguration(newConfig));
    }

    [Fact]
    public void TryUpdateConfiguration_SameConfigs_Test()
    {
        var originalConfig = new ScheduleConfiguration() { Id = "id1", Period = TimeSpan.FromSeconds(1), Offset = null };
        Assert.True(_testSchedule.TryUpdateConfiguration(originalConfig));
        var newConfig = new ScheduleConfiguration() { Id = "id1", Period = TimeSpan.FromSeconds(1), Offset = null };
        Assert.False(_testSchedule.TryUpdateConfiguration(newConfig));
    }

    [Fact]
    public void TryUpdateSamplingGroup_Null_OriginalNull_Test()
    {
        Assert.False(_testSchedule.TryUpdateSamplingGroup(null));
    }

    [Fact]
    public void TryUpdateSamplingGroup_NotNull_OriginalNull_Test()
    {
        Assert.True(_testSchedule.TryUpdateSamplingGroup(
            new HashSet<TestDataSelectionWithId>() 
            { 
                new TestDataSelectionWithId("id", "schedule1"),
            }));
    }

    [Fact]
    public void TryUpdateSamplingGroup_Null_OriginalNotNull_Test()
    {
        var originalSamplingGroup = new HashSet<TestDataSelectionWithId>() 
        {
            new TestDataSelectionWithId("id", "schedule1"),
        };

        Assert.True(_testSchedule.TryUpdateSamplingGroup(originalSamplingGroup));
        Assert.True(_testSchedule.TryUpdateSamplingGroup(null));
    }

    [Fact]
    public void TryUpdateSamplingGroup_DifferentSamplingGroup_Test()
    {
        var originalSamplingGroup = new HashSet<TestDataSelectionWithId>()
        {
            new TestDataSelectionWithId("id1", "schedule1"),
        };

        Assert.True(_testSchedule.TryUpdateSamplingGroup(originalSamplingGroup));

        var newSamplingGroup = new HashSet<TestDataSelectionWithId>()
        {
            new TestDataSelectionWithId("id2", "schedule1"),
        };
        
        Assert.True(_testSchedule.TryUpdateSamplingGroup(newSamplingGroup));
    }

    [Fact]
    public void TryUpdateSamplingGroup_SameSamplingGroup_Test()
    {
        var originalSamplingGroup = new HashSet<TestDataSelectionWithId>()
        {
            new TestDataSelectionWithId("id1", "schedule1"),
        };

        Assert.True(_testSchedule.TryUpdateSamplingGroup(originalSamplingGroup));

        var newSamplingGroup = new HashSet<TestDataSelectionWithId>()
        {
            new TestDataSelectionWithId("id1", "schedule1"),
        };

        Assert.False(_testSchedule.TryUpdateSamplingGroup(newSamplingGroup));
    }

    [Fact]
    public void StartStop_CannotStart_Test()
    {
        try
        {
            Assert.Throws<InvalidOperationException>(() => _testSchedule.Start());
        }
        finally
        {
            _testSchedule.Stop();
        }
    }

    [Fact]
    public async Task StartStop_Valid_Test()
    {
        try
        {
            var scheduleId = "scheduleId";
            var selection = new TestDataSelectionWithId("selectionId", scheduleId);
            _testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = scheduleId, Period = TimeSpan.FromSeconds(1), Offset = null });
            _testSchedule.TryUpdateSamplingGroup(new HashSet<TestDataSelectionWithId>()
            {
                selection,
            });

            _testSchedule.Start();
            Assert.True(SpinWait.SpinUntil(() => _startedScheduleId != null, 20000));

            Assert.Equal(scheduleId, _startedScheduleId);
            Assert.NotNull(_startedSelectionItems);
            Assert.Single(_startedSelectionItems);
            Assert.Equal(selection.StreamId, _startedSelectionItems[0].StreamId);
            Assert.Equal(selection.ScheduleId, _startedSelectionItems[0].ScheduleId);
        }
        finally
        {
            _testSchedule.Stop();
        }
    }

    [Fact]
    public async Task StartStop_StartTwice_Test()
    {
        try
        {
            var scheduleId = "scheduleId";
            var selection = new TestDataSelectionWithId("selectionId", scheduleId);
            _testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = scheduleId, Period = TimeSpan.FromSeconds(1), Offset = null });
            _testSchedule.TryUpdateSamplingGroup(new HashSet<TestDataSelectionWithId>()
            {
                selection,
            });

            _testSchedule.Start();
            Assert.True(SpinWait.SpinUntil(() => _startedScheduleId != null, 5000));

            Assert.Equal(scheduleId, _startedScheduleId);
            _testSchedule.Start();
        }
        finally
        {
            _testSchedule.Stop();
        }
    }

    [Fact]
    public void Stop_NotStarted_Test()
    {
        var scheduleId = "scheduleId";
        var selection = new TestDataSelectionWithId("selectionId", scheduleId);
        _testSchedule.TryUpdateConfiguration(new ScheduleConfiguration() { Id = scheduleId, Period = TimeSpan.FromSeconds(1), Offset = null });
        _testSchedule.TryUpdateSamplingGroup(new HashSet<TestDataSelectionWithId>()
            {
                selection,
            });
        _testSchedule.Stop();
        Assert.True(_testSchedule.CanStart);
    }

    #endregion

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _testScheduler?.Dispose();
        }

        _disposed = true;
    }

    private Task TestSampleDataCallback<TSelection>(string scheduleId, IReadOnlyList<TSelection> items, CancellationToken cancellationToken)
    {
        _startedScheduleId = scheduleId;
        _startedSelectionItems = (IReadOnlyList<TestDataSelectionWithId>)items;
        return Task.CompletedTask;
    }
}
