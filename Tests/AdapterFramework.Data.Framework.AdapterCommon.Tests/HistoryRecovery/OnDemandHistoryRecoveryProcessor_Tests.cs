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
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.EgressComponent.Health;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;
using static System.Net.HttpStatusCode;
using static AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery.Constants.HistoryRecoveryConstants;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.HistoryRecovery;

public class OnDemandHistoryRecoveryProcessor_Tests
{
    private const string ComponentId = "ComponentId";
    private const string ComponentType = "ComponentType";
    private const string HistoryRecoveryId = "HistoryRecoveryId";
    private const string HistoryRecoveryIdA = "SampleRecoveryIdA";
    private const string HistoryRecoveryIdB = "SampleRecoveryIdB";
    private const int HistoryRecoveryStateChangeWaitTime = 20000;
    private const string TestStreamIdPrefix = "StreamIdPrefix";
    private const string TestDefaultStreamIdPattern = "DefaultStreamIdPattern";

    private delegate void SaveHistoryRecoveryStateConfigCallback(string id, string facet, ICollection<HistoryRecoveryState> configs, out ICollection<string> errors);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    public void Constructor_Throws_Test(string badComponentId, string badComponentType)
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockEdgeDataProtector = new Mock<IEdgeDataProtector>();
        var mockMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        using var egressHealthService = new EgressHealthService(mockHealthMessageProcessor.Object, mockLogger.Object, new ApplicationManifest(), ComponentId);
        var mockHistoryRecoveryFunction = new Mock<Func<HistoryRecoveryDetails, TestDataSourceConfiguration, IReadOnlyList<TestDataSelectionWithId>, IAdapterHistoryRecoveryService, CancellationToken, Task>>();

