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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Failover.Messages;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests;

public class FailoverDataMessageProcessor_Tests : IDisposable
{
    private const int ProcessOmfMessageTimeout = 500;
    private const string FailoverTestsDirectory = "FailoverUnitTests";
    private readonly List<ISerializedOmfMessage> _sentMessages = new List<ISerializedOmfMessage>();
    private readonly string _commonApplicationDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        EdgeSystemConstants.AdapterFrameworkDirectoryName,
        FailoverTestsDirectory);

    private bool _disposed;

    [Fact]
    public void FailoverDataMessageProcessor_Initialize_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("Initialize");
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var dataEndpointManagerInitialized = false;
        mockOmfDataEndpointManager.Setup(x => x.Initialize(It.IsAny<string>(), It.IsAny<string>())).Callback(() =>
        {
            dataEndpointManagerInitialized = true;
        });

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        Assert.True(dataEndpointManagerInitialized);
    }

    [Fact]
    public void FailoverDataMessageProcessor_ProcessOmfMessage_MessageNull_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_MessageNull");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();
        failoverDataMessageProcessor.ProcessOmfMessage(null);

        Assert.Equal(FailoverMode.NotConfigured, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Secondary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Empty(_sentMessages);
    }

    [Theory]
    [InlineData(FailoverRole.Primary, FailoverRole.Secondary)]
    [InlineData(FailoverRole.Secondary, FailoverRole.Primary)]
    public void FailoverDataMessageProcessor_ProcessOmfMessage_NoneMode_RoleChange_Test(FailoverRole firstRole, FailoverRole secondRole)
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_NoneMode_RoleChange");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);

        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateState(firstRole, default);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.Equal(FailoverMode.NotConfigured, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(firstRole, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Single(_sentMessages);
        Assert.Equal(messageToProcess.MessageType, _sentMessages[0].MessageType);
        Assert.Equal(messageToProcess.ItemCount, _sentMessages[0].ItemCount);

        failoverDataMessageProcessor.UpdateState(secondRole, default);

        var secondMessageToProcess = new FailoverSerializedOmfMessage(MessageType.Data, Array.Empty<byte>(), MessageAction.Default, 2);

        failoverDataMessageProcessor.ProcessOmfMessage(secondMessageToProcess);

        Assert.Equal(FailoverMode.NotConfigured, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(secondRole, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Equal(2, _sentMessages.Count);
        Assert.Equal(secondMessageToProcess.MessageType, _sentMessages[1].MessageType);
        Assert.Equal(secondMessageToProcess.ItemCount, _sentMessages[1].ItemCount);
    }

    [Fact]
    public void FailoverDataMessageProcessor_ProcessOmfMessage_HotMode_SecondaryToPrimary_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_HotMode_SecondaryToPrimary");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(FailoverMode.Hot);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, default);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Secondary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Empty(_sentMessages);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, default);

        var secondMessageToProcess = new FailoverSerializedOmfMessage(MessageType.Data, Array.Empty<byte>(), MessageAction.Default, 2);

        failoverDataMessageProcessor.ProcessOmfMessage(secondMessageToProcess);

        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Primary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.True(SpinWait.SpinUntil(() => _sentMessages.Count == 2, 1500));
        Assert.Equal(messageToProcess.MessageType, _sentMessages[0].MessageType);
        Assert.Equal(messageToProcess.ItemCount, _sentMessages[0].ItemCount);
        Assert.Equal(secondMessageToProcess.MessageType, _sentMessages[1].MessageType);
        Assert.Equal(secondMessageToProcess.ItemCount, _sentMessages[1].ItemCount);

        failoverDataMessageProcessor.UpdateMode(FailoverMode.NotConfigured);
    }

    [Fact]
    public void FailoverDataMessageProcessor_ProcessOmfMessage_HotMode_SecondaryToSecondary_TrimBuffer_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_HotMode_SecondaryToPrimary");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(FailoverMode.Hot);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);
        messageToProcess.ProcessTimeTicks = DateTime.UtcNow.Ticks;

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Secondary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Empty(_sentMessages);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow);
        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, default);

        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Primary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.True(SpinWait.SpinUntil(() => _sentMessages.Count == 0, 1500));

        failoverDataMessageProcessor.UpdateMode(FailoverMode.NotConfigured);
    }

    [Fact]
    public void FailoverDataMessageProcessor_ProcessOmfMessage_HotMode_SecondaryToSecondary_DefaultTimeReceived_NoTrimBuffer_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_HotMode_SecondaryToPrimary");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(FailoverMode.Hot);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, DateTime.UtcNow);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);
        messageToProcess.ProcessTimeTicks = DateTime.UtcNow.Ticks;

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Secondary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Empty(_sentMessages);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, default);
        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, default);

        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Primary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.True(SpinWait.SpinUntil(() => _sentMessages.Count == 1, 1500));

        failoverDataMessageProcessor.UpdateMode(FailoverMode.NotConfigured);
    }

    [Fact]
    public async Task FailoverDataMessageProcessor_ProcessOmfMessage_HotMode_PrimaryToSecondary_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_HotMode_PrimaryToSecondary");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(FailoverMode.Hot);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, default);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);

        Assert.Equal(DateTime.MinValue, failoverDataMessageProcessor.LastDataProcessedTime);
        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.True(SpinWait.SpinUntil(() => _sentMessages.Count > 0, ProcessOmfMessageTimeout));
        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Primary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Single(_sentMessages);
        Assert.Equal(messageToProcess.MessageType, _sentMessages[0].MessageType);
        Assert.Equal(messageToProcess.ItemCount, _sentMessages[0].ItemCount);
        await Task.Delay(100);

        Assert.NotEqual(DateTime.MinValue, failoverDataMessageProcessor.LastDataProcessedTime);

        var cachedLastDataProcessedTime = failoverDataMessageProcessor.LastDataProcessedTime;

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, default);

        var secondMessageToProcess = new FailoverSerializedOmfMessage(MessageType.Data, Array.Empty<byte>(), MessageAction.Default, 2);

        failoverDataMessageProcessor.ProcessOmfMessage(secondMessageToProcess);

        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Secondary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Equal(cachedLastDataProcessedTime, failoverDataMessageProcessor.LastDataProcessedTime);
        Assert.Single(_sentMessages);

        failoverDataMessageProcessor.UpdateMode(FailoverMode.NotConfigured);
    }

    [Theory]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public void FailoverDataMessageProcessor_ProcessOmfMessage_NonHotMode_SecondaryToPrimary_Test(FailoverMode nonHotMode)
    {
        var mockLogger = new Mock<ILogger>();
        List<string> logMessages = new();
        mockLogger.Setup(logger => logger.Log(LogLevel.Debug, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_NonHotMode_SecondaryToPrimary");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(nonHotMode);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, default);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.Equal(nonHotMode, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Secondary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Single(_sentMessages);
        Assert.Single(logMessages);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, default);

        var secondMessageToProcess = new FailoverSerializedOmfMessage(MessageType.Data, Array.Empty<byte>(), MessageAction.Default, 2);

        failoverDataMessageProcessor.ProcessOmfMessage(secondMessageToProcess);

        Assert.Equal(nonHotMode, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Primary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Equal(2, _sentMessages.Count);
        Assert.Equal(messageToProcess.MessageType, _sentMessages[0].MessageType);
        Assert.Equal(messageToProcess.ItemCount, _sentMessages[0].ItemCount);
        Assert.Equal(secondMessageToProcess.MessageType, _sentMessages[1].MessageType);
        Assert.Equal(secondMessageToProcess.ItemCount, _sentMessages[1].ItemCount);

        failoverDataMessageProcessor.UpdateMode(FailoverMode.NotConfigured);
    }

    [Theory]
    [InlineData(FailoverMode.Warm)]
    [InlineData(FailoverMode.Cold)]
    public void FailoverDataMessageProcessor_ProcessOmfMessage_NonHotMode_PrimaryToSecondary_Test(FailoverMode nonHotMode)
    {
        var mockLogger = new Mock<ILogger>();
        List<string> logMessages = new();
        mockLogger.Setup(logger => logger.Log(LogLevel.Debug, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var mockConfigurationProvider = GetMockConfigurationProvider("ProcessOmfMessage_NonHotMode_PrimaryToSecondary");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(nonHotMode);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, default);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);

        Assert.Equal(DateTime.MinValue, failoverDataMessageProcessor.LastDataProcessedTime);

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.NotEqual(DateTime.MinValue, failoverDataMessageProcessor.LastDataProcessedTime);

        var cachedLastDataProcessedTime = failoverDataMessageProcessor.LastDataProcessedTime;

        Assert.Equal(nonHotMode, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Primary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Single(_sentMessages);
        Assert.Equal(messageToProcess.MessageType, _sentMessages[0].MessageType);
        Assert.Equal(messageToProcess.ItemCount, _sentMessages[0].ItemCount);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Secondary, default);

        var secondMessageToProcess = new FailoverSerializedOmfMessage(MessageType.Data, Array.Empty<byte>(), MessageAction.Default, 2);

        failoverDataMessageProcessor.ProcessOmfMessage(secondMessageToProcess);

        Assert.Equal(nonHotMode, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.True(cachedLastDataProcessedTime < failoverDataMessageProcessor.LastDataProcessedTime);
        Assert.Equal(FailoverRole.Secondary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Equal(2, _sentMessages.Count);
        Assert.Single(logMessages);

        failoverDataMessageProcessor.UpdateMode(FailoverMode.NotConfigured);
    }

    [Fact]
    public void FailoverDataMessageProcessor_Dispose_TaskRunning_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("Dispose_TaskRunning");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(FailoverMode.Hot);

        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, default);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.True(SpinWait.SpinUntil(() => _sentMessages.Count > 0, ProcessOmfMessageTimeout));
        Assert.Equal(FailoverMode.Hot, failoverDataMessageProcessor.CurrentFailoverMode);
        Assert.Equal(FailoverRole.Primary, failoverDataMessageProcessor.CurrentFailoverRole);
        Assert.Single(_sentMessages);
        Assert.Equal(messageToProcess.MessageType, _sentMessages[0].MessageType);
        Assert.Equal(messageToProcess.ItemCount, _sentMessages[0].ItemCount);
    }

    [Fact]
    public void FailoverDataMessageProcessor_LastDataProcessedTime_UtcTime_Test()
    {
        var mockLogger = new Mock<ILogger>();
        var mockConfigurationProvider = GetMockConfigurationProvider("LastDataProcessedTime_UtcTime_Test");
        var mockOmfDataEndpointManager = GetMockOmfDataEndpointManager();
        var utcTimeNow = DateTime.UtcNow;

        using var failoverDataMessageProcessor = new FailoverDataMessageProcessor(mockLogger.Object, mockConfigurationProvider.Object, mockOmfDataEndpointManager.Object);
        failoverDataMessageProcessor.Initialize();

        failoverDataMessageProcessor.UpdateMode(FailoverMode.Hot);
        
        failoverDataMessageProcessor.UpdateState(FailoverRole.Primary, utcTimeNow);

        var messageToProcess = new FailoverSerializedOmfMessage(MessageType.Type, Array.Empty<byte>(), MessageAction.Default, 1);

        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        // The LastDataProcessedTime is set AFTER a message a processed. Processing another message guarantees that the LastDataProcessedTime has been set.
        failoverDataMessageProcessor.ProcessOmfMessage(messageToProcess);

        Assert.True(SpinWait.SpinUntil(() => _sentMessages.Count > 1, ProcessOmfMessageTimeout));

        var returnedLastDataProcessedTime = failoverDataMessageProcessor.LastDataProcessedTime;

        Assert.Equal(DateTimeKind.Utc, returnedLastDataProcessedTime.Kind);
        Assert.True(returnedLastDataProcessedTime > utcTimeNow);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        try
        {
            if (Directory.Exists(_commonApplicationDataPath))
            {
                Directory.Delete(_commonApplicationDataPath, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error cleaning up {nameof(FailoverDataMessageProcessor_Tests)}: {ex}");
        }

        _disposed = true;
    }

    private Mock<IConfigurationProvider> GetMockConfigurationProvider(string methodId)
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns(
            Path.Combine(_commonApplicationDataPath, methodId));
        return mockConfigurationProvider;
    }

    private Mock<IOmfDataEndpointManager> GetMockOmfDataEndpointManager()
    {
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        mockOmfDataEndpointManager.Setup(x => x.SendMessage(It.IsAny<ISerializedOmfMessage>())).Callback((ISerializedOmfMessage message) =>
        {
            _sentMessages.Add(message);
        });

        return mockOmfDataEndpointManager;
    }
}
