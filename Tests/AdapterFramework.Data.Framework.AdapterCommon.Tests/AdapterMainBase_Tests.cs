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
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Registry;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests;

public class AdapterMainBase_Tests
{
    private const string LoggingFacetName = "Logging";
    private const string DataSourceFacetName = "DataSource";
    private const string DataSelectionFacetName = "DataSelection";
    private const string DataFiltersFacetName = "DataFilters";
    private const string SchedulesFacetName = "Schedules";
    private const string IntervalsToRecoverName = "IntervalsToRecover";
    private const string TestComponentId = "TestID";
    private const string TestComponentType = "TestAdapter";
    private const string CustomFacetName = "CustomFacet";
    private const string CustomHelpString = "Custom Help String";
    private const string BaseAddress = "http:\\localhost:5590";
    private const string JsonSuffix = ".json";
    private const string TxtSuffix = ".txt";
    private const int DefaultPort = 5590;

    private const long DefaultLogFileSizeLimitBytes = 1073741824 / 31;
    private const int DefaultLogFileCountLimit = 31;
    private const LogLevel DefaultLogLevel = LogLevel.Information;

    private static bool _processDataSourceUpdateCalled;
    private static bool _processDataSelectionUpdateCalled;
    private static bool _processFailoverModeChangeCalled;
    private static bool _processFailoverRoleChangeCalled;
    private static bool _customFacetUpdateCalled;
    private static bool _adapterRegistered;
    private static bool _adapterInitialized;
    private static int _helpFunctionCalledCounter;
    private static List<DataStream> _diagnosticsStreams;
    private static List<DataStream> _healthStreams;
    private static List<HttpRequestExecutionInfo> _httpRequestExecutionInfos;

    private readonly IEdgeEventProvider _edgeEventProvider;
    private RuntimeConfigurationRegistry _runtimeConfigurationRegistry;
    private RuntimeAdministrationRegistry _runtimeAdministrationRegistry;
    private TestLogger _testLogger;
    private Mock<ILogManager> _mockLogManager;
    private Mock<IConfigurationProvider> _mockConfigurationProvider;
    private Mock<IEdgeDataProtector> _mockEdgeDataProtector;
    private Mock<IComponentIdService> _mockComponentIdProvider;
    private Mock<IHealthMessageProcessor> _mockHealthProc;
    private Mock<IDiagnosticsMessageProcessor> _mockDiagnosticsMessageProcessor;

    public AdapterMainBase_Tests()
    {
        _edgeEventProvider = new TestEdgeEventProvider();
        _processDataSelectionUpdateCalled = false;
        _processDataSourceUpdateCalled = false;
        _processFailoverModeChangeCalled = false;
        _processFailoverRoleChangeCalled = false;
        _customFacetUpdateCalled = false;
        _adapterInitialized = false;
        _adapterRegistered = false;
        _diagnosticsStreams = new List<DataStream>();
        _healthStreams = new List<DataStream>();
        _httpRequestExecutionInfos = new List<HttpRequestExecutionInfo>();
    }

    private delegate void SchedulesCallback(string id, string facet, ScheduleConfiguration[] configs, out ICollection<string> errors);

    #region Tests
    
    [Fact]
    public async Task InitializeAdapterAsync_SchedulingEnabled_DiscoveryEnabled_HistoryRecoveryEnabled_Test()
    {
        const int ExpectedFacetCount = 6;
        using var adapter = CreateAdapter();
        adapter.SetEnableScheduling(true);
        adapter.SetEnableDiscovery(true);
        adapter.SetEnableHistoryRecovery(true);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        _runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out var registeredFacets);