        Assert.ThrowsAny<ArgumentException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(badComponentId, ComponentType, mockLogger.Object, mockConfigurationProvider.Object, mockEdgeDataProtector.Object, mockMessageProcessor.Object, egressHealthService, mockHistoryRecoveryFunction.Object));
        Assert.ThrowsAny<ArgumentException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, badComponentType, mockLogger.Object, mockConfigurationProvider.Object, mockEdgeDataProtector.Object, mockMessageProcessor.Object, egressHealthService, mockHistoryRecoveryFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, ComponentType, null, mockConfigurationProvider.Object, mockEdgeDataProtector.Object, mockMessageProcessor.Object, egressHealthService, mockHistoryRecoveryFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, ComponentType, mockLogger.Object, null, mockEdgeDataProtector.Object, mockMessageProcessor.Object, egressHealthService, mockHistoryRecoveryFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, ComponentType, mockLogger.Object, mockConfigurationProvider.Object, null, mockMessageProcessor.Object, egressHealthService, mockHistoryRecoveryFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, ComponentType, mockLogger.Object, mockConfigurationProvider.Object, mockEdgeDataProtector.Object, null, egressHealthService, mockHistoryRecoveryFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, ComponentType, mockLogger.Object, mockConfigurationProvider.Object, mockEdgeDataProtector.Object, mockMessageProcessor.Object, null, mockHistoryRecoveryFunction.Object));
        Assert.Throws<ArgumentNullException>(() => new OnDemandHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, ComponentType, mockLogger.Object, mockConfigurationProvider.Object, mockEdgeDataProtector.Object, mockMessageProcessor.Object, egressHealthService, null));
    }

    [Fact]
    public void StartHistoryRecovery_Throws_With_Null_HistoryRecoveryState_Test()
    {
        using var onDemandHistoryRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out _, out _, out _);
        Assert.Throws<ArgumentNullException>(() => onDemandHistoryRecoveryProcessor.StartOnDemandHistoryRecovery(null));
    }

    [Fact]
    public void StartHistoryRecovery_Starts_OK_Test()
    {
        var historyRecoveryState = new HistoryRecoveryState { Id = HistoryRecoveryId, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider, out var mockHistoryRecoveryFunction, out var mockMessageProcessor);
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<HistoryRecoveryState[]>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
        {
            errors = null;
        })).Returns(true);
        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
            It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
        {
            try
            {
                Task.Delay(200, ct).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
            }

            return Task.FromCanceled(ct);
        });

        var streamIdPrefixSet = false;
        mockMessageProcessor.Setup(x => x.SetStreamIdPrefix(It.IsAny<string>())).Callback((string prefix) =>
        {
            streamIdPrefixSet = true;
        });

        var mvcResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);
        Assert.Equal((int)Accepted, mvcResult.StatusCode);
        Assert.Equal(OperationStatus.Active, historyRecoveryState.Status);
        Assert.True(SpinWait.SpinUntil(() => !historyRecoveryState.Status.Equals(OperationStatus.Active), HistoryRecoveryStateChangeWaitTime));
        Assert.True(streamIdPrefixSet);
    }

    [Fact]
    public void StartHistoryRecovery_Completes_Test()
    {
        var outErrors = new List<string>();
        var historyRecoveryState = new HistoryRecoveryState { Id = HistoryRecoveryId, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _,
            out var mockConfigurationProvider, out _, out _);

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
                    It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
                    {
                        errors = outErrors;
                    })).Returns(true);

        var mvcResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);
        Assert.Equal((int)Accepted, mvcResult.StatusCode);
        Assert.True(SpinWait.SpinUntil(() => !historyRecoveryState.Status.Equals(OperationStatus.Active), HistoryRecoveryStateChangeWaitTime));
        Assert.Equal(OperationStatus.Complete, historyRecoveryState.Status);
    }

    [Fact]
    public void StartHistoryRecovery_Fails_When_Same_Id_Already_Active_Test()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider, out _, out _);
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
        {
            errors = null;
        })).Returns(true);

        var historyRecoveryStateA = new HistoryRecoveryState { Id = HistoryRecoveryId, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var historyRecoveryStateA2 = new HistoryRecoveryState { Id = HistoryRecoveryId, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var mvcResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryStateA);
        Assert.Equal((int)Accepted, mvcResult.StatusCode);

        var mvcResult2 = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryStateA2);
        Assert.Equal((int)Conflict, mvcResult2.StatusCode);
        Assert.Contains(HistoryRecoveryId, ((RestApiErrorResponse)mvcResult2.Content).Error);
    }

    [Fact]
    public void StartHistoryRecovery_Fails_When_One_Already_Active_Test()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke())
            .Returns(new TestDataSourceConfiguration());

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);

        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
                It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) => Task.Delay(200, ct));

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
        {
            errors = null;
        })).Returns(true);

        var historyRecoveryStateA = new HistoryRecoveryState { Id = HistoryRecoveryIdA, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var historyRecoveryStateB = new HistoryRecoveryState { Id = HistoryRecoveryIdB, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var mvcResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryStateA);
        Assert.Equal((int)Accepted, mvcResult.StatusCode);

        var mvcResult2 = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryStateB);
        Assert.Equal((int)Conflict, mvcResult2.StatusCode);
        Assert.Contains(string.Format(CultureInfo.InvariantCulture, ExistingHistoryRecoveryInProcess, HistoryRecoveryIdA), ((RestApiErrorResponse)mvcResult2.Content).Error);
    }

    [Fact]
    public void StartHistoryRecovery_Saves_State_Configuration()
    {
        var savedConfigCalled = false;
        ICollection<string> outErrors = new List<string>();

        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out _, out _);

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
        {
            savedConfigCalled = true;
            errors = outErrors;
        })).Returns(true);

        var historyRecoveryState = new HistoryRecoveryState { Id = HistoryRecoveryId, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);

        Assert.True(SpinWait.SpinUntil(() => !historyRecoveryState.Status.Equals(OperationStatus.Active), HistoryRecoveryStateChangeWaitTime));
        Assert.True(savedConfigCalled);
    }

    [Fact]
    public void GetHistoryRecoveryState_Test()
    {
        var historyRecoveryStates = new HistoryRecoveryState[2];
        historyRecoveryStates[0] = new HistoryRecoveryState { Id = HistoryRecoveryIdA, Status = OperationStatus.Canceled, StartTime = DateTime.UtcNow.AddSeconds(-1) };
        historyRecoveryStates[1] = new HistoryRecoveryState { Id = HistoryRecoveryIdB, Status = OperationStatus.Complete, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, historyRecoveryStates, out _, out _, out _, out _);

        var mvcResult = historyRecoveryProcessor.GetHistoryRecoveryState(HistoryRecoveryIdA);
        Assert.Equal((int)OK, mvcResult.StatusCode);
        var serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        var deserializedContent = JsonSerializer.Deserialize<HistoryRecoveryState>(serializedContent);
        Assert.Equal(OperationStatus.Canceled, deserializedContent.Status);

        mvcResult = historyRecoveryProcessor.GetHistoryRecoveryState(HistoryRecoveryIdB);
        Assert.Equal((int)OK, mvcResult.StatusCode);
        serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        deserializedContent = JsonSerializer.Deserialize<HistoryRecoveryState>(serializedContent);
        Assert.Equal(OperationStatus.Complete, deserializedContent.Status);
    }

    [Fact]
    public void GetHistoryRecoveryStates_Test()
    {
        var historyRecoveryStates = new HistoryRecoveryState[2];
        historyRecoveryStates[0] = new HistoryRecoveryState { Id = HistoryRecoveryIdA, Status = OperationStatus.Active, StartTime = DateTime.UtcNow.AddSeconds(-1) };
        historyRecoveryStates[1] = new HistoryRecoveryState { Id = HistoryRecoveryIdB, Status = OperationStatus.Complete, StartTime = DateTime.UtcNow.AddSeconds(-1) };

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, historyRecoveryStates, out _, out var mockConfigurationProvider,
            out _, out _);

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
        {
            errors = null;
        })).Returns(true);

        var mvcResult = historyRecoveryProcessor.GetHistoryRecoveryStates();
        Assert.Equal((int)OK, mvcResult.StatusCode);
        var serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        var deserializedContent = JsonSerializer.Deserialize<HistoryRecoveryState[]>(serializedContent);
        Assert.True(deserializedContent.Length == 2);
        Assert.NotNull(deserializedContent.First(d => d.Id == HistoryRecoveryIdA));
        Assert.NotNull(deserializedContent.First(d => d.Id == HistoryRecoveryIdB));
    }

    [Fact]
    public void GetHistoryRecoveryState_InvalidState_Test()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out _, out _, out _);
        var newRecoveryId = $"{HistoryRecoveryIdA}2";
        var mvcResult = historyRecoveryProcessor.GetHistoryRecoveryState(newRecoveryId);
        Assert.Equal((int)NotFound, mvcResult.StatusCode);
        Assert.Contains(newRecoveryId, ((RestApiErrorResponse)mvcResult.Content).Error);
    }

    [Fact]
    public void GetHistoryRecoveryStates_InvalidStates_Test()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out _, out _, out _);
        var mvcResult = historyRecoveryProcessor.GetHistoryRecoveryStates();
        var serializedContent = JsonSerializer.Serialize(mvcResult.Content);
        var deserializedContent = JsonSerializer.Deserialize<HistoryRecoveryState[]>(serializedContent);
        Assert.False(deserializedContent.Length != 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeleteHistoryRecovery_InvalidRecoveryId(string historyRecoveryId)
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out _, out _, out _);
        Assert.ThrowsAny<ArgumentException>(() => historyRecoveryProcessor.DeleteHistoryRecovery(historyRecoveryId));
    }

    [Fact]
    public async Task DeleteHistoryRecovery_NoOutstandingRecoveryToCancel()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
              out _, out _);

        var savedStateArrays = new List<HistoryRecoveryState[]>();

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
        {
            savedStateArrays.Add(configs.ToArray());
            errors = null;
        })).Returns(true);

        var historyRecoveryState = new HistoryRecoveryState() { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };
        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);
        SpinWait.SpinUntil(() => historyRecoveryState.Status == OperationStatus.Complete, HistoryRecoveryStateChangeWaitTime);
        var deleteResult = historyRecoveryProcessor.DeleteHistoryRecovery(historyRecoveryState.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, deleteResult.StatusCode);
        Assert.Equal(3, savedStateArrays.Count);
        Assert.Single(savedStateArrays[0]);
        Assert.Equal(historyRecoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(OperationStatus.Complete, savedStateArrays[0][0].Status);
        Assert.Empty(savedStateArrays.Last());
    }

    [Fact]
    public void DeleteHistoryRecoveryResult_CancelOutstandingRecovery()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);

        var savedStateArrays = new List<HistoryRecoveryState[]>();

        var recoveryCancelled = false;
        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
            It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
            {
                try
                {
                    Task.Delay(-1, ct).GetAwaiter().GetResult();
                }
                catch (TaskCanceledException)
                {
                    recoveryCancelled = true;
                }

                return Task.FromCanceled(ct);
            });

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId,
            CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(
            new SaveHistoryRecoveryStateConfigCallback((string id, string name,
                ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
            {
                savedStateArrays.Add(configs.ToArray());
                errors = null;
            })).Returns(true);

        var historyRecoveryState = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);
        Assert.False(recoveryCancelled);

        var deleteResult = historyRecoveryProcessor.DeleteHistoryRecovery(historyRecoveryState.Id);

        Assert.True(recoveryCancelled);
        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, deleteResult.StatusCode);
        Assert.Equal(3, savedStateArrays.Count);
        Assert.Empty(savedStateArrays.Last());
    }

    [Fact]
    public void CancelHistoryRecovery_CancelOutstandingRecovery()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);

        var savedStateArrays = new List<HistoryRecoveryState[]>();

        var recoveryCancelled = false;
        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
            It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
        {
            try
            {
                Task.Delay(-1, ct).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
                recoveryCancelled = true;
            }

            return Task.FromCanceled(ct);
        });

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId,
            CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(
            new SaveHistoryRecoveryStateConfigCallback((string id, string name,
                ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
            {
                savedStateArrays.Add(configs.ToArray());
                errors = null;
            })).Returns(true);

        var historyRecoveryState = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);
        Assert.False(recoveryCancelled);

        var cancelResult = historyRecoveryProcessor.CancelHistoryRecovery(historyRecoveryState.Id);

        Assert.True(recoveryCancelled);
        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, cancelResult.StatusCode);
        Assert.Single(savedStateArrays.Last());
        Assert.Equal(3, savedStateArrays.Count);
        Assert.Equal(historyRecoveryState.Id, savedStateArrays[0][0].Id);
        Assert.Equal(OperationStatus.Canceled, savedStateArrays[0][0].Status);
        Assert.Equal(OperationStatus.Canceled, savedStateArrays[2][0].Status);
    }

    [Fact]
    public void CancelHistoryRecovery_RecoveryIdNotFound()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _,
            out _, out _, out _);
        var historyRecoveryState1 = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };
        var historyRecoveryState2 = new HistoryRecoveryState { Id = "did2", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState1);
        var cancelResult = historyRecoveryProcessor.CancelHistoryRecovery(historyRecoveryState2.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)NotFound, cancelResult.StatusCode);
    }

    [Fact]
    public async Task CancelHistoryRecovery_RecoveryIdNotRunning()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _,
            out _, out _, out _);
        var historyRecoveryState1 = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState1);

        Assert.True(SpinWait.SpinUntil(() => historyRecoveryProcessor.ActiveRecoveryId == null, HistoryRecoveryStateChangeWaitTime));
        var cancelResult = historyRecoveryProcessor.CancelHistoryRecovery(historyRecoveryState1.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)Conflict, cancelResult.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ResumeHistoryRecovery_InvalidHistoryRecoveryId(string historyRecoveryId)
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);
        Assert.ThrowsAny<ArgumentException>(() => historyRecoveryProcessor.ResumeHistoryRecovery(historyRecoveryId));
    }

    [Fact]
    public void ResumeHistoryRecovery_RecoveryIdNotFound()
    {
        var historyRecoveryState1 = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var initialStates = new[] { historyRecoveryState1 };
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, initialStates, out _,
            out _, out _, out _);

        var historyRecoveryState2 = new HistoryRecoveryState { Id = "did2", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var resumeResult = historyRecoveryProcessor.ResumeHistoryRecovery(historyRecoveryState2.Id);

        Assert.Equal((int)NotFound, resumeResult.StatusCode);
    }

    [Fact]
    public void ResumeHistoryRecovery_RecoveryStateNotCancelledOrFailed()
    {
        var historyRecoveryState = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1), Status = OperationStatus.Complete };

        var initialStates = new[] { historyRecoveryState };
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, initialStates, out _,
            out _, out _, out _);

        var resumeResult = historyRecoveryProcessor.ResumeHistoryRecovery(historyRecoveryState.Id);

        Assert.Equal((int)Conflict, resumeResult.StatusCode);
    }

    [Theory]
    [InlineData(OperationStatus.Failed)]
    [InlineData(OperationStatus.Canceled)]
    public void ResumeHistoryRecovery_OngoingRecovery(OperationStatus status)
    {
        var historyRecoveryState1 = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1), Status = status };
        var initialStates = new[] { historyRecoveryState1 };

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, initialStates, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);

        var savedStateArrays = new List<HistoryRecoveryState[]>();

        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
            It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
            {
                try
                {
                    Task.Delay(-1, ct).GetAwaiter().GetResult();
                }
                catch (TaskCanceledException)
                {
                }

                return Task.FromCanceled(ct);
            });

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId,
            CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(
            new SaveHistoryRecoveryStateConfigCallback((string id, string name,
                ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
            {
                savedStateArrays.Add(configs.ToArray());
                errors = null;
            })).Returns(true);

        var historyRecoveryState2 = new HistoryRecoveryState { Id = "did2", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState2);
        Assert.Equal((int)Accepted, startResult.StatusCode);

        var resumeResult = historyRecoveryProcessor.ResumeHistoryRecovery(historyRecoveryState1.Id);
        Assert.Equal((int)Conflict, resumeResult.StatusCode);
    }

    [Fact]
    public void ResumeHistoryRecovery_Canceled_RunToCompletion_Test()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);

        var savedStateArrays = new List<HistoryRecoveryState[]>();

        var recoveryCancelled = false;
        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
            It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
            {
                try
                {
                    Task.Delay(-1, ct).GetAwaiter().GetResult();
                }
                catch (TaskCanceledException)
                {
                    recoveryCancelled = true;
                }

                return Task.FromCanceled(ct);
            });

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId,
            CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(
            new SaveHistoryRecoveryStateConfigCallback((string id, string name,
                ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
            {
                savedStateArrays.Add(configs.ToArray());
                errors = null;
            })).Returns(true);

        var historyRecoveryState = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);
        Assert.False(recoveryCancelled);

        var cancelResult = historyRecoveryProcessor.CancelHistoryRecovery(historyRecoveryState.Id);

        Assert.True(recoveryCancelled);
        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)OK, cancelResult.StatusCode);

        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
        It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
        {
            try
            {
                Task.Delay(200, ct).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
            }

            return Task.CompletedTask;
        });

        var resumeResult = historyRecoveryProcessor.ResumeHistoryRecovery(historyRecoveryState.Id);
        Assert.Equal((int)Accepted, resumeResult.StatusCode);
        Assert.True(SpinWait.SpinUntil(() => historyRecoveryState.Status.Equals(OperationStatus.Active), HistoryRecoveryStateChangeWaitTime));
        Assert.Null(historyRecoveryState.Errors);
        Assert.Null(historyRecoveryState.Checkpoint);
        Assert.True(SpinWait.SpinUntil(() => historyRecoveryState.Status.Equals(OperationStatus.Complete), HistoryRecoveryStateChangeWaitTime));
    }

    [Fact]
    public void ResumeHistoryRecovery_Failed_RunToCompletion_Test()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);

        var savedStateArrays = new List<HistoryRecoveryState[]>();

        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
            It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
            {
                throw new Exception();
            });

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId,
            CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(
            new SaveHistoryRecoveryStateConfigCallback((string id, string name,
                ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
            {
                savedStateArrays.Add(configs.ToArray());
                errors = null;
            })).Returns(true);

        var historyRecoveryState = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.True(SpinWait.SpinUntil(() => historyRecoveryState.Status.Equals(OperationStatus.Failed), HistoryRecoveryStateChangeWaitTime));

        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
        It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
        {
            try
            {
                Task.Delay(200, ct).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
            }

            return Task.CompletedTask;
        });

        var resumeResult = historyRecoveryProcessor.ResumeHistoryRecovery(historyRecoveryState.Id);
        Assert.Equal((int)Accepted, resumeResult.StatusCode);
        Assert.True(SpinWait.SpinUntil(() => historyRecoveryState.Status.Equals(OperationStatus.Active), HistoryRecoveryStateChangeWaitTime));
        Assert.Null(historyRecoveryState.Errors);
        Assert.Null(historyRecoveryState.Checkpoint);
        Assert.True(SpinWait.SpinUntil(() => historyRecoveryState.Status.Equals(OperationStatus.Complete), HistoryRecoveryStateChangeWaitTime));
    }

    [Fact]
    public void DeleteHistoryRecovery_RecoveryIdNotFound()
    {
        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _,
            out _, out _, out _);
        var historyRecoveryState1 = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };
        var historyRecoveryState2 = new HistoryRecoveryState { Id = "did2", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState1);
        var deleteResult = historyRecoveryProcessor.DeleteHistoryRecovery(historyRecoveryState2.Id);

        Assert.Equal((int)Accepted, startResult.StatusCode);
        Assert.Equal((int)NotFound, deleteResult.StatusCode);
    }

    [Fact]
    public async Task DeleteHistoryRecoveries_MultipleRecoveriesPresent()
    {
        var mockGetDataSourceFunction = new Mock<Func<TestDataSourceConfiguration>>();
        mockGetDataSourceFunction.Setup(x => x.Invoke()).Returns(new TestDataSourceConfiguration());

        using var historyRecoveryProcessor = CreateHistoryRecoveryProcessor<TestDataSourceConfiguration, TestDataSelectionWithId>(ComponentId, null, out _, out var mockConfigurationProvider,
            out var mockHistoryRecoveryFunction, out _);

        var savedStateArrays = new List<HistoryRecoveryState[]>();

        var recoveryCancelled = false;
        object savedStatesLock = new object();

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId,
            CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(
            new SaveHistoryRecoveryStateConfigCallback((string id, string name,
                ICollection<HistoryRecoveryState> configs, out ICollection<string> errors) =>
            {
                lock (savedStatesLock)
                {
                    savedStateArrays.Add(configs.ToArray());
                }

                errors = null;
            })).Returns(true);

        var historyRecoveryState1 = new HistoryRecoveryState { Id = "did1", StartTime = DateTime.UtcNow.AddSeconds(-1) };

        var startResult1 = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState1);
        SpinWait.SpinUntil(() => historyRecoveryState1.Status == OperationStatus.Complete, HistoryRecoveryStateChangeWaitTime);

        mockHistoryRecoveryFunction.Setup(x => x.Invoke(It.IsAny<HistoryRecoveryDetails>(), It.IsAny<TestDataSourceConfiguration>(), It.IsAny<IReadOnlyList<TestDataSelectionWithId>>(), It.IsAny<IAdapterHistoryRecoveryService>(),
            It.IsAny<CancellationToken>())).Returns((HistoryRecoveryDetails a, TestDataSourceConfiguration tdsConfig, IReadOnlyList<TestDataSelectionWithId> selection, IAdapterHistoryRecoveryService service, CancellationToken ct) =>
        {
            try
            {
                Task.Delay(-1, ct).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
                recoveryCancelled = true;
            }

            return Task.FromCanceled(ct);
        });

        var historyRecoveryState2 = new HistoryRecoveryState { Id = "did2", StartTime = DateTime.UtcNow.AddSeconds(-1) };
        var startResult2 = historyRecoveryProcessor.StartOnDemandHistoryRecovery(historyRecoveryState2);
        SpinWait.SpinUntil(() => historyRecoveryState2.Status == OperationStatus.Active && savedStateArrays.Count >= 3, HistoryRecoveryStateChangeWaitTime);

        Assert.False(recoveryCancelled);

        Assert.Equal((int)Accepted, startResult1.StatusCode);
        Assert.Equal((int)Accepted, startResult2.StatusCode);
        Assert.Contains(historyRecoveryState1.Id, savedStateArrays[0][0].Id);
        Assert.Contains(historyRecoveryState1.Id, savedStateArrays[1][0].Id);
        Assert.Equal(OperationStatus.Complete, savedStateArrays[1][0].Status);
        Assert.Contains(historyRecoveryState1.Id, savedStateArrays[2].Select(x => x.Id));
        Assert.Contains(historyRecoveryState2.Id, savedStateArrays[2].Select(x => x.Id));
        Assert.Equal(OperationStatus.Complete, savedStateArrays[2].First(x => x.Id == historyRecoveryState1.Id).Status);
        Assert.Equal(OperationStatus.Active, savedStateArrays[2].First(x => x.Id == historyRecoveryState2.Id).Status);

        Assert.Equal(3, savedStateArrays.Count);
        Assert.Equal(2, savedStateArrays[2].Length);

        var deleteResult = historyRecoveryProcessor.DeleteHistoryRecoveries();
        Assert.Equal((int)OK, deleteResult.StatusCode);
        Assert.True(recoveryCancelled);

        SpinWait.SpinUntil(() => savedStateArrays.Count == 0, HistoryRecoveryStateChangeWaitTime);

        Assert.Empty(savedStateArrays.Last());
    }

    private static OnDemandHistoryRecoveryProcessor<TDataSource, TDataSelection> CreateHistoryRecoveryProcessor<TDataSource, TDataSelection>(string componentId,
        HistoryRecoveryState[] initialHistoryRecoveryStates,
        out Mock<ILogger> mockLogger,
        out Mock<IConfigurationProvider> mockConfigurationProvider,
        out Mock<Func<HistoryRecoveryDetails, TDataSource, IReadOnlyList<TDataSelection>, IAdapterHistoryRecoveryService, CancellationToken, Task>> mockHistoryRecoveryFunction,
        out Mock<IInstrumentedMessageProcessor> mockMessageProcessor)
        where TDataSource : class, IDataSourceConfiguration
        where TDataSelection : class, IDataSelectionConfiguration
    {
        ICollection<string> errors = new List<string>();
        var dataSelection = new TestDataSelectionWithId[5];

        for (var i = 0; i < dataSelection.Length; i++)
        {
            dataSelection[i] = new TestDataSelectionWithId("hello" + i, null, true);
        }

        mockLogger = new Mock<ILogger>();
        mockConfigurationProvider = new Mock<IConfigurationProvider>();
        if (initialHistoryRecoveryStates != null)
        {
            mockConfigurationProvider.Setup(configurationProvider => configurationProvider.TryGetConfiguration(It.IsAny<string>(), CommonConstants.HistoryRecoveryConfigurationName,
                out initialHistoryRecoveryStates, out errors)).Returns(true);
        }

        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(ComponentId, CommonConstants.HistoryRecoveryConfigurationName,
            It.IsAny<ICollection<HistoryRecoveryState>>(), out It.Ref<ICollection<string>>.IsAny)).Callback(new SaveHistoryRecoveryStateConfigCallback((string id, string name,
            ICollection<HistoryRecoveryState> configs, out ICollection<string> otherErrors) =>
        {
            otherErrors = null;
        })).Returns(true);

        mockMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var mockEdgeDataProtector = new Mock<IEdgeDataProtector>();
        var mockHealthMessageProcessor = new Mock<IHealthMessageProcessor>();
        using var egressHealthService = new EgressHealthService(mockHealthMessageProcessor.Object, mockLogger.Object, new ApplicationManifest(), ComponentId);
        mockHistoryRecoveryFunction = new Mock<Func<HistoryRecoveryDetails, TDataSource, IReadOnlyList<TDataSelection>, IAdapterHistoryRecoveryService, CancellationToken, Task>>();

        var historyRecoveryProcessor = new OnDemandHistoryRecoveryProcessor<TDataSource, TDataSelection>(componentId, ComponentType,
            mockLogger.Object, mockConfigurationProvider.Object, mockEdgeDataProtector.Object, mockMessageProcessor.Object, egressHealthService, mockHistoryRecoveryFunction.Object);
        historyRecoveryProcessor.UpdateDataSelectionItems(dataSelection);
        historyRecoveryProcessor.Start(new TestDataSourceConfiguration() { StreamIdPrefix = TestStreamIdPrefix, DefaultStreamIdPattern = TestDefaultStreamIdPattern });

        return historyRecoveryProcessor;
    }
}
