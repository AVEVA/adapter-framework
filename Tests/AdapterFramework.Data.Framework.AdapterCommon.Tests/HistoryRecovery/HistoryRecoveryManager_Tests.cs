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
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.HistoryRecovery;

public class HistoryRecoveryManager_Tests
{
    [Fact]
    public void HistoryRecoveryManager_Constructor_Throws_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();

        Assert.ThrowsAny<ArgumentException>(() => new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(null, mockAutomaticHistoryRecoveryProcessor.Object));
        Assert.ThrowsAny<ArgumentException>(() => new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, null));
    }

    [Fact]
    public void HistoryRecoveryManager_Constructor_DoubleDispose_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        var onDemandDisposeCount = 0;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Dispose()).Callback(() => { onDemandDisposeCount += 1; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        var audotmaticDisposeCount = 0;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Dispose()).Callback(() => { audotmaticDisposeCount += 1; });

        var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);

        historyRecoveryManager.Dispose();
        historyRecoveryManager.Dispose();

        Assert.Equal(1, onDemandDisposeCount);
        Assert.Equal(1, audotmaticDisposeCount);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_DataSourceIsNull_BothProcessorStopped_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(null, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_DataSourceIsNull_OnDemandStarted_NoActiveRecovery_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = null;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(null, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.True(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_DataSourceIsNull_OnDemandStarted_ActiveRecovery_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = "Test";
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(null, out var errorMessage);

        Assert.False(result);
        Assert.Contains(activeRecoveryId, errorMessage);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_DataSourceIsNull_AutomaticStarted_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = null;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(null, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.True(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrentOnly_BothProcessorStopped_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentOnly };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrentOnly_OnDemandStarted_NoActiveRecovery_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = null;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentOnly };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.True(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrrentOnly_OnDemandStarted_ActiveRecovery_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = "Test";
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentOnly };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out var errorMessage);

        Assert.False(result);
        Assert.Contains(activeRecoveryId, errorMessage);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Theory]
    [InlineData(true, "HelloWorld")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(false, "HelloWorld")]
    public void HistoryRecoveryManager_IsOnDemandRecoveryInProgress_Test(bool historyOnlyMode, string activeRecoveryId)
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(historyOnlyMode);

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        
        var result = historyRecoveryManager.IsOnDemandRecoveryInProgress();

        if (historyOnlyMode && activeRecoveryId != null)
        {
            Assert.True(result);
        }
        else
        {
            Assert.False(result);
        }
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrentOnly_AutomaticStarted_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = null;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentOnly };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.True(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_HistoryOnly_BothProcessorStopped_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.HistoryOnly };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.True(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_HistoryOnly_OnDemandStarted_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });
        var onDemandDataSourceUpdated = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.UpdateDataSourceConfiguration(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandDataSourceUpdated = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.HistoryOnly };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.True(onDemandDataSourceUpdated);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_HistoryOnly_AutomaticStarted_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });
        var onDemandDataSourceUpdated = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.UpdateDataSourceConfiguration(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandDataSourceUpdated = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.HistoryOnly };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.True(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(onDemandDataSourceUpdated);
        Assert.False(automaticStartCalled);
        Assert.True(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrentWithBackfill_BothProcessorStopped_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentWithBackfill };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.True(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrentWithBackfill_OnDemandStarted_NoActiveRecovery_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = null;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentWithBackfill };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.True(onDemandStopCalled);
        Assert.True(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrentWithBackfill_OnDemandStarted_ActiveRecovery_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        string activeRecoveryId = "Test";
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.ActiveRecoveryId).Returns(activeRecoveryId);
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentWithBackfill };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out var errorMessage);

        Assert.False(result);
        Assert.Contains(activeRecoveryId, errorMessage);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSourceUpdate_CurrentWithBackfill_AutomaticStarted_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Started).Returns(false);
        var onDemandStartCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { onDemandStartCalled = true; });
        var onDemandStopCalled = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { onDemandStopCalled = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Started).Returns(true);
        var automaticStartCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Start(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticStartCalled = true; });
        var automaticStopCalled = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.Stop()).Callback(() => { automaticStopCalled = true; });
        var automaticDataSourceUpdated = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.UpdateDataSourceConfiguration(It.IsAny<IDataSourceConfiguration>())).Callback((IDataSourceConfiguration config) => { automaticDataSourceUpdated = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSourceConfiguration = new TestHistoryDataSourceConfiguration() { DataCollectionMode = DataCollectionMode.CurrentWithBackfill };
        var result = historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfiguration, out _);

        Assert.True(result);
        Assert.False(onDemandStartCalled);
        Assert.False(onDemandStopCalled);
        Assert.True(automaticDataSourceUpdated);
        Assert.False(automaticStartCalled);
        Assert.False(automaticStopCalled);
    }

    [Fact]
    public void HistoryRecoveryManager_ProcessDataSelectionUpdate_Test()
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        var onDemandDataSelectionUpdated = false;
        mockOnDemandHistoryRecoveryProcessor.Setup(x => x.UpdateDataSelectionItems(It.IsAny<IDataSelectionConfiguration[]>())).Callback((IDataSelectionConfiguration[] config) => { onDemandDataSelectionUpdated = true; });

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        var automaticDataSelectionUpdated = false;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.UpdateDataSelectionItems(It.IsAny<IDataSelectionConfiguration[]>())).Callback((IDataSelectionConfiguration[] config) => { automaticDataSelectionUpdated = true; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        var dataSelectionItems = new TestDataSelectionWithId[] { new TestDataSelectionWithId() };
        historyRecoveryManager.ProcessDataSelectionUpdate(dataSelectionItems);

        Assert.True(onDemandDataSelectionUpdated);
        Assert.True(automaticDataSelectionUpdated);
    }

    [Theory]
    [InlineData(FailoverMode.Hot)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public void HistoryRecoveryManager_UpdateFailoverMode_Test(FailoverMode newMode)
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        var updatedFailoverMode = FailoverMode.NotConfigured;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.UpdateFailoverMode(It.IsAny<FailoverMode>())).Callback((FailoverMode mode) => { updatedFailoverMode = mode; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        historyRecoveryManager.UpdateFailoverMode(newMode);

        Assert.Equal(updatedFailoverMode, newMode);
    }

    [Theory]
    [InlineData(FailoverRole.Primary)]
    [InlineData(FailoverRole.Secondary)]
    public void HistoryRecoveryManager_UpdateFailoverRole_Test(FailoverRole newRole)
    {
        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();

        var mockAutomaticHistoryRecoveryProcessor = new Mock<IAutomaticHistoryRecoveryProcessor>();
        var updatedFailoverRole = FailoverRole.PendingPrimary;
        mockAutomaticHistoryRecoveryProcessor.Setup(x => x.UpdateFailoverRole(It.IsAny<FailoverRole>())).Callback((FailoverRole role) => { updatedFailoverRole = role; });

        using var historyRecoveryManager = new HistoryRecoveryManager<TestHistoryDataSourceConfiguration, TestDataSelectionWithId>(mockOnDemandHistoryRecoveryProcessor.Object, mockAutomaticHistoryRecoveryProcessor.Object);
        historyRecoveryManager.UpdateFailoverRole(newRole);

        Assert.Equal(updatedFailoverRole, newRole);
    }
}
