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
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Common;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.AdapterCommon.Discovery;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;
using static System.Net.HttpStatusCode;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Discovery;

public class DataSourceDiscoveryManager_Tests
{
    private const string ComponentId = "ComponentId";
    private const string DiscoveryId = "DiscoveryId";
    private const string DiscoveryIdA = "SampleDiscoveryIdA";
    private const string DiscoveryIdB = "SampleDiscoveryIdB";
    private const string StreamIdBase = "StreamId.";
    private const string StreamIdBase1 = "StreamId.";
    private const string StreamIdBase2 = "NotStreamId.";
    private const string BaseAddress = "http://localhost:5590";
    private const string DataSelectionString = "data selection";
    private const string OnlyOneDiscoveryPermitted = "Only one active discovery operation is permitted at a time";
    private const int DiscoveryStateChangeTimeout = 10000;

    private ConfigurationChangedEventArgs _dataSelectionUpdateArguments;
    private delegate void SaveDiscoveryStateConfigCallback(string id, string facet, DiscoveryState[] configs, out ICollection<string> errors);
    private delegate void TrySaveDiscoveryResultConfigCallback(string id, string discoveryId, TestDataSelectionWithId[] discoveryConfig, out ICollection<string> errors);

    [Theory]
    [InlineData(null, BaseAddress)]
    [InlineData("", BaseAddress)]
    [InlineData(" ", BaseAddress)]
    [InlineData(ComponentId, null)]
    [InlineData(ComponentId, "")]
    [InlineData(ComponentId, " ")]
    public void Constructor_Throws_Test(string componentId, string baseAddress)
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockLogger = new Mock<ILogger>();
        var mockDataSourceRecoveryFunction = new Mock<Func<string, TestDataSourceConfiguration, DataSourceDiscoveryService<TestDataSelectionWithId>, CancellationToken, Task>>();
        var mockDataSelectionUpdateFunction = new Mock<Action<ConfigurationChangedEventArgs>>();
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        Assert.ThrowsAny<Exception>(() => CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(componentId, baseAddress, null, out _, out _, out _, out _));
        Assert.Throws<ArgumentNullException>(() => new DataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, mockLogger.Object, mockDataSourceRecoveryFunction.Object, mockDataSelectionUpdateFunction.Object, mockGetDataSourceFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new DataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, mockConfigurationProvider.Object, null, mockDataSourceRecoveryFunction.Object, mockDataSelectionUpdateFunction.Object, mockGetDataSourceFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new DataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, mockConfigurationProvider.Object, mockLogger.Object, null, mockDataSelectionUpdateFunction.Object, mockGetDataSourceFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new DataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, mockConfigurationProvider.Object, mockLogger.Object, mockDataSourceRecoveryFunction.Object, null, mockGetDataSourceFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new DataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, mockConfigurationProvider.Object, mockLogger.Object, mockDataSourceRecoveryFunction.Object, mockDataSelectionUpdateFunction.Object, null));
    }

    [Theory]
    [InlineData(null, 0, 100)]
    [InlineData("", 0, 100)]
    [InlineData(" ", 0, 100)]
    [InlineData(DiscoveryId, -1, 100)]
    [InlineData(DiscoveryId, 10, -1)]
    public void GetDiscoveryResult_Invalid_Input_Test(string discoveryId, int skip, int count)
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);

        Assert.ThrowsAny<Exception>(() => discoveryManager.GetDiscoveryResult(discoveryId, new DiscoveryOptions(count, skip)));
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(10, 1, 11, 1)]
    [InlineData(100, 100, 200, 100)]
    [InlineData(10, 10, 11, 1)]
    [InlineData(0, 20, 11, 11)]
    [InlineData(20, 100, 100, 80)]
    public void GetDiscoveryResult_Count_Matches_ExpectedCount(int skip, int count, int discoveredCount, int expectedCount)
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryId },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryResult = CreateDiscoveryResult(discoveredCount, StreamIdBase);
        ICollection<string> errors = new List<string>();
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryId, out discoveryResult, out errors)).Returns(true);

        var mvcResult = discoveryManager.GetDiscoveryResult(DiscoveryId, new DiscoveryOptions(count, skip));

        Assert.True(mvcResult.StatusCode.Equals((int)OK));
        Assert.NotNull(mvcResult.Content);
        var content = (TestDataSelectionWithId[])mvcResult.Content;
        Assert.True(content.Length.Equals(expectedCount));
    }

    [Theory]
    [InlineData(101, 100, 0)]
    [InlineData(10, 2, 5)]
    [InlineData(100, 100, 40)]
    public void GetDiscoveryResult_Outside_Of_Range(int skip, int count, int discoveredCount)
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryResult = CreateDiscoveryResult(discoveredCount, StreamIdBase);
        ICollection<string> errors = new List<string>();
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryId, out discoveryResult, out errors)).Returns(true);

        var mvcResult = discoveryManager.GetDiscoveryResult(DiscoveryId, new DiscoveryOptions(count, skip));

        Assert.True(mvcResult.StatusCode.Equals((int)NotFound));
        Assert.Contains(DiscoveryId, ((RestApiErrorResponse)mvcResult.Content).Error);
    }

    [Theory]
    [InlineData(null, DiscoveryIdB)]
    [InlineData("", DiscoveryIdB)]
    [InlineData(" ", DiscoveryIdB)]
    [InlineData(DiscoveryIdA, null)]
    [InlineData(DiscoveryIdA, "")]
    [InlineData(DiscoveryIdA, " ")]
    public void GetDiscoveriesDifference_InvalidInput_Test(string discoveryIdA, string discoveryIdB)
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        Assert.ThrowsAny<Exception>(() => discoveryManager.GetDiscoveriesDifference(discoveryIdA, discoveryIdB));
    }

    [Fact]
    public void GetDiscoveriesDifference_DiscoveryResultNotFound_Test()
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA }, new DiscoveryState { Id = DiscoveryIdB },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryResult = CreateDiscoveryResult(10, StreamIdBase);
        ICollection<string> errors = new List<string>();

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryResult, out errors)).Returns(false);

        var result = discoveryManager.GetDiscoveriesDifference(DiscoveryIdA, DiscoveryIdB);

        Assert.Equal((int)NotFound, result.StatusCode);
        Assert.Contains(DiscoveryIdA, ((RestApiErrorResponse)result.Content).Error);

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdB, out discoveryResult, out errors)).Returns(false);

        result = discoveryManager.GetDiscoveriesDifference(DiscoveryIdA, DiscoveryIdB);

        Assert.Equal((int)NotFound, result.StatusCode);
        Assert.Contains(DiscoveryIdB, ((RestApiErrorResponse)result.Content).Error);
    }

    [Fact]
    public void GetDiscoveriesDifference_DiscoveryResultInvalid_Test()
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA }, new DiscoveryState { Id = DiscoveryIdB },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryResult = CreateDiscoveryResult(10, StreamIdBase);
        ICollection<string> errors = new List<string> { "Discovery file is terribly corrupted." };

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryResult, out errors)).Returns(false);

        var result = discoveryManager.GetDiscoveriesDifference(DiscoveryIdA, DiscoveryIdB);

        Assert.Equal((int)InternalServerError, result.StatusCode);
        Assert.Contains(DiscoveryIdA, ((RestApiErrorResponse)result.Content).Error);

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdB, out discoveryResult, out errors)).Returns(false);

        result = discoveryManager.GetDiscoveriesDifference(DiscoveryIdA, DiscoveryIdB);

        Assert.Equal((int)InternalServerError, result.StatusCode);
        Assert.Contains(DiscoveryIdB, ((RestApiErrorResponse)result.Content).Error);
    }

    [Fact]
    public void GetDiscoveriesDifference_Test()
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA }, new DiscoveryState { Id = DiscoveryIdB },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryResultA = CreateDiscoveryResult(10, StreamIdBase);
        var discoveryResultB = CreateDiscoveryResult(100, StreamIdBase);
        ICollection<string> errors = new List<string>();

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryResultA, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdB, out discoveryResultB, out errors)).Returns(true);

        var result = discoveryManager.GetDiscoveriesDifference(DiscoveryIdA, DiscoveryIdB);

        Assert.Equal((int)OK, result.StatusCode);
        Assert.Equal(discoveryResultB.Length - discoveryResultA.Length, ((IEnumerable<TestDataSelectionWithId>)result.Content).Count());

        result = discoveryManager.GetDiscoveriesDifference(DiscoveryIdB, DiscoveryIdA);

        Assert.Equal((int)OK, result.StatusCode);
        Assert.Empty((IEnumerable<TestDataSelectionWithId>)result.Content);
    }

    #region MergeWithDataSelection

    [Theory]
    [InlineData(false, StreamIdBase1, 20, StreamIdBase1, 10, 20)]
    [InlineData(false, StreamIdBase1, 20, StreamIdBase2, 10, 30)]
    [InlineData(true, StreamIdBase1, 20, StreamIdBase1, 10, 20)]
    [InlineData(true, StreamIdBase1, 20, StreamIdBase2, 10, 30)]
    public void MergeWithDataSelection_Test(bool selected, string dataSelectionStreamIdBase, int dataSelectionCount, string discoveryStreamIdBase, int discoveryCount, int expectedFinalCount)
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out var mockDataSelectionUpdateAction);

        var discoveryResult = CreateDiscoveryResult(discoveryCount, discoveryStreamIdBase);
        var dataSelectionResult = CreateDiscoveryResult(dataSelectionCount, dataSelectionStreamIdBase);
        TestDataSelectionWithId[] persistedConfiguration = null;
        ICollection<string> errors = new List<string>();

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName, out dataSelectionResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TrySaveConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName, It.IsAny<TestDataSelectionWithId[]>(), out errors))
            .Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId, TestDataSelectionWithId[] configuration, out ICollection<string> errorMessages) =>
            {
                errorMessages = null;
                persistedConfiguration = configuration;
            })).Returns(true);

        var result = discoveryManager.MergeWithDataSelection(DiscoveryIdA, selected);

        Assert.Equal((int)OK, result.StatusCode);
        Assert.Equal(((MergeOperationResult)result.Content).StreamCount, expectedFinalCount);
        Assert.Equal(((MergeOperationResult)result.Content).StreamsAdded, expectedFinalCount - dataSelectionCount);
        Assert.Equal(expectedFinalCount, persistedConfiguration.Length);

        var selectedCount = persistedConfiguration.Count(dataSelectionItem => dataSelectionItem.Selected);

        // Selected is false for all items to start. We only want to set to true if the argument is true but we do not want to replace a value of false on existing items.
        if (selected && dataSelectionStreamIdBase != discoveryStreamIdBase)
        {
            Assert.Equal(discoveryCount, selectedCount);
        }
        else
        {
            Assert.Equal(0, selectedCount);
        }

        mockConfigurationProvider.Verify(configurationProvider => configurationProvider.TrySaveConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName, It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny), Times.Once());
        mockDataSelectionUpdateAction.Verify(a => a(It.IsAny<ConfigurationChangedEventArgs>()), Times.Once);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData(" ", true)]
    public void MergeWithDataSelection_InvalidInput_Test(string discoveryIdA, bool selected)
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        Assert.ThrowsAny<Exception>(() => discoveryManager.MergeWithDataSelection(discoveryIdA, selected));
    }

    [Fact]
    public void MergeWithDataSelection_DiscoveryResultNotFound_Test()
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryOrDataSelectionResult = CreateDiscoveryResult(10, StreamIdBase1);
        ICollection<string> errors = new List<string>();

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TrySaveConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
            It.IsAny<TestDataSelectionWithId[]>(), out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(false);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out discoveryOrDataSelectionResult, out errors)).Returns(true);

        var result = discoveryManager.MergeWithDataSelection(DiscoveryIdA, false);

        Assert.Equal((int)NotFound, result.StatusCode);
        Assert.Contains(DiscoveryIdA, ((RestApiErrorResponse)result.Content).Error);

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
            out discoveryOrDataSelectionResult, out errors)).Returns(false);

        result = discoveryManager.MergeWithDataSelection(DiscoveryIdA, false);

        Assert.Equal((int)OK, result.StatusCode);
        Assert.Equal(((MergeOperationResult)result.Content).StreamCount, discoveryOrDataSelectionResult.Length);
        Assert.Equal(((MergeOperationResult)result.Content).StreamsAdded, discoveryOrDataSelectionResult.Length);

        mockConfigurationProvider.Verify(configurationProvider => configurationProvider.TrySaveConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName, It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny), Times.Once());
    }

    [Fact]
    public void MergeWithDataSelection_DiscoveryOrDataSelectionResultInvalid_Test()
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA }, new DiscoveryState { Id = DiscoveryIdB },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryOrDataSelectionResult = CreateDiscoveryResult(10, StreamIdBase1);
        ICollection<string> errors = new List<string> { "Discovery file is terribly corrupted." };

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(false);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out discoveryOrDataSelectionResult, out errors)).Returns(true);

        var result = discoveryManager.MergeWithDataSelection(DiscoveryIdA, true);

        Assert.Equal((int)InternalServerError, result.StatusCode);
        Assert.Contains(DiscoveryIdA, ((RestApiErrorResponse)result.Content).Error);

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out discoveryOrDataSelectionResult, out errors)).Returns(false);

        result = discoveryManager.MergeWithDataSelection(DiscoveryIdA, true);

        Assert.Equal((int)InternalServerError, result.StatusCode);
        Assert.Contains(DataSelectionString, ((RestApiErrorResponse)result.Content).Error);
    }

    #endregion

    [Theory]
    [InlineData(StreamIdBase1, 20, StreamIdBase1, 10, 0)]
    [InlineData(StreamIdBase1, 20, StreamIdBase2, 10, 10)]
    public void GetDataSelectionDifference_Test(string dataSelectionStreamIdBase, int dataSelectionCount, string discoveryStreamIdBase, int discoveryCount, int expectedFinalCount)
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA }, new DiscoveryState { Id = DiscoveryIdB },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryResult = CreateDiscoveryResult(discoveryCount, discoveryStreamIdBase);
        var dataSelectionResult = CreateDiscoveryResult(dataSelectionCount, dataSelectionStreamIdBase);
        ICollection<string> errors = new List<string>();

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out dataSelectionResult, out errors)).Returns(true);

        var result = discoveryManager.GetDataSelectionDifference(DiscoveryIdA);
        var content = (IEnumerable<TestDataSelectionWithId>)result.Content;

        Assert.Equal((int)OK, result.StatusCode);
        Assert.Equal(expectedFinalCount, content.Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void GetDataSelectionDifference_InvalidInput_Test(string discoveryIdA)
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        Assert.ThrowsAny<Exception>(() => discoveryManager.GetDataSelectionDifference(discoveryIdA));
    }

    [Fact]
    public void GetDataSelectionDifference_DiscoveryOrDataSelectionResultNotFound_Test()
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA }, new DiscoveryState { Id = DiscoveryIdB },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryOrDataSelectionResult = CreateDiscoveryResult(10, StreamIdBase1);
        ICollection<string> errors = new List<string>();

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(false);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out discoveryOrDataSelectionResult, out errors)).Returns(true);

        var result = discoveryManager.GetDataSelectionDifference(DiscoveryIdA);

        Assert.Equal((int)NotFound, result.StatusCode);
        Assert.Contains(DiscoveryIdA, ((RestApiErrorResponse)result.Content).Error);

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out discoveryOrDataSelectionResult, out errors)).Returns(false);

        result = discoveryManager.GetDataSelectionDifference(DiscoveryIdA);

        Assert.Equal((int)OK, result.StatusCode);
        Assert.Equal(discoveryOrDataSelectionResult, (IEnumerable<TestDataSelectionWithId>)result.Content);
    }

    [Fact]
    public void GetDataSelectionDifference_DiscoveryOrDataSelectionResultInvalid_Test()
    {
        var discoveryStates = new[]
        {
            new DiscoveryState { Id = DiscoveryIdA }, new DiscoveryState { Id = DiscoveryIdB },
        };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates,
            out var mockConfigurationProvider, out _, out _, out _);

        var discoveryOrDataSelectionResult = CreateDiscoveryResult(10, StreamIdBase1);
        ICollection<string> errors = new List<string> { "Discovery file is terribly corrupted." };

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(false);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out discoveryOrDataSelectionResult, out errors)).Returns(true);

        var result = discoveryManager.GetDataSelectionDifference(DiscoveryIdA);

        Assert.Equal((int)InternalServerError, result.StatusCode);
        Assert.Contains(DiscoveryIdA, ((RestApiErrorResponse)result.Content).Error);

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryIdA, out discoveryOrDataSelectionResult, out errors)).Returns(true);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName,
                out discoveryOrDataSelectionResult, out errors)).Returns(false);

        result = discoveryManager.GetDataSelectionDifference(DiscoveryIdA);

        Assert.Equal((int)InternalServerError, result.StatusCode);
        Assert.Contains(DataSelectionString, ((RestApiErrorResponse)result.Content).Error);
    }

    [Fact]
    public void StartDiscovery_Throws_With_Null_DiscoveryState_Test()
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        Assert.Throws<ArgumentNullException>(() => discoveryManager.StartDiscovery(null, null));
    }

    [Fact]
    public void StartDiscovery_Starts_OK_Test()
    {
        var discoveryState = new DiscoveryState
        {
            Id = DiscoveryId,
        };

        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke())
            .Callback(() => Task.Delay(100).GetAwaiter().GetResult())
            .Returns(new TestDataSourceConfiguration());

        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(() => new TestDataSourceConfiguration());
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _, mockGetDataSourceFunction);
        var mvcResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.True(mvcResult.StatusCode.Equals((int)Accepted));
        Assert.True(discoveryState.Status.Equals(OperationStatus.Active));
        Assert.True(SpinWait.SpinUntil(() => !discoveryState.Status.Equals(OperationStatus.Active), DiscoveryStateChangeTimeout),
            $"Status was {discoveryState.Status}. Start time {discoveryState.StartTime}. End time {discoveryState.EndTime}. Errors {discoveryState.Errors}");
    }

    [Fact]
    public void StartDiscovery_Completes_Test()
    {
        var outErrors = new List<string>();
        var discoveryState = new DiscoveryState { Id = DiscoveryId };

        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out _, out _, mockGetDataSourceFunction);

        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, DiscoveryId, It.IsAny<TestDataSelectionWithId[]>(),
                out It.Ref<ICollection<string>>.IsAny)).Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
                TestDataSelectionWithId[] discoveryResult0, out ICollection<string> errors) =>
                {
                    errors = new List<string>();
                })).Returns(true);

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                    It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveDiscoveryStateConfigCallback((string id, string name,
                DiscoveryState[] configs, out ICollection<string> errors) =>
                    {
                        errors = outErrors;
                    })).Returns(true);

        var mvcResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.True(mvcResult.StatusCode.Equals((int)Accepted));
        Assert.True(SpinWait.SpinUntil(() => !discoveryState.Status.Equals(OperationStatus.Active), DiscoveryStateChangeTimeout));
        Assert.True(discoveryState.Status.Equals(OperationStatus.Complete));
    }

    [Fact]
    public void StartDiscovery_Fails_When_Same_Id_Already_Active_Test()
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);

        var discoveryStateA = new DiscoveryState
        {
            Id = DiscoveryId,
        };

        var discoveryStateA2 = new DiscoveryState
        {
            Id = DiscoveryId,
        };

        var mvcResult = discoveryManager.StartDiscovery(discoveryStateA, null);
        Assert.True(mvcResult.StatusCode.Equals((int)Accepted));

        var mvcResult2 = discoveryManager.StartDiscovery(discoveryStateA2, null);
        Assert.True(mvcResult2.StatusCode.Equals((int)Conflict));
        Assert.Contains(DiscoveryId, ((RestApiErrorResponse)mvcResult2.Content).Error);
    }

    [Fact]
    public void StartDiscovery_Fails_When_One_Already_Active_Test()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke())
            .Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _,
            out var mockDiscoveryFunction, out _, mockGetDataSourceFunction);

        mockDiscoveryFunction.Setup(x => x.Invoke(It.IsAny<string>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<DataSourceDiscoveryService<TestDataSelectionWithId>>(),
                It.IsAny<CancellationToken>())).Returns((string a, TestDataSourceConfiguration tdsConfig, DataSourceDiscoveryService<TestDataSelectionWithId> service, CancellationToken ct) => Task.Delay(500, ct));

        var discoveryStateA = new DiscoveryState
        {
            Id = DiscoveryIdA,
        };

        var discoveryStateB = new DiscoveryState
        {
            Id = DiscoveryIdB,
        };

        var mvcResult = discoveryManager.StartDiscovery(discoveryStateA, null);
        Assert.True(mvcResult.StatusCode.Equals((int)Accepted));

        var mvcResult2 = discoveryManager.StartDiscovery(discoveryStateB, null);
        Assert.True(mvcResult2.StatusCode.Equals((int)BadRequest));
        Assert.Contains(OnlyOneDiscoveryPermitted, ((RestApiErrorResponse)mvcResult2.Content).Error);
    }

    [Fact]
    public void StartDiscovery_Saves_State_Configuration()
    {
        var savedConfigCalled = false;
        ICollection<string> outErrors = new List<string>();
        var discoveryResult = CreateDiscoveryResult(100, StreamIdBase);

        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out _, out _, mockGetDataSourceFunction);

        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, DiscoveryId, It.IsAny<TestDataSelectionWithId[]>(),
                    out It.Ref<ICollection<string>>.IsAny)).Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
                TestDataSelectionWithId[] discoveryResult0, out ICollection<string> errors) =>
                    {
                        errors = outErrors;
                    })).Returns(true);

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveDiscoveryStateConfigCallback((string id, string name,
                DiscoveryState[] configs, out ICollection<string> errors) =>
                {
                    savedConfigCalled = true;
                    errors = outErrors;
                })).Returns(true);

        var discoveryState = new DiscoveryState
        {
            Id = DiscoveryId,
        };

        ICollection<string> tryGetErrors;
        mockConfigurationProvider.Setup(configurationProvider =>
            configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryId, out discoveryResult, out tryGetErrors)).Returns(true);

        discoveryManager.StartDiscovery(discoveryState, null);

        Assert.True(SpinWait.SpinUntil(() => !discoveryState.Status.Equals(OperationStatus.Active), DiscoveryStateChangeTimeout));
        Assert.True(SpinWait.SpinUntil(() => savedConfigCalled, DiscoveryStateChangeTimeout));
    }

    [Fact]
    public void StartDiscovery_Saves_Results_Configuration()
    {
        var savedResultsCalled = false;
        ICollection<string> outErrors = new List<string>();
        var discoveryResult = CreateDiscoveryResult(100, StreamIdBase);

        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out _, out _, mockGetDataSourceFunction);

        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, DiscoveryId, It.IsAny<TestDataSelectionWithId[]>(),
                    out It.Ref<ICollection<string>>.IsAny)).Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
                TestDataSelectionWithId[] discoveryResult0, out ICollection<string> errors0) =>
                    {
                        errors0 = outErrors;
                        savedResultsCalled = true;
                    })).Returns(true);

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveDiscoveryStateConfigCallback(
            (string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
            {
                errors = outErrors;
            })).Returns(true);

        var discoveryState = new DiscoveryState
        {
            Id = DiscoveryId,
        };

        ICollection<string> tryGetResultErrors;
        mockConfigurationProvider.Setup(configurationProvider =>
            configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryId, out discoveryResult, out tryGetResultErrors)).Returns(true);

        discoveryManager.StartDiscovery(discoveryState, null);

        Assert.True(SpinWait.SpinUntil(() => !discoveryState.Status.Equals(OperationStatus.Active), DiscoveryStateChangeTimeout));
        Assert.True(savedResultsCalled || SpinWait.SpinUntil(() => savedResultsCalled, DiscoveryStateChangeTimeout));
    }

    [Fact]
    public void StartDiscovery_AutoSelect_Test()
    {
        var savedResultsCalled = false;
        var dataSelectionSaved = false;

        ICollection<string> outErrors = new List<string>();
        var discoveryResult = CreateDiscoveryResult(100, StreamIdBase);

        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out var mockDataSourceDiscoveryFunction, out _, mockGetDataSourceFunction);

        mockDataSourceDiscoveryFunction.Setup(discoveryFunction => discoveryFunction
            .Invoke(It.IsAny<string>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<DataSourceDiscoveryService<TestDataSelectionWithId>>(), It.IsAny<CancellationToken>()))
            .Callback((string query, TestDataSourceConfiguration configuration, DataSourceDiscoveryService<TestDataSelectionWithId> discoveryService, CancellationToken token) =>
            {
                foreach (var item in discoveryResult)
                {
                    discoveryService.AddOrUpdateSelectionItem(item);
                }
            });

        ICollection<string> selectionErrors = new List<string>();
        var dataSelectionResult = Array.Empty<TestDataSelectionWithId>();
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TrySaveConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName, It.IsAny<TestDataSelectionWithId[]>(), out selectionErrors))
            .Callback(() => dataSelectionSaved = true)
            .Returns(true);

        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(ComponentId, CommonConstants.DataSelectionConfigurationName, out dataSelectionResult, out selectionErrors)).Returns(true);
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, DiscoveryId, It.IsAny<TestDataSelectionWithId[]>(),
                    out It.Ref<ICollection<string>>.IsAny)).Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
                TestDataSelectionWithId[] discoveryResult0, out ICollection<string> errors0) =>
                    {
                        errors0 = outErrors;
                        savedResultsCalled = true;
                    })).Returns(true);

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveDiscoveryStateConfigCallback(
            (string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
            {
                errors = outErrors;
            })).Returns(true);

        var discoveryState = new DiscoveryState
        {
            Id = DiscoveryId,
            AutoSelect = true,
        };

        ICollection<string> tryGetResultErrors;
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(ComponentId, DiscoveryId, out discoveryResult, out tryGetResultErrors)).Returns(true);

        discoveryManager.StartDiscovery(discoveryState, null);

        Assert.True(SpinWait.SpinUntil(() => !discoveryState.Status.Equals(OperationStatus.Active), DiscoveryStateChangeTimeout * 10));
        Assert.True(savedResultsCalled);
        Assert.NotNull(_dataSelectionUpdateArguments);
        Assert.True(_dataSelectionUpdateArguments.NewValue is TestDataSelectionWithId[]);
        Assert.Equal(discoveryResult.Length, ((TestDataSelectionWithId[])_dataSelectionUpdateArguments.NewValue).Length);
        Assert.True(dataSelectionSaved);
    }

    [Fact]
    public void GetDiscoveryState_Test()
    {
        var discoveryStates = new DiscoveryState[2];
        discoveryStates[0] = new DiscoveryState { Id = DiscoveryIdA, Status = OperationStatus.Active, };
        discoveryStates[1] = new DiscoveryState { Id = DiscoveryIdB, Status = OperationStatus.Complete };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates, out _, out _, out _, out _);

        var mvcResult = discoveryManager.GetDiscoveryState(DiscoveryIdA);
        Assert.Equal((int)OK, mvcResult.StatusCode);
        var serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        var deserializedContent = JsonSerializer.Deserialize<DiscoveryState>(serializedContent);
        Assert.Equal(OperationStatus.Active, deserializedContent.Status);

        mvcResult = discoveryManager.GetDiscoveryState(DiscoveryIdB);
        Assert.Equal((int)OK, mvcResult.StatusCode);
        serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        deserializedContent = JsonSerializer.Deserialize<DiscoveryState>(serializedContent);
        Assert.Equal(OperationStatus.Complete, deserializedContent.Status);
    }

    [Fact]
    public void GetDiscoveryStates_Test()
    {
        var discoveryStates = new DiscoveryState[2];
        discoveryStates[0] = new DiscoveryState { Id = DiscoveryIdA };
        discoveryStates[1] = new DiscoveryState { Id = DiscoveryIdB };

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, discoveryStates, out _, out _, out _, out _);

        var mvcResult = discoveryManager.GetDiscoveryStates();
        Assert.Equal((int)OK, mvcResult.StatusCode);
        var serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        var deserializedContent = JsonSerializer.Deserialize<DiscoveryState[]>(serializedContent);
        Assert.True(deserializedContent.Length == 2);
        Assert.NotNull(deserializedContent.First(d => d.Id == DiscoveryIdA));
        Assert.NotNull(deserializedContent.First(d => d.Id == DiscoveryIdB));
    }

    [Fact]
    public void GetDiscoveryState_InvalidState_Test()
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        var newDiscoveryId = $"{DiscoveryIdA}2";
        var mvcResult = discoveryManager.GetDiscoveryState(newDiscoveryId);
        Assert.Equal((int)NotFound, mvcResult.StatusCode);
        Assert.Contains(DiscoveryId, ((RestApiErrorResponse)mvcResult.Content).Error);
    }

    [Fact]
    public void GetDiscoveryStates_InvalidStates_Test()
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        var mvcResult = discoveryManager.GetDiscoveryStates();
        var serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        var deserializedContent = JsonSerializer.Deserialize<DiscoveryState[]>(serializedContent);
        Assert.False(deserializedContent.Length != 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteDiscoveryResult_InvalidDiscoveryId(string discoveryId)
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        Assert.ThrowsAny<ArgumentException>(() => discoveryManager.DeleteDiscoveryResult(discoveryId));
    }

    [Fact]
    public void DeleteDiscoveryResult_DiscoveryIdNotFound()
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);

        var discoveryState1 = new DiscoveryState { Id = "did1" };
        var discoveryState2 = new DiscoveryState { Id = "did2" };

        var startResult = discoveryManager.StartDiscovery(discoveryState1, null);
        var deleteResult = discoveryManager.DeleteDiscoveryResult(discoveryState2.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)NotFound, deleteResult.StatusCode);
    }

    [Fact]
    public void DeleteDiscoveryResult_NoOutstandingDiscoveryToCancel()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out _, out _, mockGetDataSourceFunction);

        var savedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, It.IsAny<string>(),
                It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
        TestDataSelectionWithId[] discoveryResult, out ICollection<string> errors) =>
                {
                    savedResultIds.Add(discoveryId);
                    errors = null;
                })).Returns(true);

        var savedStateArrays = new List<DiscoveryState[]>();
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new SaveDiscoveryStateConfigCallback((string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs);
            errors = null;
        })).Returns(true);

        var deletedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.DeleteDiscoveryResult(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string componentId, string discoveryId) =>
        {
            deletedResultIds.Add(discoveryId);
        });

        var discoveryState = new DiscoveryState { Id = "did1" };
        var startResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.True(SpinWait.SpinUntil(() => savedStateArrays.Count > 0, DiscoveryStateChangeTimeout));

        var deleteResult = discoveryManager.DeleteDiscoveryResult(discoveryState.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, deleteResult.StatusCode);
        Assert.Single(savedResultIds);
        Assert.Contains(discoveryState.Id, savedResultIds);
        Assert.Equal(2, savedStateArrays.Count);
        Assert.Single(savedStateArrays[0]);
        Assert.Equal(discoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(discoveryState.Id, savedStateArrays[1][0].Id);
        Assert.Equal(OperationStatus.Complete, savedStateArrays[0][0].Status);
        Assert.Equal(OperationStatus.Complete, savedStateArrays[1][0].Status);
        Assert.Single(deletedResultIds);
        Assert.Contains(discoveryState.Id, deletedResultIds);
    }

    [Fact]
    public void DeleteDiscoveryResult_CancelOutstandingDiscovery()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out var mockDiscoveryFunction, out _, mockGetDataSourceFunction);

        var savedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, It.IsAny<string>(),
                It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new TrySaveDiscoveryResultConfigCallback(
            (string cid, string discoveryId, TestDataSelectionWithId[] discoveryResult, out ICollection<string> errors) =>
            {
                savedResultIds.Add(discoveryId);
                errors = null;
            })).Returns(true);

        var savedStateArrays = new List<DiscoveryState[]>();
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new SaveDiscoveryStateConfigCallback((string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs);
            errors = null;
        })).Returns(true);

        var deletedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.DeleteDiscoveryResult(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string componentId, string discoveryId) =>
        {
            deletedResultIds.Add(discoveryId);
        });

        var discoveryCancelled = false;
        mockDiscoveryFunction.Setup(x => x.Invoke(It.IsAny<string>(), It.IsAny<TestDataSourceConfiguration>(),
                It.IsAny<DataSourceDiscoveryService<TestDataSelectionWithId>>(), It.IsAny<CancellationToken>()))
        .Returns(async (string a, TestDataSourceConfiguration tdsConfig, DataSourceDiscoveryService<TestDataSelectionWithId> service, CancellationToken ct) =>
        {
            try
            {
                await Task.Delay(-1, ct);
            }
            catch (TaskCanceledException)
            {
                discoveryCancelled = true;
            }

            return Task.FromCanceled(ct);
        });

        var discoveryState = new DiscoveryState { Id = "did1" };

        var startResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.False(discoveryCancelled);

        var deleteResult = discoveryManager.DeleteDiscoveryResult(discoveryState.Id);

        Assert.True(discoveryCancelled);
        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, deleteResult.StatusCode);
        Assert.Empty(savedResultIds);
        Assert.Equal(2, savedStateArrays.Count);
        Assert.Single(savedStateArrays[0]);
        Assert.Equal(discoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(discoveryState.Id, savedStateArrays[1][0].Id);
        Assert.Equal(OperationStatus.Canceled, savedStateArrays[0][0].Status);
        Assert.Equal(OperationStatus.Canceled, savedStateArrays[1][0].Status);
        Assert.Single(deletedResultIds);
        Assert.Contains(discoveryState.Id, deletedResultIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteDiscovery_InvalidDiscoveryId(string discoveryId)
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);
        Assert.ThrowsAny<ArgumentException>(() => discoveryManager.DeleteDiscovery(discoveryId));
    }

    [Fact]
    public void DeleteDiscovery_DiscoveryIdNotFound()
    {
        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null, out _, out _, out _, out _);

        var discoveryState1 = new DiscoveryState { Id = "did1" };
        var discoveryState2 = new DiscoveryState { Id = "did2" };

        var startResult = discoveryManager.StartDiscovery(discoveryState1, null);
        var deleteResult = discoveryManager.DeleteDiscovery(discoveryState2.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)NotFound, deleteResult.StatusCode);
    }

    [Fact]
    public void DeleteDiscovery_NoOutstandingDiscoveryToCancel()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out _, out _, mockGetDataSourceFunction);

        var savedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, It.IsAny<string>(),
                It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
        TestDataSelectionWithId[] discoveryResult, out ICollection<string> errors) =>
        {
            savedResultIds.Add(discoveryId);
            errors = null;
        })).Returns(true);

        var savedStateArrays = new List<DiscoveryState[]>();
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new SaveDiscoveryStateConfigCallback((string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs);
            errors = null;
        })).Returns(true);

        var deletedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.DeleteDiscoveryResult(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string componentId, string discoveryId) =>
        {
            deletedResultIds.Add(discoveryId);
        });

        var discoveryState = new DiscoveryState { Id = "did1" };

        var startResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.True(SpinWait.SpinUntil(() => savedStateArrays.Count > 0, DiscoveryStateChangeTimeout));

        var deleteResult = discoveryManager.DeleteDiscovery(discoveryState.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, deleteResult.StatusCode);
        Assert.Single(savedResultIds);
        Assert.Contains(discoveryState.Id, savedResultIds);
        Assert.Equal(2, savedStateArrays.Count);
        Assert.Equal(discoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(OperationStatus.Complete, savedStateArrays[0][0].Status);
        Assert.Empty(savedStateArrays[1]);
        Assert.Single(deletedResultIds);
        Assert.Contains(discoveryState.Id, deletedResultIds);
    }

    [Fact]
    public void DeleteDiscovery_CancelOutstandingDiscovery()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress,
            null, out var mockConfigurationProvider, out _, out var mockDiscoveryFunction, out _, mockGetDataSourceFunction);

        var savedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, It.IsAny<string>(),
                It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
        TestDataSelectionWithId[] discoveryResult, out ICollection<string> errors) =>
        {
            savedResultIds.Add(discoveryId);
            errors = null;
        })).Returns(true);

        var savedStateArrays = new List<DiscoveryState[]>();
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new SaveDiscoveryStateConfigCallback((string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs);
            errors = null;
        })).Returns(true);

        var deletedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.DeleteDiscoveryResult(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string componentId, string discoveryId) =>
        {
            deletedResultIds.Add(discoveryId);
        });

        var discoveryCancelled = false;
        mockDiscoveryFunction.Setup(x => x.Invoke(It.IsAny<string>(), It.IsAny<TestDataSourceConfiguration>(),
                It.IsAny<DataSourceDiscoveryService<TestDataSelectionWithId>>(), It.IsAny<CancellationToken>()))
        .Returns(async (string a, TestDataSourceConfiguration tdsConfig, DataSourceDiscoveryService<TestDataSelectionWithId> service, CancellationToken ct) =>
        {
            try
            {
                await Task.Delay(-1, ct);
            }
            catch (TaskCanceledException)
            {
                discoveryCancelled = true;
            }

            return Task.FromCanceled(ct);
        });

        var discoveryState = new DiscoveryState { Id = "did1" };

        var startResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.False(discoveryCancelled);

        var deleteResult = discoveryManager.DeleteDiscovery(discoveryState.Id);

        Assert.True(discoveryCancelled);
        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, deleteResult.StatusCode);
        Assert.Empty(savedResultIds);
        Assert.Equal(2, savedStateArrays.Count);
        Assert.Single(savedStateArrays[0]);
        Assert.Equal(discoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(OperationStatus.Canceled, savedStateArrays[0][0].Status);
        Assert.Empty(savedStateArrays[1]);
        Assert.Single(deletedResultIds);
        Assert.Contains(discoveryState.Id, deletedResultIds);
    }

    [Fact]
    public void CancelDiscovery_NoOutstandingDiscoveryToCancel()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out _, out _, mockGetDataSourceFunction);

        var savedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, It.IsAny<string>(),
                It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
        TestDataSelectionWithId[] discoveryResult, out ICollection<string> errors) =>
        {
            savedResultIds.Add(discoveryId);
            errors = null;
        })).Returns(true);

        var savedStateArrays = new List<DiscoveryState[]>();
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new SaveDiscoveryStateConfigCallback((string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs);
            errors = null;
        })).Returns(true);

        var discoveryState = new DiscoveryState { Id = "did1" };

        var startResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.True(SpinWait.SpinUntil(() => savedStateArrays.Count > 0, DiscoveryStateChangeTimeout));

        var cancelResult = discoveryManager.CancelDiscovery(discoveryState.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)Conflict, cancelResult.StatusCode);
        Assert.Single(savedResultIds);
        Assert.Contains(discoveryState.Id, savedResultIds);
        Assert.Single(savedStateArrays);
        Assert.Equal(discoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(OperationStatus.Complete, savedStateArrays[0][0].Status);
    }

    [Fact]
    public void CancelDiscovery_CancelOutstandingDiscovery()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress,
            null, out var mockConfigurationProvider, out _, out var mockDiscoveryFunction, out _, mockGetDataSourceFunction);

        var savedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, It.IsAny<string>(),
                It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
        TestDataSelectionWithId[] discoveryResult, out ICollection<string> errors) =>
        {
            savedResultIds.Add(discoveryId);
            errors = null;
        })).Returns(true);

        var savedStateArrays = new List<DiscoveryState[]>();
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new SaveDiscoveryStateConfigCallback((string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs);
            errors = null;
        })).Returns(true);

        var discoveryCancelled = false;
        mockDiscoveryFunction.Setup(x => x.Invoke(It.IsAny<string>(), It.IsAny<TestDataSourceConfiguration>(),
                It.IsAny<DataSourceDiscoveryService<TestDataSelectionWithId>>(), It.IsAny<CancellationToken>()))
        .Returns(async (string a, TestDataSourceConfiguration tdsConfig, DataSourceDiscoveryService<TestDataSelectionWithId> service, CancellationToken ct) =>
        {
            try
            {
                await Task.Delay(-1, ct);
            }
            catch (TaskCanceledException)
            {
                discoveryCancelled = true;
            }

            return Task.FromCanceled(ct);
        });

        var discoveryState = new DiscoveryState { Id = "did1" };

        var startResult = discoveryManager.StartDiscovery(discoveryState, null);
        Assert.False(discoveryCancelled);

        var cancelResult = discoveryManager.CancelDiscovery(discoveryState.Id);

        Assert.True(discoveryCancelled);
        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, cancelResult.StatusCode);
        Assert.Empty(savedResultIds);
        Assert.Single(savedStateArrays);
        Assert.Single(savedStateArrays[0]);
        Assert.Equal(discoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(OperationStatus.Canceled, savedStateArrays[0][0].Status);
    }

    [Fact]
    public void DeleteDiscoveries_MultipleDiscoveriesPresent()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var discoveryManager = CreateDataSourceDiscoveryManager<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, BaseAddress, null,
            out var mockConfigurationProvider, out _, out var mockDiscoveryFunction, out _, mockGetDataSourceFunction);

        var savedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.TrySaveDiscoveryResult(ComponentId, It.IsAny<string>(),
                It.IsAny<TestDataSelectionWithId[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new TrySaveDiscoveryResultConfigCallback((string cid, string discoveryId,
        TestDataSelectionWithId[] discoveryResult, out ICollection<string> errors) =>
        {
            savedResultIds.Add(discoveryId);
            errors = null;
        })).Returns(true);

        var savedStateArrays = new List<DiscoveryState[]>();
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                It.IsAny<DiscoveryState[]>(), out It.Ref<ICollection<string>>.IsAny))
        .Callback(new SaveDiscoveryStateConfigCallback((string id, string name, DiscoveryState[] configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs);
            errors = null;
        })).Returns(true);

        var deletedResultIds = new List<string>();
        mockConfigurationProvider.Setup(x => x.DeleteDiscoveryResult(It.IsAny<string>(), It.IsAny<string>()))
        .Callback((string componentId, string discoveryId) =>
        {
            deletedResultIds.Add(discoveryId);
        });

        var discoveryState1 = new DiscoveryState { Id = "did1" };

        var startResult1 = discoveryManager.StartDiscovery(discoveryState1, null);
        Assert.True(SpinWait.SpinUntil(() => savedStateArrays.Count > 0, DiscoveryStateChangeTimeout));

        var discoveryCancelled = false;
        mockDiscoveryFunction.Setup(x => x.Invoke(It.IsAny<string>(), It.IsAny<TestDataSourceConfiguration>(),
                It.IsAny<DataSourceDiscoveryService<TestDataSelectionWithId>>(), It.IsAny<CancellationToken>()))
        .Returns(async (string a, TestDataSourceConfiguration tdsConfig, DataSourceDiscoveryService<TestDataSelectionWithId> service, CancellationToken ct) =>
        {
            try
            {
                await Task.Delay(-1, ct);
            }
            catch (TaskCanceledException)
            {
                discoveryCancelled = true;
            }

            return Task.FromCanceled(ct);
        });

        var discoveryState2 = new DiscoveryState { Id = "did2" };

        var startResult2 = discoveryManager.StartDiscovery(discoveryState2, null);
        Assert.False(discoveryCancelled);

        var deleteResult = discoveryManager.DeleteDiscoveries();

        Assert.Equal((int)Accepted, startResult1.StatusCode);
        Assert.Equal((int)Accepted, startResult2.StatusCode);
        Assert.True(discoveryCancelled);
        Assert.Equal((int)OK, deleteResult.StatusCode);

        Assert.Single(savedResultIds);
        Assert.Contains(discoveryState1.Id, savedResultIds);

        Assert.Equal(3, savedStateArrays.Count);

        Assert.Single(savedStateArrays[0]);
        Assert.Equal(discoveryState1.Id, savedStateArrays[0][0].Id);
        Assert.Equal(OperationStatus.Complete, savedStateArrays[0][0].Status);

        Assert.Equal(2, savedStateArrays[1].Length);
        IDictionary<string, DiscoveryState> stateDictionary = new Dictionary<string, DiscoveryState>
        {
            [savedStateArrays[1][0].Id] = savedStateArrays[1][0],
            [savedStateArrays[1][1].Id] = savedStateArrays[1][1],
        };

        Assert.Equal(2, stateDictionary.Count);
        Assert.Contains(discoveryState1.Id, stateDictionary);
        Assert.Equal(OperationStatus.Complete, stateDictionary[discoveryState1.Id].Status);
        Assert.Contains(discoveryState2.Id, stateDictionary);
        Assert.Equal(OperationStatus.Canceled, stateDictionary[discoveryState2.Id].Status);

        Assert.Empty(savedStateArrays[2]);

        Assert.Equal(2, deletedResultIds.Count);
        Assert.Contains(discoveryState1.Id, deletedResultIds);
        Assert.Contains(discoveryState2.Id, deletedResultIds);
    }

    private static TestDataSelectionWithId[] CreateDiscoveryResult(int itemCount, string streamIdSeed, bool selected = false)
    {
        var discoveryResult = new TestDataSelectionWithId[itemCount];

        for (var i = 0; i < itemCount; i++)
        {
            discoveryResult[i] = new TestDataSelectionWithId(streamIdSeed + i, null, selected);
        }

        return discoveryResult;
    }

    private DataSourceDiscoveryManager<TDataSource, TDataSelection> CreateDataSourceDiscoveryManager<TDataSource, TDataSelection>(string componentId, string baseUrl, DiscoveryState[] initialDiscoveryStates,
        out Mock<IConfigurationProvider> mockConfigurationProvider, out Mock<ILogger> mockLogger, out Mock<Func<string, TDataSource, DataSourceDiscoveryService<TDataSelection>, CancellationToken, Task>> mockDiscoveryFunction,
        out Mock<Action<ConfigurationChangedEventArgs>> mockDataSelectionUpdateAction, Mock<Func<TDataSource>> mockGetDataSourceFunction = null)
        where TDataSource : class, IDataSourceConfiguration
        where TDataSelection : class, IDataSelectionConfiguration
    {
        ICollection<string> errors = new List<string>();
        TDataSelection result;
        _dataSelectionUpdateArguments = null;
        mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockLogger = new Mock<ILogger>();
        mockDataSelectionUpdateAction = new Mock<Action<ConfigurationChangedEventArgs>>();
        mockDataSelectionUpdateAction.Setup(_ => _.Invoke(It.IsAny<ConfigurationChangedEventArgs>()))
            .Callback((ConfigurationChangedEventArgs arguments) => _dataSelectionUpdateArguments = arguments);
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetDiscoveryResult(It.IsAny<string>(), It.IsAny<string>(), out result, out errors)).Returns(true);

        mockDiscoveryFunction = new Mock<Func<string, TDataSource, DataSourceDiscoveryService<TDataSelection>, CancellationToken, Task>>();

        mockGetDataSourceFunction ??= new Mock<Func<TDataSource>>();

        if (initialDiscoveryStates != null)
        {
            mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DiscoveriesConfigurationName,
                out initialDiscoveryStates, out errors)).Returns(true);
        }

        return new DataSourceDiscoveryManager<TDataSource, TDataSelection>(componentId, baseUrl,
            mockConfigurationProvider.Object, mockLogger.Object, mockDiscoveryFunction.Object,
            mockDataSelectionUpdateAction.Object, mockGetDataSourceFunction.Object);
    }
}
