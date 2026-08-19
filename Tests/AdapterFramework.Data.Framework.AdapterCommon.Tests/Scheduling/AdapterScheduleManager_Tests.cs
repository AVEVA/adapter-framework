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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.AdapterCommon.Scheduling;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Scheduling;

public class AdapterScheduleManager_Tests : IDisposable
{
    private const int SpinWaitTimeoutMs = 30000;

    private readonly AdapterScheduleManager<TestDataSelectionWithId> _scheduleManager;
    private readonly TestDataSelectionWithId[] _testSelectionConfig = new[]
    {
        new TestDataSelectionWithId("Selection_1_1", "Schedule_1"),
        new TestDataSelectionWithId("Selection_1_2", "Schedule_1"),
        new TestDataSelectionWithId("Selection_1_3", null),
        new TestDataSelectionWithId("Selection_1_4", "Schedule_1", false),

        new TestDataSelectionWithId("Selection_2_1", "Schedule_2"),
        new TestDataSelectionWithId("Selection_2_2", "Schedule_2"),
        new TestDataSelectionWithId("Selection_2_3", null),
        new TestDataSelectionWithId("Selection_2_4", "Schedule_2", false),
    };
    private ConcurrentDictionary<string, IReadOnlyList<TestDataSelectionWithId>> _startedSchedules;
    private bool _disposed;

    #region Test Initialize

