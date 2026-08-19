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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.AdapterCommon.Health;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery.Constants;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.HistoryRecovery;

public class AutomaticHistoryRecoveryProcessor_Tests
{
    private const string ComponentId = "MyComponent1";
    private const string ComponentType = "MyComponentType";
    private const int MaximumDelay = 15_000;
    private const int StatusChangeDelay = 500;
    private static readonly HashSet<DeviceStatus> _automaticHrStates = new()
    {
        DeviceStatus.Shutdown,
        DeviceStatus.DeviceInError,
        DeviceStatus.Removed,
    };

    private readonly TimeSpan _maxAutomaticHistoryIntervalLength = TimeSpan.FromDays(HistoryRecoveryConstants.MaxAutomaticHistoryIntervalLengthDays);

    private delegate void SaveIntervalsCallback(string id, string facet, object intervals, out ICollection<string> errors);
    private delegate void GetIntervalsCallback(string id, string facet, out object intervals, out ICollection<string> errors);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Constructor_Throws_Test(string badComponentId)
    {
        var testLogger = new TestLogger();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockRecoveryFunction = new Mock<Func<Interval, TestDataSourceConfiguration, IReadOnlyList<TestDataSelectionItem>, CancellationToken, Task>>();
        using var mockHealthService = await GetAdapterHealthServiceAsync();

        static DateTime? GetLastReadTime() => DateTime.UtcNow;

        Assert.ThrowsAny<ArgumentException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(badComponentId, testLogger, mockConfigurationProvider.Object, mockHealthService, _automaticHrStates, mockRecoveryFunction.Object, GetLastReadTime));
        Assert.Throws<ArgumentNullException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(ComponentId, null, mockConfigurationProvider.Object, mockHealthService, _automaticHrStates, mockRecoveryFunction.Object, GetLastReadTime));
        Assert.Throws<ArgumentNullException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(ComponentId, testLogger, null, mockHealthService, _automaticHrStates, mockRecoveryFunction.Object, GetLastReadTime));
        Assert.Throws<ArgumentNullException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(ComponentId, testLogger, mockConfigurationProvider.Object, mockHealthService, null, mockRecoveryFunction.Object, GetLastReadTime));
        Assert.Throws<ArgumentNullException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(ComponentId, testLogger, mockConfigurationProvider.Object, null, _automaticHrStates, mockRecoveryFunction.Object, GetLastReadTime));
        Assert.Throws<ArgumentNullException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(ComponentId, testLogger, mockConfigurationProvider.Object, mockHealthService, _automaticHrStates, null, GetLastReadTime));
        Assert.Throws<ArgumentNullException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(ComponentId, testLogger, mockConfigurationProvider.Object, mockHealthService, _automaticHrStates, mockRecoveryFunction.Object, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Empty")]
    [InlineData("ContainsGood")]
    [InlineData("AlsoContainsGood")]
    public async Task Constructor_AutomaticHistoryRecoveryStates_Misconfigured_Throws_Test(string states)
    {
        HashSet<DeviceStatus> autoHrStatusSet = null;
        var testLogger = new TestLogger();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockRecoveryFunction = new Mock<Func<Interval, TestDataSourceConfiguration, IReadOnlyList<TestDataSelectionItem>, CancellationToken, Task>>();
        using var mockHealthService = await GetAdapterHealthServiceAsync();
        static DateTime? GetLastReadTime() => DateTime.UtcNow;

        if (string.Equals(states, "Empty"))
        {
            autoHrStatusSet = new HashSet<DeviceStatus>();
        }
        else if (string.Equals(states, "ContainsGood"))
        {
            autoHrStatusSet = new HashSet<DeviceStatus> { DeviceStatus.Good };
        }
        else if (string.Equals(states, "AlsoContainsGood"))
        {
            autoHrStatusSet = new HashSet<DeviceStatus> { DeviceStatus.Good, DeviceStatus.DeviceInError };
        }

        Assert.ThrowsAny<ArgumentException>(() => new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(ComponentId, testLogger, mockConfigurationProvider.Object, mockHealthService, autoHrStatusSet, mockRecoveryFunction.Object, GetLastReadTime));
    }

    [Fact]
    public async Task DeviceStatusChange_CreatesNewInterval_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);
        mockHealthService.SendDeviceStatus(DeviceStatus.Good);

        Assert.NotEmpty(intervals);
        Assert.True(intervals.Length == 1);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.NotNull(intervals[0].EndTime);
    }

    [Theory]
    [InlineData(FailoverMode.Hot, FailoverRole.Primary)]
    [InlineData(FailoverMode.Hot, FailoverRole.Secondary)]
    [InlineData(FailoverMode.Warm, FailoverRole.Primary)]
    [InlineData(FailoverMode.Warm, FailoverRole.Secondary)]
    [InlineData(FailoverMode.Cold, FailoverRole.Primary)]
    [InlineData(FailoverMode.Cold, FailoverRole.Secondary)]
    public async Task DeviceStatusChange_FailoverEnabled_Shutdown_NoNewIntervalCreated_Test(FailoverMode mode, FailoverRole role)
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        historyProcessor.UpdateFailoverMode(mode);
        historyProcessor.UpdateFailoverRole(role);

        mockHealthService.SendDeviceStatus(DeviceStatus.Shutdown);
        await Task.Delay(StatusChangeDelay);

        Assert.Null(intervals);
    }

    [Fact]
    public async Task DeviceStatusChange_UnlistedStateDoesNotCreateNewInterval_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        var automaticHrStates = new HashSet<DeviceStatus> { DeviceStatus.Shutdown };
        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, automaticHrStates: automaticHrStates);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
        await Task.Delay(StatusChangeDelay);
        mockHealthService.SendDeviceStatus(DeviceStatus.Good);

        Assert.Null(intervals);
    }

    [Fact]
    public async Task DeviceStatusChange_MultipleBadStatusDoesNotCreateNewInterval_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        await Task.Delay(StatusChangeDelay);
        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        await Task.Delay(StatusChangeDelay);
        mockHealthService.SendDeviceStatus(DeviceStatus.ConnectedNoData);
        await Task.Delay(StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);

        mockHealthService.SendDeviceStatus(DeviceStatus.Good);
        SpinWait.SpinUntil(() => intervals.IsEmpty(), MaximumDelay);
    }

    [Fact]
    public async Task LoadIntervals_CapExistingIntervalLength_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var configuredIntervals = new[]
        {
            new Interval { StartTime = DateTime.UtcNow.AddDays(-100), EndTime = DateTime.UtcNow.AddDays(-50), LastReadTime = DateTime.UtcNow.AddDays(-100) },
            new Interval { StartTime = DateTime.UtcNow.AddDays(-10), EndTime = DateTime.UtcNow.AddDays(-1), LastReadTime = DateTime.UtcNow.AddDays(-50) },
            new Interval { StartTime = DateTime.UtcNow.AddDays(-4), EndTime = DateTime.UtcNow.AddDays(-1), LastReadTime = DateTime.UtcNow.AddDays(-1) },
        };

        var initialIntervals = configuredIntervals.ToArray();

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = configuredIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.NotEmpty(intervals);
        Assert.Equal(3, intervals.Length);

        foreach (var interval in intervals)
        {
            Assert.True(interval.EndTime - interval.StartTime <= _maxAutomaticHistoryIntervalLength);
            Assert.True(interval.EndTime - interval.LastReadTime <= _maxAutomaticHistoryIntervalLength);
        }

        Assert.Equal(initialIntervals[0].EndTime, intervals[0].EndTime);
        Assert.Equal(initialIntervals[1].EndTime, intervals[1].EndTime);
        Assert.Equal(initialIntervals[2].StartTime, intervals[2].StartTime);
        Assert.Equal(initialIntervals[2].LastReadTime, intervals[2].LastReadTime);
    }

    [Fact]
    public async Task LoadIntervals_DeviceStatusChange_MaintainExistingIntervals_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var initialIntervals = new[]
        {
            new Interval { StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-1) },
            new Interval { StartTime = DateTime.UtcNow.AddDays(-3), EndTime = DateTime.UtcNow.AddDays(-2) },
        };

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = initialIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        await Task.Delay(StatusChangeDelay);
        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        await Task.Delay(StatusChangeDelay);
        mockHealthService.SendDeviceStatus(DeviceStatus.ConnectedNoData);
        await Task.Delay(StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.True(intervals.Length == 1 + initialIntervals.Length);
    }

    [Fact]
    public async Task LoadIntervals_DiscardBadFormatIntervals_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var startTime = DateTime.UtcNow.AddDays(-5);
        var initialIntervals = new[]
        {
            new Interval { StartTime = startTime },
            new Interval { StartTime = startTime },
            new Interval(),
            new Interval { EndTime = startTime },
        };

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = initialIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.Null(intervals[0].EndTime);
    }

    [Fact]
    public async Task RecoverInterval_DeviceStatus_BadGood_AddsIntervalAndRecovers_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        var lastReadTime = DateTime.UtcNow;

        DateTime? GetLastReadTime() => lastReadTime;
        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true, GetLastReadTime);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.Equal(intervals[0].LastReadTime, lastReadTime);

        mockHealthService.SendDeviceStatus(DeviceStatus.Good);
        SpinWait.SpinUntil(() => intervals.IsEmpty(), MaximumDelay);
    }

    [Fact]
    public async Task RecoverInterval_DeviceStatus_BadGood_GetLastReadTimeThrows_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        static DateTime? GetLastReadTime() => throw new ArgumentNullException(nameof(intervals));
        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true, GetLastReadTime);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.Null(intervals[0].LastReadTime);

        mockHealthService.SendDeviceStatus(DeviceStatus.Good);
        SpinWait.SpinUntil(() => intervals.IsEmpty(), MaximumDelay);
    }

    [Fact]
    public async Task RecoverInterval_ExistingIntervalsAtStart_DeviceStatus_Good_RecoversIntervals_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var initialIntervals = new[]
        {
            new Interval { StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-1) },
            new Interval { StartTime = DateTime.UtcNow.AddDays(-3), EndTime = DateTime.UtcNow.AddDays(-2) },
        };

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = initialIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.NotEmpty(intervals);

        mockHealthService.SendDeviceStatus(DeviceStatus.Good);
        SpinWait.SpinUntil(() => intervals.IsEmpty(), MaximumDelay * initialIntervals.Length);
    }

    [Fact]
    public async Task RecoverInterval_ExistingIntervalsAtStart_DeviceStatus_Bad_DoesNotTryToRecoverInterval_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var initialIntervals = new[]
        {
            new Interval { StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-1) },
            new Interval { StartTime = DateTime.UtcNow.AddDays(-3), EndTime = DateTime.UtcNow.AddDays(-2) },
        };

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = initialIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.NotEmpty(intervals);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > initialIntervals.Length, MaximumDelay);

        Assert.NotEmpty(intervals);
        Assert.Equal(initialIntervals.Length + 1, intervals.Length);
    }

    [Fact]
    public async Task RecoverInterval_DeviceStatus_BadGoodBad_CancelsRecovery_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.Null(intervals);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        Assert.NotEmpty(intervals);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        await Task.Delay(StatusChangeDelay);

        mockHealthService.SendDeviceStatus(DeviceStatus.Good);
        await Task.Delay(StatusChangeDelay);
        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        await Task.Delay(StatusChangeDelay);

        Assert.NotEmpty(intervals);
    }

    [Fact]
    public async Task RecoverInterval_ExistingIntervalsAtStart_RecoverIntervalsInOrder_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var firstStartTime = DateTime.UtcNow.AddDays(-3);
        var initialIntervals = new[]
        {
            new Interval { StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-1) },
            new Interval { StartTime = firstStartTime, EndTime = DateTime.UtcNow.AddDays(-2) },
        };

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = initialIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.NotEmpty(intervals);

        mockHealthService.SendDeviceStatus(DeviceStatus.Good);

        SpinWait.SpinUntil(() => intervals.Length == initialIntervals.Length - 1, MaximumDelay);

        Assert.NotEmpty(intervals);
        Assert.Equal(initialIntervals.Length - 1, intervals.Length);
        Assert.NotEqual(firstStartTime, intervals.First().StartTime);
    }

    [Fact]
    public async Task RecoverInterval_ExistingIntervalsAtStart_BadStatusContinuesIntervalThenRecovers_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var initialIntervals = new[]
        {
            new Interval { StartTime = DateTime.UtcNow.AddDays(-10) },
        };

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = initialIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.NotEmpty(intervals);

        mockHealthService.SendDeviceStatus(GetAutomaticHrIntervalState());
        await Task.Delay(StatusChangeDelay);

        Assert.Single(intervals);

        mockHealthService.SendDeviceStatus(DeviceStatus.Good);
        await Task.Delay(StatusChangeDelay);

        Assert.Single(intervals);
        Assert.True(intervals[0].EndTime - intervals[0].StartTime <= _maxAutomaticHistoryIntervalLength);

        SpinWait.SpinUntil(() => intervals.IsEmpty(), MaximumDelay);
    }

    [Fact]
    public async Task LoadIntervals_SortsExistingIntervals_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        var firstStartTime = DateTime.UtcNow.AddDays(-3);

        var initialIntervals = new[]
        {
            new Interval { StartTime = DateTime.UtcNow.AddDays(-2), EndTime = DateTime.UtcNow.AddDays(-1) },
            new Interval { StartTime = firstStartTime, EndTime = DateTime.UtcNow.AddDays(-2) },
        };

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = initialIntervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService, true);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        Assert.NotEmpty(intervals);
        Assert.Equal(initialIntervals.Length, intervals.Length);
        Assert.Equal(firstStartTime, intervals.First().StartTime);
    }

    [Theory]
    [InlineData(FailoverMode.Hot)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public async Task UpdateFailoverMode_NoneToAny_SecondaryRole_OpenIntervalClosed_Test(FailoverMode mode)
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);

        historyProcessor.UpdateFailoverMode(mode);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.NotNull(intervals[0].EndTime);
    }

    [Theory]
    [InlineData(FailoverMode.Hot)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public async Task UpdateFailoverMode_NoneToAny_PrimaryRole_OpenIntervalNotClosed_Test(FailoverMode mode)
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);

        historyProcessor.UpdateFailoverRole(FailoverRole.Primary);
        historyProcessor.UpdateFailoverMode(mode);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);
    }

    [Theory]
    [InlineData(FailoverMode.Hot)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public async Task UpdateFailoverMode_AnyToNone_SecondaryRole_DeviceErrorCapturedAgain_Test(FailoverMode mode)
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        mockHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        historyProcessor.UpdateFailoverMode(mode);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.NotNull(intervals[0].EndTime);

        historyProcessor.UpdateFailoverMode(FailoverMode.NotConfigured);

        Assert.Equal(2, intervals.Length);
        Assert.NotNull(intervals[1].StartTime);
        Assert.NotNull(intervals[1].LastReadTime);
        Assert.Null(intervals[1].EndTime);
    }

    [Theory]
    [InlineData(FailoverMode.Hot)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public async Task UpdateFailoverRole_PrimaryToSecondary_AnyMode_OpenIntervalClosed_Test(FailoverMode mode)
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        historyProcessor.UpdateFailoverMode(mode);
        historyProcessor.UpdateFailoverRole(FailoverRole.Primary);

        mockHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);

        historyProcessor.UpdateFailoverRole(FailoverRole.Secondary);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.NotNull(intervals[0].EndTime);
    }

    [Fact]
    public async Task UpdateFailoverRole_PrimaryToSecondary_NoneMode_OpenIntervalNotClosed_Test()
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        historyProcessor.UpdateFailoverRole(FailoverRole.Primary);
        historyProcessor.UpdateFailoverMode(FailoverMode.NotConfigured);

        mockHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
        SpinWait.SpinUntil(() => intervals != null && intervals.Length > 0, StatusChangeDelay);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);

        historyProcessor.UpdateFailoverRole(FailoverRole.Secondary);

        Assert.NotEmpty(intervals);
        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);
    }

    [Theory]
    [InlineData(FailoverMode.Hot)]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public async Task UpdateFailoverRole_SecondaryToPrimary_AnyMode_DeviceErrorCapturedAgain_Test(FailoverMode mode)
    {
        ICollection<string> outErrors = new List<string>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        Interval[] intervals = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out outErrors))
            .Callback(new SaveIntervalsCallback((string id, string facet, object intervalsCallback, out ICollection<string> errors) =>
            {
                intervals = (Interval[])intervalsCallback;
                errors = new List<string>();
            }))
            .Returns(true);

        mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out intervals, out outErrors))
            .Callback(new GetIntervalsCallback((string id, string facet, out object intervalsCallback, out ICollection<string> errors) =>
            {
                intervalsCallback = intervals;
                errors = outErrors;
            }))
            .Returns(true);

        using var mockHealthService = await GetAdapterHealthServiceAsync(true);
        using var historyProcessor = await GetHistoryRecoveryProcessor(mockConfigurationProvider, mockHealthService);
        var dataSourceConfiguration = new TestDataSourceConfiguration();

        historyProcessor.Start(dataSourceConfiguration);

        historyProcessor.UpdateFailoverMode(mode);
        historyProcessor.UpdateFailoverRole(FailoverRole.Secondary);

        mockHealthService.SendDeviceStatus(DeviceStatus.DeviceInError);
        await Task.Delay(StatusChangeDelay);

        Assert.Null(intervals);

        historyProcessor.UpdateFailoverRole(FailoverRole.Primary);

        Assert.Single(intervals);
        Assert.NotNull(intervals[0].StartTime);
        Assert.NotNull(intervals[0].LastReadTime);
        Assert.Null(intervals[0].EndTime);
    }

    private static async Task<AdapterHealthService> GetAdapterHealthServiceAsync(bool initialize = false)
    {
        var logger = new TestLogger();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        var mockHealthService = new AdapterHealthService(
            mockHealthMessageProcessor.Object,
            logger,
            mockApplicationManifest.Object,
            ComponentId,
            ComponentType,
            "MyProduct",
            "1.1.1.1");

        if (initialize)
        {
            await mockHealthService.InitializeAsync();
        }

        return mockHealthService;
    }

    private static async Task<AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>> GetHistoryRecoveryProcessor(
        IMock<IConfigurationProvider> mockConfigurationProvider = null, AdapterHealthService mockHealthService = null, bool setupRecoveryTask = false, Func<DateTime?> getLastTimestampFunc = null, HashSet<DeviceStatus> automaticHrStates = null)
    {
        mockConfigurationProvider ??= new Mock<IConfigurationProvider>();
#pragma warning disable CA2000 // Dispose objects before losing scope
        mockHealthService ??= await GetAdapterHealthServiceAsync();
#pragma warning restore CA2000 // Dispose objects before losing scope
        getLastTimestampFunc ??= () => DateTime.UtcNow;
        automaticHrStates ??= _automaticHrStates;

        var logger = new TestLogger();
        var mockRecoveryFunction = new Mock<Func<Interval, TestDataSourceConfiguration, IReadOnlyList<TestDataSelectionItem>, CancellationToken, Task>>();

        if (setupRecoveryTask)
        {
            mockRecoveryFunction.Setup(rf => rf.Invoke(It.IsAny<Interval>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionItem>>(), It.IsAny<CancellationToken>()))
            .Returns((Interval interval, TestDataSourceConfiguration dataSource, IReadOnlyList<TestDataSelectionItem> dataSelection, CancellationToken ct) => Task.Delay(200, ct));
        }

        return new AutomaticHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionItem>(
            ComponentId, logger, mockConfigurationProvider.Object, mockHealthService, automaticHrStates, mockRecoveryFunction.Object, getLastTimestampFunc);
    }

    private static DeviceStatus GetAutomaticHrIntervalState()
    {
        var rand = new Random();
        return _automaticHrStates.ElementAt(rand.Next(_automaticHrStates.Count));
    }
}