        Assert.Equal(ExpectedFacetCount, registeredFacets.Count);
        Assert.True(registeredFacets.Contains(LoggingFacetName));
        Assert.True(registeredFacets.Contains(DataSourceFacetName));
        Assert.True(registeredFacets.Contains(DataSelectionFacetName));
        Assert.True(registeredFacets.Contains(SchedulesFacetName));
        Assert.True(registeredFacets.Contains(DataFiltersFacetName));
        Assert.True(registeredFacets.Contains(IntervalsToRecoverName));
        Assert.True(_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId, out var discoveryManager));
        Assert.NotNull(discoveryManager);
        Assert.True(adapter.IsCommonServiceInitialized());
        Assert.True(_adapterRegistered);
        Assert.True(_adapterInitialized);
    }

    [Fact]
    public async Task InitializeAdapterAsync_SchedulingDisabled_DiscoveryDisabled_HistoryRecoveryDisabled_Test()
    {
        const int ExpectedFacetCount = 4;

        using var adapter = CreateAdapter();
        adapter.SetEnableScheduling(false);
        adapter.SetEnableDiscovery(false);
        adapter.SetEnableHistoryRecovery(false);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);

        await adapter.InitializeAsync(CancellationToken.None);
        _runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out var registeredFacets);

        Assert.Equal(ExpectedFacetCount, registeredFacets.Count);
        Assert.True(registeredFacets.Contains(LoggingFacetName));
        Assert.True(registeredFacets.Contains(DataSourceFacetName));
        Assert.True(registeredFacets.Contains(DataSelectionFacetName));
        Assert.True(registeredFacets.Contains(DataFiltersFacetName));
        Assert.False(_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId, out _));
        Assert.True(adapter.IsCommonServiceInitialized());
    }

    [Fact]
    public async Task InitializeAdapterAsync_CustomizedHistoryRecoveryStates_Test()
    {
        const int ExpectedFacetCount = 5;
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);
        adapter.SetAutomaticHrIntervalStates(new HashSet<DeviceStatus> { DeviceStatus.Shutdown, DeviceStatus.Removed });

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        _runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out var registeredFacets);

        Assert.Equal(ExpectedFacetCount, registeredFacets.Count);
        Assert.True(registeredFacets.Contains(LoggingFacetName));
        Assert.True(registeredFacets.Contains(DataSourceFacetName));
        Assert.True(registeredFacets.Contains(DataSelectionFacetName));
        Assert.True(registeredFacets.Contains(DataFiltersFacetName));
        Assert.True(registeredFacets.Contains(IntervalsToRecoverName));
        Assert.True(adapter.IsCommonServiceInitialized());
        Assert.True(_adapterRegistered);
        Assert.True(_adapterInitialized);
    }

    [Fact]
    public void RegisterCustomFacetAdapterAsync_Add_Test()
    {
        using var adapter = CreateAdapter();

        adapter.Register(CancellationToken.None);
        adapter.RegisterCustomFacet<TestCustomFacet>(CustomFacetName, CustomFacetUpdate, CustomFacetHelp);

        _runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out var facets);

        Assert.True(facets.Contains(CustomFacetName), "Custom facet was not found.");
    }

    [Theory]
    [InlineData(OmfVersion.Omf12)]
    [InlineData(OmfVersion.Omf12)]
    public void RegisterCustomFacetUpdateAsync_UpdateConfigCallback_Test(OmfVersion omfVersion)
    {
        using var adapter = CreateAdapter(omfVersion: omfVersion);

        adapter.Register(CancellationToken.None);
        adapter.RegisterCustomFacet<TestCustomFacet>(CustomFacetName, CustomFacetUpdate, CustomFacetHelp);

        _runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CustomFacetName), out var commandGeneratorTuple);
        var testCustomFacet = new TestCustomFacet();
        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(testCustomFacet, testCustomFacet));

        Assert.True(_customFacetUpdateCalled, "Custom facet callback did not execute.");
    }

    [Fact]
    public async Task RegisterCustomFacetUpdateAsync_CheckHelpOutput_Test()
    {
        const int ExpectedFunctionCallCount = 1;

        using var adapter = CreateAdapter();
        adapter.Register(CancellationToken.None);
        await adapter.InitializeAsync(CancellationToken.None);

        adapter.RegisterCustomFacet<TestCustomFacet>(CustomFacetName, null, CustomFacetHelp);

        var mockManagementRegistry = new Mock<IRuntimeManagementRegistry>();
        using var helpController = new SystemConfigurationHelpController(_runtimeConfigurationRegistry, mockManagementRegistry.Object);
        var result = helpController.GetComponentFacetHelp(TestComponentId, CustomFacetName);

        Assert.True(result is OkObjectResult);
        Assert.Equal(ExpectedFunctionCallCount, _helpFunctionCalledCounter);
    }

    [Fact]
    public async Task StartAdapterAsync_MissingDataSource_Test()
    {
        using var adapter = CreateAdapter();

        ICollection<string> errors = new List<string>();
        TestDataSource dataSourceConfig = null;
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors))
            .Returns(dataSourceConfig != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.False(adapter.Started);
    }

    [Theory]
    [InlineData(DataCollectionMode.CurrentOnly)]
    [InlineData(DataCollectionMode.CurrentWithBackfill)]
    [InlineData(DataCollectionMode.HistoryOnly)]
    public async Task StartAdapterAsync_HistoryRecoveryEnabled_DataCollectionMode_Test(DataCollectionMode dataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource() { DataCollectionMode = dataCollectionMode };
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors))
            .Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors))
            .Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        if (dataCollectionMode == DataCollectionMode.HistoryOnly)
        {
            Assert.False(adapter.Started);
        }
        else
        {
            Assert.True(adapter.Started);
        }
    }

    [Fact]
    public async Task InitializeAdapterAsync_SchedulesErrorDoesNotSave_Test()
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableScheduling(true);
        var savedConfigCalled = false;
        ICollection<string> outErrors = new List<string> { "Hello there" };
        var outScheduleConfig = new[] { new ScheduleConfiguration() };

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), CommonConstants.SchedulesConfigurationName, out outScheduleConfig, out outErrors)).Returns(false);
        _mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.SchedulesConfigurationName, It.IsAny<ScheduleConfiguration[]>(), out It.Ref<ICollection<string>>.IsAny))
            .Callback(new SchedulesCallback((string id, string name, ScheduleConfiguration[] configs, out ICollection<string> errors) =>
            {
                savedConfigCalled = true;
                errors = outErrors;
            }));

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.False(savedConfigCalled);
    }

    [Fact]
    public async Task InitializeAdapterAsync_SchedulesExist_Test()
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableScheduling(true);
        var savedConfigCalled = false;
        ICollection<string> outErrors = new List<string>();
        var outScheduleConfig = new[] { new ScheduleConfiguration() };

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), CommonConstants.SchedulesConfigurationName, out outScheduleConfig, out outErrors)).Returns(true);
        _mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.SchedulesConfigurationName, It.IsAny<ScheduleConfiguration[]>(), out It.Ref<ICollection<string>>.IsAny))
            .Callback(new SchedulesCallback((string id, string name, ScheduleConfiguration[] configs, out ICollection<string> errors) =>
            {
                savedConfigCalled = true;
                errors = outErrors;
            }));

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.False(savedConfigCalled);
    }

    [Fact]
    public async Task InitializeAdapterAsync_SchedulesDisabledNoConfig_Test()
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableScheduling(false);
        var savedConfigCalled = false;
        ICollection<string> outErrors = new List<string>();
        ScheduleConfiguration[] outScheduleConfig = null;

        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), CommonConstants.SchedulesConfigurationName, out outScheduleConfig, out outErrors)).Returns(false);
        _mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.SchedulesConfigurationName, It.IsAny<ScheduleConfiguration[]>(), out It.Ref<ICollection<string>>.IsAny))
            .Callback(new SchedulesCallback((string id, string name, ScheduleConfiguration[] configs, out ICollection<string> errors) =>
            {
                savedConfigCalled = true;
                errors = outErrors;
            }));

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.False(savedConfigCalled);
    }

    [Fact]
    public async Task StopAdapterAsync_Test()
    {
        using var adapter = CreateAdapter();

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        await adapter.StopAsync(CancellationToken.None);

        Assert.False(_processDataSelectionUpdateCalled);
        Assert.False(adapter.Started);
    }

    [Fact]
    public async Task ProcessDataSourceUpdateAsync_Update_Test()
    {
        using var adapter = CreateAdapter();
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(dataSourceConfig, dataSourceConfig));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceEditedLogMessage);

        Assert.True(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
    }

    [Theory]
    [InlineData(DataCollectionMode.CurrentOnly)]
    [InlineData(DataCollectionMode.CurrentWithBackfill)]
    [InlineData(DataCollectionMode.HistoryOnly)]
    public async Task ProcessDataSourceUpdateAsync_HistoryRecoveryEnabled_Stopped_UpdateToHistoryOnly_Test(DataCollectionMode initialDataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource() { DataCollectionMode = initialDataCollectionMode };
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(dataSourceConfig, new TestDataSource() { DataCollectionMode = DataCollectionMode.HistoryOnly }));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceEditedLogMessage);

        Assert.False(adapter.Started);
        Assert.True(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
    }

    [Theory]
    [InlineData(DataCollectionMode.CurrentOnly)]
    [InlineData(DataCollectionMode.CurrentWithBackfill)]
    [InlineData(DataCollectionMode.HistoryOnly)]
    public async Task ProcessDataSourceUpdateAsync_HistoryRecoveryEnabled_Started_UpdateToHistoryOnly_Test(DataCollectionMode initialDataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource() { DataCollectionMode = initialDataCollectionMode };
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(dataSourceConfig, new TestDataSource() { DataCollectionMode = DataCollectionMode.HistoryOnly }));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceEditedLogMessage);

        Assert.False(adapter.Started);
        Assert.True(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
        Assert.False(adapter.Started);
    }

    [Theory]
    [InlineData(DataCollectionMode.CurrentOnly)]
    [InlineData(DataCollectionMode.CurrentWithBackfill)]
    [InlineData(DataCollectionMode.HistoryOnly)]
    public async Task ProcessDataSourceUpdateAsync_HistoryRecoveryEnabled_Stopped_UpdateToNonHistoryOnly_Test(DataCollectionMode initialDataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource() { DataCollectionMode = initialDataCollectionMode };
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(dataSourceConfig, new TestDataSource() { DataCollectionMode = DataCollectionMode.CurrentWithBackfill }));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceEditedLogMessage);

        if (initialDataCollectionMode == DataCollectionMode.HistoryOnly)
        {
            Assert.True(adapter.Started);
        }
        else
        {
            Assert.False(adapter.Started);
        }

        Assert.False(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
    }

    [Theory]
    [InlineData(DataCollectionMode.CurrentOnly)]
    [InlineData(DataCollectionMode.CurrentWithBackfill)]
    public async Task ProcessDataSourceUpdateAsync_HistoryRecoveryEnabled_Started_UpdateToNonHistoryOnly_Test(DataCollectionMode initialDataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource() { DataCollectionMode = initialDataCollectionMode };
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(dataSourceConfig, new TestDataSource() { DataCollectionMode = DataCollectionMode.CurrentOnly }));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceEditedLogMessage);

        Assert.True(adapter.Started);
        Assert.True(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
        Assert.True(adapter.Started);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessDataSourceUpdateAsync_Deletion_Test(bool enableHistoryRecovery)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(enableHistoryRecovery);
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.True(adapter.Started);
        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(dataSourceConfig, null));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceRemovedLogMessage);

        Assert.False(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
        Assert.False(adapter.Started);
    }

    [Fact]
    public async Task ProcessDataSourceUpdateAsync_Created_Test()
    {
        using var adapter = CreateAdapter();
        adapter.Register(CancellationToken.None);

        Assert.True(_adapterRegistered);
        Assert.False(_adapterInitialized);

        ICollection<string> errors = new List<string>();
        TestDataSource historyDataSourceConfig = null;
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out historyDataSourceConfig, out errors)).Returns(historyDataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.False(adapter.Started);
        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(historyDataSourceConfig, new TestDataSource()));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceAddedLogMessage);

        Assert.False(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
        Assert.True(adapter.Started);
    }

    [Theory]
    [InlineData(DataCollectionMode.CurrentOnly)]
    [InlineData(DataCollectionMode.CurrentWithBackfill)]
    [InlineData(DataCollectionMode.HistoryOnly)]
    public async Task ProcessDataSourceUpdateAsync_HistoryRecoveryEnabled_Created_DataCollectionMode_Test(DataCollectionMode newDataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);
        adapter.Register(CancellationToken.None);

        Assert.True(_adapterRegistered);
        Assert.False(_adapterInitialized);

        ICollection<string> errors = new List<string>();
        TestDataSource dataSourceConfig = null;
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.False(adapter.Started);
        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(dataSourceConfig, new TestDataSource() { DataCollectionMode = newDataCollectionMode }));
        _testLogger.ContainsMessage(AdapterMainBase<TestDataSource, TestDataSelectionWithId>.DataSourceAddedLogMessage);

        Assert.False(_processDataSourceUpdateCalled);
        Assert.False(_processDataSelectionUpdateCalled);
        if (newDataCollectionMode == DataCollectionMode.HistoryOnly)
        {
            Assert.False(adapter.Started);
        }
        else
        {
            Assert.True(adapter.Started);
        }
    }

    [Fact]
    public async Task ProcessSelectionUpdateAsync_Test()
    {
        using var adapter = CreateAdapter();
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSelectionConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(null, null));

        Assert.True(_processDataSelectionUpdateCalled);
        Assert.False(_processDataSourceUpdateCalled);
    }

    [Fact]
    public async Task ProcessSelectionUpdateAsync_CalledOnStart_Test()
    {
        using var adapter = CreateAdapter();
        adapter.Register(CancellationToken.None);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        var dataSelection = Array.Empty<TestDataSelectionWithId>();

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.True(_processDataSelectionUpdateCalled);
        Assert.False(_processDataSourceUpdateCalled);
    }

    [Fact]
    public async Task AdministrationController_Start_Stop_CallbackFunction()
    {
        using var adapter = CreateAdapter();

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        TestDataSelectionWithId[] dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        Assert.True(_runtimeAdministrationRegistry.TryGetCallbackFunction((TestComponentId, "Stop"), out var stopCallback));

        await stopCallback.Invoke();

        Assert.False(adapter.Started);
        Assert.True(_runtimeAdministrationRegistry.TryGetCallbackFunction((TestComponentId, "Start"), out var startCallback));

        await startCallback.Invoke();

        Assert.True(adapter.Started);
    }

    [Theory]
    [InlineData(false, DataCollectionMode.CurrentOnly)]
    [InlineData(true, DataCollectionMode.CurrentOnly)]
    [InlineData(true, DataCollectionMode.CurrentWithBackfill)]
    [InlineData(true, DataCollectionMode.HistoryOnly)]
    public async Task AdministrationController_Stopped_Started_DataSourceChanges_Test(bool enableHistoryRecovery, DataCollectionMode dataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(enableHistoryRecovery);

        ICollection<string> errors = new List<string>();
        TestDataSource dataSourceConfig = null;
        TestDataSelectionWithId dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        Assert.False(_adapterRegistered);
        Assert.False(_adapterInitialized);

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.True(_adapterRegistered);
        Assert.True(_adapterInitialized);

        await adapter.StartAsync(CancellationToken.None);

        Assert.True(_runtimeAdministrationRegistry.TryGetCallbackFunction((TestComponentId, "Stop"), out var stopCallback));

        await stopCallback.Invoke();

        Assert.False(adapter.Started);
        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSourceConfigurationName), out var commandGeneratorTuple));

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(null, null));

        Assert.False(adapter.Started);
        Assert.False(_processDataSourceUpdateCalled);

        Assert.True(_runtimeAdministrationRegistry.TryGetCallbackFunction((TestComponentId, "Start"), out var startCallback));

        await startCallback.Invoke();

        // make sure it doesn't start without the configuration
        Assert.False(adapter.Started);

        commandGeneratorTuple.CallbackAction.Invoke(new ConfigurationChangedEventArgs(null, new TestDataSource() { DataCollectionMode = dataCollectionMode }));

        // now it should start
        if (dataCollectionMode == DataCollectionMode.HistoryOnly)
        {
            Assert.False(adapter.Started);
        }
        else
        {
            Assert.True(adapter.Started);
        }

        Assert.False(_processDataSourceUpdateCalled);
    }

    [Fact]
    public async Task AdministrationController_Start_NoDataSource_CallbackFunction()
    {
        using var adapter = CreateAdapter();

        ICollection<string> errors = new List<string>();
        TestDataSource dataSourceConfig = null;
        TestDataSelectionWithId dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.True(_adapterRegistered);
        Assert.True(_adapterInitialized);

        await adapter.StartAsync(CancellationToken.None);

        Assert.False(adapter.Started);
        Assert.True(_runtimeAdministrationRegistry.TryGetCallbackFunction((TestComponentId, "Start"), out var startCallback));
        Assert.False(adapter.Started);

        await startCallback.Invoke();

        // make sure it doesn't start when data source is not available
        Assert.False(adapter.Started);
    }

    [Theory]
    [InlineData(DataCollectionMode.CurrentOnly)]
    [InlineData(DataCollectionMode.CurrentWithBackfill)]
    [InlineData(DataCollectionMode.HistoryOnly)]
    public async Task AdministrationController_Start_HistoryRecoveryEnabled_DataCollectionMode_Test(DataCollectionMode dataCollectionMode)
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableHistoryRecovery(true);

        ICollection<string> errors = new List<string>();
        TestDataSource dataSourceConfig = null;
        TestDataSelectionWithId dataSelection = null;

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSelectionConfigurationName, out dataSelection, out errors)).Returns(dataSelection != null);
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName, It.IsAny<object>(), out errors)).Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.True(_adapterRegistered);
        Assert.True(_adapterInitialized);

        await adapter.StartAsync(CancellationToken.None);

        Assert.False(adapter.Started);
        Assert.True(_runtimeAdministrationRegistry.TryGetCallbackFunction((TestComponentId, "Start"), out var startCallback));
        Assert.False(adapter.Started);

        dataSourceConfig = new TestDataSource() { DataCollectionMode = dataCollectionMode };

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);

        await startCallback.Invoke();

        if (dataCollectionMode == DataCollectionMode.HistoryOnly)
        {
            Assert.False(adapter.Started);
        }
        else
        {
            Assert.True(adapter.Started);
        }
    }

    [Fact]
    public void AdapterMainBase_Constructor_Test()
    {
        using var adapter = CreateAdapter();

        Assert.False(_runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out var facets));
        Assert.Null(facets);
        Assert.False(adapter.IsCommonServiceInitialized());
    }

    [Fact]
    public void AdapterMainBase_Register_Test()
    {
        const int ExpectedFacetCount = 6;
        using var adapter = CreateAdapter();
        adapter.SetEnableDiscovery(true);
        adapter.SetEnableScheduling(true);
        adapter.SetEnableHistoryRecovery(true);

        adapter.Register(CancellationToken.None);
        _runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out var registeredFacets);

        Assert.Equal(ExpectedFacetCount, registeredFacets.Count);
        Assert.True(registeredFacets.Contains(LoggingFacetName));
        Assert.True(registeredFacets.Contains(DataSourceFacetName));
        Assert.True(registeredFacets.Contains(DataSelectionFacetName));
        Assert.True(registeredFacets.Contains(SchedulesFacetName));
        Assert.True(registeredFacets.Contains(IntervalsToRecoverName));
        Assert.True(registeredFacets.Contains(DataFiltersFacetName));

        Assert.True(adapter.IsCommonServiceInitialized());
        Assert.True(_adapterRegistered);
        Assert.False(_adapterInitialized);
    }

    [Fact]
    public void AdapterMainBase_Unregister_Test()
    {
        const int ExpectedFacetCount = 5;
        const int ExpectedAdministrationFunctionsCount = 2;

        using var adapter = CreateAdapter();
        adapter.SetEnableScheduling(true);
        adapter.SetEnableDiscovery(true);
        adapter.Register(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out var registeredFacets));
        Assert.True(_runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(TestComponentId, out var administrationFunctionNames));
        Assert.Equal(ExpectedFacetCount, registeredFacets.Count);
        Assert.Equal(ExpectedAdministrationFunctionsCount, administrationFunctionNames.Count);

        adapter.Unregister(CancellationToken.None);

        Assert.False(_runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(TestComponentId, out administrationFunctionNames));
        Assert.False(_runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId, out registeredFacets));
        Assert.Null(registeredFacets);
        Assert.Null(administrationFunctionNames);
        Assert.False(_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId, out _));
        Assert.False(_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(TestComponentId, out _));
    }

    [Theory]
    [InlineData(MetadataInfo.None, StreamProperties.Maximum)]
    [InlineData(MetadataInfo.Low, StreamProperties.Uom | StreamProperties.Minimum)]
    [InlineData(MetadataInfo.High, StreamProperties.None)]
    public async Task AdapterMainBase_GeneralConfigurationChangeUpdatesCommonService_Test(MetadataInfo metadataInfo, StreamProperties streamProperties)
    {
        using var cts = new CancellationTokenSource();
        using var adapter = CreateAdapter();
        await adapter.InitializeAsync(cts.Token);

        await adapter.ProcessGeneralConfigurationUpdateCallbackAsync(new GeneralConfiguration(), new GeneralConfiguration { MetadataLevel = metadataInfo, IncludeSourceProperties = streamProperties });
        Assert.Equal(metadataInfo, adapter.GetCommonService().StreamMetadataLevel);
        Assert.Equal(streamProperties, adapter.GetCommonService().IncludeSourceProperties);
    }

    [Fact]
    public async Task AdapterMainBase_GeneralConfigurationChangeHasNullConfigUpdatesCommonService_Test()
    {
        using var cts = new CancellationTokenSource();
        using var adapter = CreateAdapter();
        await adapter.InitializeAsync(cts.Token);

        await adapter.ProcessGeneralConfigurationUpdateCallbackAsync(new GeneralConfiguration(), new GeneralConfiguration { MetadataLevel = MetadataInfo.High, IncludeSourceProperties = StreamProperties.Uom });
        Assert.Equal(MetadataInfo.High, adapter.GetCommonService().StreamMetadataLevel);
        Assert.Equal(StreamProperties.Uom, adapter.GetCommonService().IncludeSourceProperties);

        await adapter.ProcessGeneralConfigurationUpdateCallbackAsync(new GeneralConfiguration(), null);
        Assert.NotEqual(MetadataInfo.High, adapter.GetCommonService().StreamMetadataLevel);
        Assert.NotEqual(StreamProperties.Uom, adapter.GetCommonService().IncludeSourceProperties);

        await adapter.ProcessGeneralConfigurationUpdateCallbackAsync(null, new GeneralConfiguration { MetadataLevel = MetadataInfo.Low, IncludeSourceProperties = StreamProperties.Minimum });
        Assert.Equal(MetadataInfo.Low, adapter.GetCommonService().StreamMetadataLevel);
        Assert.Equal(StreamProperties.Minimum, adapter.GetCommonService().IncludeSourceProperties);
    }

    [Fact]
    public async Task AdapterMainBase_ResendHealthMetadata_Test()
    {
        // DeviceStatus and NextHealthMessageExpected
        const int ExpectedHealthStreamsCount = 2;

        // ErrorRate, IORate, StreamCount, AssetCount and EventCount
        const int ExpectedDiagnosticsStreamsCount = 5;

        using var cts = new CancellationTokenSource();
        using var adapter = CreateAdapter();

        await adapter.InitializeAsync(cts.Token);

        _mockHealthProc.Setup(healthProcessor => healthProcessor.WriteHealthStreams(It.IsAny<DataStream[]>(), It.IsAny<MessageAction>())).Callback((DataStream[] streams, MessageAction messageAction) => _healthStreams.AddRange(streams));
        _mockDiagnosticsMessageProcessor.Setup(diagnosticsProcessor => diagnosticsProcessor.WriteDiagnosticsStreams(It.IsAny<DataStream[]>())).Callback((DataStream[] streams) => _diagnosticsStreams.AddRange(streams));

        adapter.ResendHealthMetadata();

        Assert.NotEmpty(_healthStreams);
        Assert.NotEmpty(_diagnosticsStreams);
        Assert.Equal(ExpectedHealthStreamsCount, _healthStreams.Count);
        Assert.Equal(ExpectedDiagnosticsStreamsCount, _diagnosticsStreams.Count);
    }

    [Fact]
    public async Task AdapterMainBase_ResendDynamicMetadata_DoesNotSentHealth_Test()
    {
        using var cts = new CancellationTokenSource();
        using var adapter = CreateAdapter();
        await adapter.InitializeAsync(cts.Token);

        _mockHealthProc.Setup(healthProcessor => healthProcessor.WriteHealthStreams(It.IsAny<DataStream[]>(), It.IsAny<MessageAction>())).Callback((DataStream[] streams, MessageAction messageAction) => _healthStreams.AddRange(streams));
        _mockDiagnosticsMessageProcessor.Setup(diagnosticsProcessor => diagnosticsProcessor.WriteDiagnosticsStreams(It.IsAny<DataStream[]>())).Callback((DataStream[] streams) => _diagnosticsStreams.AddRange(streams));

        adapter.ResendDynamicMetadata();

        Assert.Empty(_healthStreams);
        Assert.Empty(_diagnosticsStreams);
    }

    [Fact]
    public async Task AdapterMainBase_ValidateDataSelectionConfigurationInternal_Test()
    {
        const string FirstStreamId = "Hello";
        const string SecondStreamId = "World";
        var dataSelections = new[] { new TestDataSelectionWithId(null), new TestDataSelectionWithId(null), };

        using var adapter = CreateAdapter();
        adapter.Register(CancellationToken.None);
        await adapter.InitializeAsync(CancellationToken.None);

        Assert.True(_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.DataSelectionConfigurationName), out var commandGeneratorTuple));

        foreach (var item in dataSelections)
        {
            Assert.Null(item.StreamId);
        }

        Assert.NotNull(commandGeneratorTuple.CustomValidationFunction);

        var errors = commandGeneratorTuple.CustomValidationFunction.Invoke(new ConfigurationChangedEventArgs(null, dataSelections));

        Assert.Empty(errors);

        var defaultSelectionItem = new TestDataSelectionWithId();
        foreach (var item in dataSelections)
        {
            Assert.Equal(defaultSelectionItem.Name, item.StreamId);
        }

        dataSelections = new[] { new TestDataSelectionWithId(FirstStreamId), new TestDataSelectionWithId(SecondStreamId), };
        var expectedDataSelections = new[] { new TestDataSelectionWithId(FirstStreamId), new TestDataSelectionWithId(SecondStreamId), };

        errors = commandGeneratorTuple.CustomValidationFunction.Invoke(new ConfigurationChangedEventArgs(null, dataSelections));
        Assert.Empty(errors);

        for (var i = 0; i < dataSelections.Length; i++)
        {
            Assert.Equal(expectedDataSelections[i].StreamId, dataSelections[i].StreamId);
        }
    }

    [Fact]
    public async Task AdapterMainBase_Unregister_ConfigurationAndLogs_Removed_Test()
    {
        using var adapter = CreateAdapter();
        adapter.Register(CancellationToken.None);
        await adapter.InitializeAsync(CancellationToken.None);

        var basePath = _mockConfigurationProvider.Object.GetCommonApplicationDataDirectoryPath();

        var configPath = Path.Combine(basePath, EdgeSystemConstants.ConfigurationDirectoryName);
        var configFileName = $"{TestComponentId}{EdgeSystemConstants.SeparatorUnderscore}{CommonConstants.DataSourceConfigurationName}{JsonSuffix}";

        Directory.CreateDirectory(configPath);
        File.Create(Path.Combine(configPath, configFileName)).Close();

        var logPath = Path.Combine(basePath, EdgeSystemConstants.LoggingDirectoryName);
        var logFileName = $"{TestComponentId}{EdgeSystemConstants.SeparatorHyphen}12345678{TxtSuffix}";

        Directory.CreateDirectory(logPath);
        File.Create(Path.Combine(logPath, logFileName)).Close();

        adapter.Unregister(CancellationToken.None);

        var configRemovalPath = Path.Combine(configPath, EdgeSystemConstants.RemovedDirectoryName);
        var logRemovalPath = Path.Combine(logPath, EdgeSystemConstants.RemovedDirectoryName);

        Assert.True(Directory.Exists(configRemovalPath), $"Removed folder for {EdgeSystemConstants.ConfigurationDirectoryName} files does not exist.");
        Assert.True(Directory.Exists(logRemovalPath), $"Removed folder for {EdgeSystemConstants.LoggingDirectoryName} files does not exist.");
        Assert.True(Directory.GetFiles(configRemovalPath).Length > 0, $"No files found in {EdgeSystemConstants.ConfigurationDirectoryName} removal path.");
        Assert.True(Directory.GetFiles(logRemovalPath).Length > 0, $"No files found in {EdgeSystemConstants.LoggingDirectoryName} removal path.");
    }

    [Theory]
    [InlineData(DefaultLogLevel, 20, DefaultLogFileSizeLimitBytes)]
    [InlineData(DefaultLogLevel, DefaultLogFileCountLimit, 34636822)]
    [InlineData(LogLevel.Critical, DefaultLogFileCountLimit, DefaultLogFileSizeLimitBytes)]
    public async Task AdapterMainBase_UpdateLoggingConfiguration_Test(LogLevel logLevel, int logFileCountLimit, long logFileSizeLimit)
    {
        const string ConfigChangeMessage = "Logging configuration change will take effect only on Adapter restart.";
        var testLogger = new TestLogger();

        using var adapter = CreateAdapter();

        _mockLogManager.Setup(logManager => logManager.GetOrCreateInstrumentedLogger(TestComponentId, It.IsAny<LoggerConfiguration>())).Returns(testLogger);
        await adapter.InitializeAsync(CancellationToken.None);

        var oldLoggerConfiguration = new LoggerConfiguration();
        var newLoggerConfiguration = new LoggerConfiguration
        {
            LogLevel = logLevel,
            LogFileCountLimit = logFileCountLimit,
            LogFileSizeLimitBytes = logFileSizeLimit,
        };

        _runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId, CommonConstants.LoggingConfigurationName), out var callAction);

        var configurationChangedEvent = new ConfigurationChangedEventArgs(oldLoggerConfiguration, newLoggerConfiguration);
        callAction.CallbackAction(configurationChangedEvent);

        var logMessages = testLogger.GetLogMessages();
        if (logFileCountLimit != DefaultLogFileCountLimit || logFileSizeLimit != DefaultLogFileSizeLimitBytes)
        {
            Assert.Equal(LogLevel.Warning, logMessages.Last().LogLevel);
            Assert.Equal(ConfigChangeMessage, logMessages.Last().LogMessage);
        }
        else
        {
            Assert.Equal(LogLevel.Debug, logMessages.Last().LogLevel);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdapterMainBase_SubscribeToEgressEvents_Test(bool subscribeToEvents)
    {
        using var adapter = CreateAdapter();
        adapter.SetSubscribeToEdgeEvents(subscribeToEvents);

        var dataSourceConfiguration = new TestDataSource();
        ICollection<string> errors = new List<string>();

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), DataSourceFacetName, out dataSourceConfiguration, out errors))
            .Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);
        if (subscribeToEvents)
        {
            Assert.NotNull(adapter.EdgeEventChannel);
        }
        else
        {
            Assert.Null(adapter.EdgeEventChannel);
        }

        await adapter.StartAsync(CancellationToken.None);

        if (subscribeToEvents)
        {
            var executionInfo = new HttpRequestExecutionInfo("Test", "http://", MessageType.Data, HttpStatusCode.BadRequest, null);

            // Write to the adapter's own channel, not the event provider's channel
            await adapter.EdgeEventChannel.Writer.WriteAsync(executionInfo);

            // Wait up to 2 seconds for the event to be processed
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_httpRequestExecutionInfos.Count == 0 && sw.ElapsedMilliseconds < 2000)
            {
                await Task.Delay(50);
            }

            Assert.Single(_httpRequestExecutionInfos);
            Assert.Equal(executionInfo, _httpRequestExecutionInfos[0]);
        }
        else
        {
            Assert.Empty(_httpRequestExecutionInfos);
        }

        await adapter.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AdapterMainBase_UpdateSchedules_Test()
    {
        using var adapter = CreateAdapter();
        adapter.SetEnableScheduling(true);

        var schedulesSaved = false;
        var dataSourceConfiguration = new TestDataSource();
        ICollection<string> errors = new List<string>();

        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), DataSourceFacetName, out dataSourceConfiguration, out errors))
            .Returns(true);

        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(),
            CommonConstants.SchedulesConfigurationName, It.IsAny<object>(), out errors))
            .Callback(() => schedulesSaved = true)
            .Returns(true);

        await adapter.InitializeAsync(CancellationToken.None);

        await adapter.StartAsync(CancellationToken.None);

        var schedules = new[] { new ScheduleConfiguration() { Id = "TestConfig", Period = TimeSpan.FromSeconds(2), } };

        adapter.UpdateSchedules(schedules);

        Assert.True(schedulesSaved);
    }

    [Fact]
    public async Task AdapterMainBase_Initialize_FailoverCallbacksRegistration_Test()
    {
        var mockFailoverManager = new Mock<IFailoverManager>();
        string modeCallbackRegisteredComponentId = null;
        mockFailoverManager.Setup(m => m.RegisterFailoverModeChangeCallback(It.IsAny<string>(), It.IsAny<Func<FailoverMode, FailoverMode, Task>>()))
            .Callback((string componentId, Func<FailoverMode, FailoverMode, Task> _) =>
            {
                modeCallbackRegisteredComponentId = componentId;
            });

        string modeCallbackUnregisteredComponentId = null;
        mockFailoverManager.Setup(m => m.UnregisterFailoverModeChangeCallback(It.IsAny<string>()))
        .Callback((string componentId) =>
        {
            modeCallbackUnregisteredComponentId = componentId;
        });

        string roleCallbackRegisteredComponentId = null;
        mockFailoverManager.Setup(m => m.RegisterFailoverRoleChangeCallback(It.IsAny<string>(), It.IsAny<Func<FailoverRole, FailoverRole, Task>>()))
            .Callback((string componentId, Func<FailoverRole, FailoverRole, Task> _) =>
            {
                roleCallbackRegisteredComponentId = componentId;
            });

        string roleCallbackUnregisteredComponentId = null;
        mockFailoverManager.Setup(m => m.UnregisterFailoverRoleChangeCallback(It.IsAny<string>()))
        .Callback((string componentId) =>
        {
            roleCallbackUnregisteredComponentId = componentId;
        });

        using var adapter = CreateAdapter(failoverManager: mockFailoverManager.Object);
        adapter.SetSupportedFailoverModes(FailoverMode.Hot);

        await adapter.InitializeAsync(CancellationToken.None);

        Assert.Equal(TestComponentId, modeCallbackRegisteredComponentId);
        Assert.Equal(TestComponentId, roleCallbackRegisteredComponentId);
        Assert.Null(modeCallbackUnregisteredComponentId);
        Assert.Null(roleCallbackUnregisteredComponentId);

        adapter.Unregister(CancellationToken.None);

        Assert.Equal(TestComponentId, modeCallbackUnregisteredComponentId);
        Assert.Equal(TestComponentId, roleCallbackUnregisteredComponentId);
    }

    [Theory]
    [InlineData(FailoverMode.NotConfigured, FailoverMode.Hot, true)]
    [InlineData(FailoverMode.NotConfigured, FailoverMode.Cold, false)]
    [InlineData(FailoverMode.NotConfigured, FailoverMode.Warm, true)]
    [InlineData(FailoverMode.Hot, FailoverMode.Cold, false)]
    [InlineData(FailoverMode.Hot, FailoverMode.Warm, true)]
    [InlineData(FailoverMode.Hot, FailoverMode.NotConfigured, true)]
    [InlineData(FailoverMode.Cold, FailoverMode.Hot, true)]
    [InlineData(FailoverMode.Cold, FailoverMode.Warm, true)]
    [InlineData(FailoverMode.Cold, FailoverMode.NotConfigured, true)]
    [InlineData(FailoverMode.Warm, FailoverMode.NotConfigured, true)]
    [InlineData(FailoverMode.Warm, FailoverMode.Cold, false)]
    [InlineData(FailoverMode.Warm, FailoverMode.Hot, true)]

    public async Task AdapterMainBase_FailoverModeChange_Tests(FailoverMode oldMode, FailoverMode newMode, bool adapterShouldStart)
    {
        var mockFailoverManager = new Mock<IFailoverManager>();

        using var adapter = CreateAdapter(failoverManager: mockFailoverManager.Object);
        adapter.SetEnableScheduling(true);
        adapter.SetEnableDiscovery(false);
        adapter.SetEnableHistoryRecovery(false);
        adapter.SetSupportedFailoverModes(FailoverMode.NotConfigured | FailoverMode.Hot | FailoverMode.Warm | FailoverMode.Cold);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        await adapter.UpdateFailoverModeAsync(oldMode, newMode);
        Assert.Equal(adapterShouldStart, adapter.Started);
        Assert.True(_processFailoverModeChangeCalled);
    }

    [Theory]
    [InlineData(FailoverMode.Cold, false)]
    [InlineData(FailoverMode.Warm, true)]
    [InlineData(FailoverMode.Hot, true)]
    public async Task AdapterMainBase_FailoverRoleChange_Tests(FailoverMode failoverMode, bool adapterShouldStart)
    {
        var mockFailoverManager = new Mock<IFailoverManager>();
        using var adapter = CreateAdapter(failoverManager: mockFailoverManager.Object);
        adapter.SetEnableScheduling(true);
        adapter.SetEnableDiscovery(false);
        adapter.SetEnableHistoryRecovery(false);
        adapter.SetSupportedFailoverModes(FailoverMode.NotConfigured | FailoverMode.Hot | FailoverMode.Warm | FailoverMode.Cold);

        ICollection<string> errors = new List<string>();
        var dataSourceConfig = new TestDataSource();
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataSourceConfigurationName, out dataSourceConfig, out errors)).Returns(dataSourceConfig != null);

        await adapter.InitializeAsync(CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);

        await adapter.UpdateFailoverModeAsync(FailoverMode.NotConfigured, failoverMode);
        Assert.Equal(adapter.Started, adapterShouldStart);
        Assert.True(_processFailoverModeChangeCalled);

        await adapter.UpdateFailoverRoleAsync(FailoverRole.Secondary, FailoverRole.Primary);
        Assert.True(adapter.Started);
        Assert.True(_processFailoverRoleChangeCalled);

        await adapter.UpdateFailoverRoleAsync(FailoverRole.Primary, FailoverRole.Secondary);
        Assert.Equal(adapter.Started, adapterShouldStart);
        Assert.True(_processFailoverRoleChangeCalled);
    }

    #endregion

    #region Private Methods
    private static void CustomFacetUpdate(ConfigurationChangedEventArgs args) => _customFacetUpdateCalled = true;

    private static string CustomFacetHelp()
    {
        _helpFunctionCalledCounter++;

        return CustomHelpString;
    }

    private TestAdapter CreateAdapter(string componentId = TestComponentId, IFailoverManager failoverManager = null, OmfVersion omfVersion = OmfVersion.Omf12)
    {
        const string TestId = "Test ID";
        _runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        _runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        _testLogger = new TestLogger();
        _mockLogManager = new Mock<ILogManager>();
        _mockLogManager.Setup(lm => lm.GetOrCreateInstrumentedLogger(componentId, null)).Returns(_testLogger);
        _mockConfigurationProvider = new Mock<IConfigurationProvider>();

        var path = $"C:\\ProgramData\\AdapterFramework\\Adapters\\{componentId}\\{componentId}";
        _mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(null)).Returns(path);

        var outScheduleConfig = new[] { new ScheduleConfiguration { Id = TestId, Period = TimeSpan.Zero, Offset = null } };
        var outFilterConfig = new[] { new DataFiltersConfiguration { Id = TestId, AbsoluteDeadband = 0 } };
        ICollection<string> outErrors = new List<string>();
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), CommonConstants.SchedulesConfigurationName, out outScheduleConfig, out outErrors)).
            Returns(true);
        _mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), CommonConstants.DataFiltersConfigurationName, out outFilterConfig, out outErrors)).
            Returns(true);

        _mockEdgeDataProtector = new Mock<IEdgeDataProtector>();
        _mockComponentIdProvider = new Mock<IComponentIdService>();
        _mockComponentIdProvider.Setup(idProvider => idProvider.GetEdgeComponentId(TestComponentType)).Returns(componentId);
        _mockComponentIdProvider.Object.AddEdgeComponentId(TestComponentType, componentId);

        _mockHealthProc = new Mock<IHealthMessageProcessor>();
        _mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();

        var adapter = new TestAdapter(_mockLogManager.Object,
            _mockConfigurationProvider.Object,
            new Mock<IMessageProcessor>().Object,
            new ApplicationManifest(DefaultPort, BaseAddress, "prefix", "machine", "service", omfVersion),
            _runtimeConfigurationRegistry,
            _mockEdgeDataProtector.Object,
            _mockComponentIdProvider.Object,
            _mockHealthProc.Object,
            _runtimeAdministrationRegistry,
            _mockDiagnosticsMessageProcessor.Object,
            failoverManager,
            _edgeEventProvider);

        return adapter;
    }

    #endregion

    #region Test Classes

    private class TestDataSource : DataSourceConfigurationBase, IHistoryDataSourceConfiguration
    {
        public TestDataSource()
        {
            StreamIdPrefix = "Test";
            DefaultStreamIdPattern = "Test.{Test}";
        }

        public DataCollectionMode DataCollectionMode { get; set; } = DataCollectionMode.CurrentOnly;

        string IDataSourceConfiguration.DefaultStreamIdPattern => "Test.{Test}";

        protected override IEnumerable<ValidationResult> ValidateConfiguration()
        {
            yield break;
        }
    }

    private sealed class TestCustomFacet
    {
    }

    private sealed class TestAdapter : AdapterMainBase<TestDataSource, TestDataSelectionWithId>
    {
        public TestAdapter(ILogManager logManager,
            IConfigurationProvider configurationProvider,
            IMessageProcessor messageProcessor,
            IApplicationManifest applicationManifest,
            IRuntimeConfigurationRegistry runtimeConfigurationRegistry,
            IEdgeDataProtector edgeDataProtector,
            IComponentIdService componentIdService,
            IHealthMessageProcessor healthMessageProcessor,
            IRuntimeAdministrationRegistry runtimeAdministrationRegistry,
            IDiagnosticsMessageProcessor omfDiagnosticsMessageProcessor,
            IFailoverManager failoverManager,
            IEdgeEventProvider edgeEventProvider)
            : base(logManager, configurationProvider,
            messageProcessor, applicationManifest, runtimeConfigurationRegistry, edgeDataProtector, componentIdService,
            healthMessageProcessor, omfDiagnosticsMessageProcessor, runtimeAdministrationRegistry, failoverManager, edgeEventProvider)
        {
        }

        public bool Started { get; private set; }

        public override string ComponentType => TestComponentType;

        public void SetEnableDiscovery(bool enableDiscovery)
        {
            EnableDiscovery = enableDiscovery;
        }

        public void SetEnableScheduling(bool enableScheduling)
        {
            EnableScheduling = enableScheduling;
        }

        public void SetEnableHistoryRecovery(bool enableHistoryRecovery)
        {
            EnableHistoryRecovery = enableHistoryRecovery;
        }

        public void SetAutomaticHrIntervalStates(HashSet<DeviceStatus> deviceStatusSet)
        {
            AutomaticHistoryRecoveryIntervalStates = deviceStatusSet;
        }

        public IAdapterCommonService GetCommonService()
        {
            return CommonService;
        }

        public void UpdateSchedules(ScheduleConfiguration[] schedules)
        {
            UpdateSchedulesConfiguration(schedules);
        }

        public void SetSupportedFailoverModes(FailoverMode supportedFailoverModes)
        {
            SupportedFailoverModes = supportedFailoverModes;
        }

        public bool IsCommonServiceInitialized()
        {
            return CommonService?.ConfigurationProvider != null && CommonService.DataProtector != null &&
                   CommonService.HealthService != null && CommonService.Logger != null &&
                   CommonService.MessageProcessor != null;
        }

        public void SetSubscribeToEdgeEvents(bool subscribeToEvents)
        {
            if (subscribeToEvents)
            {
                SubscribedEdgeEventTypes = EdgeEventType.Egress;
            }
            else
            {
                SubscribedEdgeEventTypes = EdgeEventType.None;
            }
        }

        protected override Task ProcessDataSourceUpdateAsync(TestDataSource oldValue, TestDataSource newValue)
        {
            _processDataSourceUpdateCalled = true;
            return Task.CompletedTask;
        }

        protected override Task RegisterAdapterAsync(CancellationToken cancellationToken)
        {
            CommonService.DefaultStreamIdGenerator.SetDefaultStreamIdPattern("Test.{Test}", "Test");
            _adapterRegistered = true;

            return Task.CompletedTask;
        }

        protected override Task InitializeAdapterAsync(CancellationToken cancellationToken)
        {
            _adapterInitialized = true;
            return Task.CompletedTask;
        }

        protected override Task StartAdapterAsync(TestDataSource dataSourceConfiguration, CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        protected override Task StopAdapterAsync(TestDataSource dataSourceConfiguration, CancellationToken cancellationToken)
        {
            Started = false;
            return Task.CompletedTask;
        }

        protected override Task ProcessSelectionUpdateAsync(TestDataSelectionWithId[] oldValue, TestDataSelectionWithId[] newValue)
        {
            _processDataSelectionUpdateCalled = true;
            return Task.Delay(1);
        }

        protected override string GetDataSourceHelpInfo()
        {
            throw new NotImplementedException();
        }

        protected override string GetDataSelectionHelpInfo()
        {
            throw new NotImplementedException();
        }

        protected override string GetDefaultStreamId(TestDataSelectionWithId selectionItem)
        {
            return selectionItem.Name;
        }

        protected override Task DiscoverDataSourceAsync(string discoveryQuery, TestDataSource dataSourceConfiguration, IDataSourceDiscoveryService<TestDataSelectionWithId> discoveryService, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override ICollection<string> ValidateDataSelectionConfiguration(ConfigurationChangedEventArgs configurations)
        {
            return new List<string>();
        }

        protected override Task ProcessGeneralConfigurationUpdateAsync(IAdapterGeneralConfiguration oldValue, IAdapterGeneralConfiguration newValue)
        {
            return Task.CompletedTask;
        }

        protected override void OnEventRaised(IEdgeEvent httpRequestExecutionInfo)
        {
            _httpRequestExecutionInfos.Add((HttpRequestExecutionInfo)httpRequestExecutionInfo);
        }

        protected override Task ProcessFailoverModeChangeAsync(FailoverMode oldMode, FailoverMode newMode)
        {
            _processFailoverModeChangeCalled = true;
            return Task.CompletedTask;
        }

        protected override Task ProcessFailoverRoleChangeAsync(FailoverRole oldRole, FailoverRole newRole)
        {
            _processFailoverRoleChangeCalled = true;
            return Task.CompletedTask;
        }

        #endregion
    }
}