    public AdapterScheduleManager_Tests()
    {
        var mockLogger = new Mock<ILogger>();
        _scheduleManager = new AdapterScheduleManager<TestDataSelectionWithId>(mockLogger.Object, TestSampleDataCallback);
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
    public void ProcessDataSelectionChanges_NoSchedulesConfig_Create_Test()
    {
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessDataSelectionChanges_HasSchedulesConfig_Create_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        Assert.Null(_startedSchedules);

        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 1, SpinWaitTimeoutMs));
        Assert.Single(_startedSchedules);
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);
    }

    [Fact]
    public void ProcessDataSelectionChanges_NoSchedulesConfig_Update_Test()
    {
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        var newTestSelectionConfig = new[]
        {
            new TestDataSelectionWithId("Selection_1_1", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_2", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_3_new", null),
            new TestDataSelectionWithId("Selection_1_4_new", "Schedule_1", false),

            new TestDataSelectionWithId("Selection_2_1_new", "Schedule_2"),
            new TestDataSelectionWithId("Selection_2_2_new", "Schedule_2"),
            new TestDataSelectionWithId("Selection_2_3_new", null),
            new TestDataSelectionWithId("Selection_2_4_new", "Schedule_2", false),

            new TestDataSelectionWithId("Selection_3_1", "Schedule_3"),
            new TestDataSelectionWithId("Selection_3_2", "Schedule_3"),
            new TestDataSelectionWithId("Selection_3_3", null),
            new TestDataSelectionWithId("Selection_3_4", "Schedule_3", false),
        };

        _scheduleManager.ProcessDataSelectionChanges(newTestSelectionConfig);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessDataSelectionChanges_HasSchedulesConfig_Update_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        Assert.Null(_startedSchedules);

        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null, SpinWaitTimeoutMs));
        Assert.Single(_startedSchedules);
        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(2, _startedSchedules["Schedule_2"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_2_1", selectionIdSet);
        Assert.Contains("Selection_2_2", selectionIdSet);

        var newTestSelectionConfig = new[]
        {
            new TestDataSelectionWithId("Selection_1_1", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_2", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_3_new", null),
            new TestDataSelectionWithId("Selection_1_4_new", "Schedule_1", false),

            new TestDataSelectionWithId("Selection_2_1_new", "Schedule_2"),
            new TestDataSelectionWithId("Selection_2_2_new", "Schedule_2"),
            new TestDataSelectionWithId("Selection_2_3_new", null),
            new TestDataSelectionWithId("Selection_2_4_new", "Schedule_2"),

            new TestDataSelectionWithId("Selection_3_1", "Schedule_3"),
            new TestDataSelectionWithId("Selection_3_2", "Schedule_3"),
            new TestDataSelectionWithId("Selection_3_3", null),
            new TestDataSelectionWithId("Selection_3_4", "Schedule_3", false),
        };

        _scheduleManager.ProcessDataSelectionChanges(newTestSelectionConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules["Schedule_2"].Count == 3, SpinWaitTimeoutMs));
        Assert.Single(_startedSchedules);
        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(3, _startedSchedules["Schedule_2"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(3, selectionIdSet.Count);
        Assert.Contains("Selection_2_1_new", selectionIdSet);
        Assert.Contains("Selection_2_2_new", selectionIdSet);
        Assert.Contains("Selection_2_4_new", selectionIdSet);
    }

    [Fact]
    public void ProcessDataSelectionChanges_NoSchedulesConfig_PartialDelete_Test()
    {
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        var newTestSelectionConfig = new[]
        {
            new TestDataSelectionWithId("Selection_1_1", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_2", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_3", null),
            new TestDataSelectionWithId("Selection_1_4", "Schedule_1", false),
        };

        _scheduleManager.ProcessDataSelectionChanges(newTestSelectionConfig);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessDataSelectionChanges_HasSchedulesConfig_PartialDelete_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        Assert.Null(_startedSchedules);

        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 2, SpinWaitTimeoutMs));

        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);

        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(2, _startedSchedules["Schedule_2"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_2_1", selectionIdSet);
        Assert.Contains("Selection_2_2", selectionIdSet);

        var newTestSelectionConfig = new[]
        {
            new TestDataSelectionWithId("Selection_1_1", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_2", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_3", null),
            new TestDataSelectionWithId("Selection_1_4", "Schedule_1", false),
        };

        _scheduleManager.ProcessDataSelectionChanges(newTestSelectionConfig);

        _startedSchedules = null;

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null, SpinWaitTimeoutMs));

        Assert.Single(_startedSchedules);
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);
    }

    [Fact]
    public void ProcessDataSelectionChanges_NoSchedulesConfig_Delete_Test()
    {
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);
        _scheduleManager.ProcessDataSelectionChanges(null);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessDataSelectionChanges_HasSchedulesConfig_Delete_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        Assert.Null(_startedSchedules);

        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 2, SpinWaitTimeoutMs));
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);

        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(2, _startedSchedules["Schedule_2"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_2_1", selectionIdSet);
        Assert.Contains("Selection_2_2", selectionIdSet);

        _scheduleManager.ProcessDataSelectionChanges(null);

        _startedSchedules = null;

        await Task.Delay(500);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public void ProcessSchedulesConfigurationChanges_NoSamplingGroups_Create_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessSchedulesConfigurationChanges_HasSamplingGroups_Create_Test()
    {
        var testSelectionConfig = new[]
        {
            new TestDataSelectionWithId("Selection_1_1", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_2", "Schedule_1"),
            new TestDataSelectionWithId("Selection_1_3", null),
            new TestDataSelectionWithId("Selection_1_4", "Schedule_1", false),
        };

        _scheduleManager.ProcessDataSelectionChanges(testSelectionConfig);
        Assert.Null(_startedSchedules);

        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 1, SpinWaitTimeoutMs));
        Assert.Single(_startedSchedules);
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);
    }

    [Fact]
    public void ProcessSchedulesConfigurationChanges_NoSamplingGroups_Update_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);

        var newTestSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2_new", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_3", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(newTestSchedulesConfig);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessSchedulesConfigurationChanges_HasSamplingGroups_Update_Test()
    {
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.Null(_startedSchedules);

        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count > 1, SpinWaitTimeoutMs));
        Assert.Equal(2, _startedSchedules.Count);
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);

        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(2, _startedSchedules["Schedule_2"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_2_1", selectionIdSet);
        Assert.Contains("Selection_2_2", selectionIdSet);

        var newTestSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(newTestSchedulesConfig);

        _startedSchedules = null;

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 1, SpinWaitTimeoutMs));
        Assert.Single(_startedSchedules);
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);
    }

    [Fact]
    public void ProcessSchedulesConfigurationChanges_NoSamplingGroup_PartialDelete_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);

        var newTestSchedulesConfig = new[]
        {                
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(newTestSchedulesConfig);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessSchedulesConfigurationChanges_HasSamplingGroups_PartialDelete_Test()
    {
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);
        Assert.Null(_startedSchedules);

        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 2, SpinWaitTimeoutMs));

        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);

        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(2, _startedSchedules["Schedule_2"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_2_1", selectionIdSet);
        Assert.Contains("Selection_2_2", selectionIdSet);

        var newTestSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(newTestSchedulesConfig);

        _startedSchedules = null;

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 1, SpinWaitTimeoutMs));
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);
    }

    [Fact]
    public void ProcessSchedulesConfigurationChanges_NoSamplingGroups_Delete_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        _scheduleManager.ProcessSchedulesConfigurationChanges(null);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ProcessSchedulesConfigurationChanges_HasSamplingGroups_Delete_Test()
    {
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);
        Assert.Null(_startedSchedules);

        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_1", Period = TimeSpan.FromSeconds(1), Offset = null },
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null && _startedSchedules.Count == 2, SpinWaitTimeoutMs));
        Assert.Equal(2, _startedSchedules.Count);
        Assert.True(_startedSchedules.ContainsKey("Schedule_1"));
        Assert.Equal(2, _startedSchedules["Schedule_1"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_1"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_1_1", selectionIdSet);
        Assert.Contains("Selection_1_2", selectionIdSet);

        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(2, _startedSchedules["Schedule_2"].Count);
        selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_2_1", selectionIdSet);
        Assert.Contains("Selection_2_2", selectionIdSet);

        _scheduleManager.ProcessSchedulesConfigurationChanges(null);

        _startedSchedules = null;

        await Task.Delay(500);
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public void ClearAllSchedules_NoOngoingSchedules_Test()
    {
        _scheduleManager.ClearAllSchedules();
        Assert.Null(_startedSchedules);
    }

    [Fact]
    public async Task ClearAllSchedules_HasOngoingSchedules_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        Assert.Null(_startedSchedules);

        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null, SpinWaitTimeoutMs));
        Assert.Single(_startedSchedules);
        Assert.True(_startedSchedules.ContainsKey("Schedule_2"));
        Assert.Equal(2, _startedSchedules["Schedule_2"].Count);
        var selectionIdSet = new HashSet<string>();
        foreach (var selection in _startedSchedules["Schedule_2"])
        {
            selectionIdSet.Add(selection.StreamId);
        }

        Assert.Equal(2, selectionIdSet.Count);
        Assert.Contains("Selection_2_1", selectionIdSet);
        Assert.Contains("Selection_2_2", selectionIdSet);

        _startedSchedules = null;

        _scheduleManager.ClearAllSchedules();

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules == null, SpinWaitTimeoutMs));
    }

    [Fact]
    public void Enable_StartsAllSchedules_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        // Disable since default is Enabled
        _scheduleManager.Disable();

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);

        Assert.False(_scheduleManager.Enabled);
        Assert.Null(_startedSchedules);

        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);
        Assert.Null(_startedSchedules); // started schedules null until we re-enable

        _scheduleManager.Enable();
        Assert.True(_scheduleManager.Enabled);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null, SpinWaitTimeoutMs));
        Assert.NotNull(_startedSchedules);
    }

    [Fact]
    public async Task Disable_StopsAllSchedules_Test()
    {
        var testSchedulesConfig = new[]
        {
            new ScheduleConfiguration() { Id = "Schedule_2", Period = TimeSpan.FromSeconds(1), Offset = null },
        };

        // ensure default is enabled
        Assert.True(_scheduleManager.Enabled);

        _scheduleManager.ProcessSchedulesConfigurationChanges(testSchedulesConfig);
        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        Assert.True(SpinWait.SpinUntil(() => _startedSchedules != null, SpinWaitTimeoutMs));

        // reset startedSchedules so we can make sure start is not getting called after we disable
        _startedSchedules = null;
        _scheduleManager.Disable();
        Assert.False(_scheduleManager.Enabled);

        _scheduleManager.ProcessDataSelectionChanges(_testSelectionConfig);

        // Ensure the callback method isn't getting hit after a delay and _startedSchedules remains null
        await Task.Delay(500);
        Assert.Null(_startedSchedules);
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
            _scheduleManager?.Dispose();
        }

        _disposed = true;
    }

    private Task TestSampleDataCallback<TSelection>(string scheduleId, IReadOnlyList<TSelection> items, CancellationToken cancellationToken)
    {
        if (_startedSchedules == null)
        {
            _startedSchedules = new ConcurrentDictionary<string, IReadOnlyList<TestDataSelectionWithId>>();
        }

        _startedSchedules[scheduleId] = (IReadOnlyList<TestDataSelectionWithId>)items;
        return Task.CompletedTask;
    }
}
