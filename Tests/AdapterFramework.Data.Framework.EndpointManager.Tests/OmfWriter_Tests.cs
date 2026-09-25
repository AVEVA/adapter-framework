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
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Compression;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

public class OmfWriter_Tests : IDisposable
{
    private const string TestUri = "http://localhost:5465/api/omf/";
    private const string UserName = @"DummyUserName";
    private const string Password = "dummyPassword";
    private const string AppDataPath = "UnitTests";
    private const string ReconnectSuccessString = "Error has been resolved";
    private const string SendMessageFailureString = "No data from this adapter will be successfully processed";
    private const int SuccessfulSendExpectedWaitTime = 1_500;
    private const int BackoffTestDelay = 15_000;
    private const int RetryDelay = 3_000;
    private const int TimeToWaitForBufferedRetry = 5_000;
    private const int SendOmfMessageDelay = 250;
    private const int LogMessageMaxWaitTime = 60_000;
    private const string EndpointId = "a376dd91-d8fd-4166-9f70-ebdc4d7d9ead";
    private const string GoodHttpResponseString = "200 (OK)";
    private readonly TestLogger _testLogger;
    private readonly TestEndpoint _testEndpoint;
    private readonly IEdgeDataProtector _dataProtector;
    private readonly ISerializer _serializer = new OmfJsonSerializer();
    private readonly OmfWriter _omfWriter;
    private readonly OmfWriter _omfWriterBuffered;
    private readonly string _defaultBufferLocation;
    private bool? _endpointInError;
    private bool _disposed;

    public OmfWriter_Tests()
    {
        _testLogger = new TestLogger();
        _dataProtector = TestUtilities.CreateSecretsManagerInstance(null, _testLogger, null);
        _defaultBufferLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AdapterFramework", "UnitTests", EdgeSystemConstants.BuffersDirectoryName).Replace("\\", "/", StringComparison.InvariantCultureIgnoreCase);

        var protectedPassword = _dataProtector.Protect(Password);
        var configurationProvider = new JsonConfigurationProvider("unitTests/unitTests");
        var bufferPartition = Path.Combine(configurationProvider.GetCommonApplicationDataDirectoryPath(), EdgeSystemConstants.BuffersDirectoryName);

        TestUtilities.CleanupDirectories(bufferPartition);
        TestUtilities.CleanupDirectories(_defaultBufferLocation);

        void StatusAction(bool inError)
        {
            _endpointInError = inError;
        }

        _omfWriter = new OmfWriter(
            new EndpointConfigurationBase
            {
                Id = EndpointId,
                Endpoint = TestUri,
                UserName = UserName,
                Password = protectedPassword,
            },
            _testLogger,
            _serializer,
            null,
            _dataProtector,
            new BufferingConfiguration { EnablePersistentBuffering = false, BufferLocation = _defaultBufferLocation },
            new ApplicationManifest(),
            bufferPartition,
            string.Empty,
            OmfWriterType.Data,
            StatusAction);

        _omfWriterBuffered = new OmfWriter(
            new EndpointConfigurationBase
            {
                Id = EndpointId,
                Endpoint = TestUri,
                UserName = UserName,
                Password = protectedPassword,
            },
            _testLogger,
            _serializer,
            null,
            _dataProtector,
            new BufferingConfiguration { EnablePersistentBuffering = true, BufferLocation = _defaultBufferLocation },
            new ApplicationManifest(),
            bufferPartition,
            string.Empty,
            OmfWriterType.Data,
            StatusAction);

        _testEndpoint = new TestEndpoint(TestUri);
    }

    public static IEnumerable<object[]> GetInvalidConfigUpdates()
    {
        var protector = TestUtilities.CreateDataProtectorInstance(null, null);

        yield return new object[]
        {
            new EndpointConfigurationBase()
            {
                Id = "abcde",
                Endpoint = TestUri,
                UserName = UserName,
                Password = protector.Protect(Password),
            },
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(1000, true, OmfWriterType.Health)]
    [InlineData(1000, true, OmfWriterType.Data)]
    [InlineData(1000, false, OmfWriterType.Health)]
    [InlineData(1000, false, OmfWriterType.Data)]
    [InlineData(9000, false, OmfWriterType.Data)]
    [InlineData(1, false, OmfWriterType.Data)]
    public void OmfWriter_Constructor_MaxVolatileMessageSize_Test(int maxBufferSizeMb, bool enablePersistentBuffering, OmfWriterType writerType)
    {
        var defaultVolatileQueueSizeMb = 20;
        var kilobyte = 1024;

        var defaultSize = defaultVolatileQueueSizeMb * kilobyte * kilobyte;
        var configuredSize = (long)maxBufferSizeMb * kilobyte * kilobyte;

        var bufferingConfiguration = new BufferingConfiguration
        {
            BufferLocation = _defaultBufferLocation,
            MaxBufferSizeMB = maxBufferSizeMb,
            EnablePersistentBuffering = enablePersistentBuffering,
        };

        _testLogger.ClearLog();

        using var omfWriter = new OmfWriter(
            new EndpointConfigurationBase
            {
                Id = EndpointId,
                Endpoint = "https://localhost:5595",
                UserName = UserName,
                Password = _dataProtector.Protect(Password),
            },
            _testLogger,
            _serializer,
            new GZipCompressor(),
            _dataProtector,
            bufferingConfiguration,
            new ApplicationManifest(),
            _defaultBufferLocation,
            string.Empty,
            writerType,
            callback => { });

        var backedUpDataQueue = (BackedUpOmfMessageQueueBase<ISerializedOmfMessage>)TestUtilities.GetFieldValueFromObject("_dataQueue", omfWriter);
        var backedUpTypesStreamsQueue = (BackedUpOmfMessageQueueBase<ISerializedOmfMessage>)TestUtilities.GetFieldValueFromObject("_typesAndStreamsQueue", omfWriter);

        var maximumDataQueueSizeBytes = (long)TestUtilities.GetFieldValueFromObject("_maxVolatileQueueSize", backedUpDataQueue);
        var maximumTypesStreamsQueueSizeBytes = (long)TestUtilities.GetFieldValueFromObject("_maxVolatileQueueSize", backedUpTypesStreamsQueue);

        if (writerType == OmfWriterType.Health || enablePersistentBuffering)
        {
            Assert.Equal(defaultSize, maximumDataQueueSizeBytes);
        }
        else
        {
            Assert.False(_testLogger.AreErrorsWarningsInLog());

            Assert.Equal(configuredSize, maximumDataQueueSizeBytes);
        }

        Assert.Equal(defaultSize, maximumTypesStreamsQueueSizeBytes);
    }

    [Fact]
    public void OmfWriter_SendMessage_NoMessageLogged_OnSuccessfulSend()
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.Accepted);
        _testEndpoint.StartListening();

        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _omfWriter.SendMessage(msg);

        Assert.False(_testLogger.ContainsMessage(ReconnectSuccessString));

        _omfWriterBuffered.SendMessage(msg);

        Assert.False(_testLogger.ContainsMessage(ReconnectSuccessString));
    }

    [Fact]
    public async Task OmfWriter_SendMessage_SingleMessageLogged_OnFail()
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.Unauthorized);
        _testEndpoint.StartListening();

        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _omfWriter.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testLogger.ContainsMessage(SendMessageFailureString), SuccessfulSendExpectedWaitTime));
        Assert.True(_testLogger.ContainsMessage("Error sending"));

        _testLogger.ClearLog();

        _omfWriter.SendMessage(msg);

        await Task.Delay(SuccessfulSendExpectedWaitTime);

        Assert.False(_testLogger.ContainsMessage(SendMessageFailureString));
        Assert.False(_testLogger.ContainsMessage("Error sending"));

        _testLogger.ClearLog();

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testLogger.ContainsMessage(SendMessageFailureString), SuccessfulSendExpectedWaitTime));
        Assert.True(_testLogger.ContainsMessage("Error sending"));

        _testLogger.ClearLog();

        _omfWriterBuffered.SendMessage(msg);

        await Task.Delay(SuccessfulSendExpectedWaitTime);

        Assert.False(_testLogger.ContainsMessage(SendMessageFailureString));
        Assert.False(_testLogger.ContainsMessage("Error sending"));
    }

    [Theory]
    [InlineData("1234shdjs3848sjkvhs")]
    [InlineData(TestEndpoint.ClientId)]
    public void OmfWriter_SendMessage_ExponentialBackoff_OnFail(string clientId)
    {
        _testEndpoint.StopListening();
        _testEndpoint.SetHttpResponse(HttpStatusCode.Unauthorized);
        _testEndpoint.StartListening(clientId);

        const string TestMessage = "testMsg_dfgewt23563";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) >= 4, BackoffTestDelay));
        Assert.True(_testEndpoint.NumberOfTimesMessageReceived(TestMessage) < 8);
    }

    [Theory]
    [InlineData(MessageType.Type, 1)]
    [InlineData(MessageType.Container, 1)]
    [InlineData(MessageType.Data, 1)]
    [InlineData(MessageType.DynamicData, 1)]
    [InlineData(MessageType.StaticData, 1)]
    public void OmfWriter_SendMessage_NotFound_DataMessageBreaksOut(MessageType messageType, int expectedRetries)
    {
        _testEndpoint.StopListening();
        _testEndpoint.SetHttpResponse(HttpStatusCode.NotFound);
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_dfgewt23563";
        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) == expectedRetries, TimeToWaitForBufferedRetry));
        Assert.True(SpinWait.SpinUntil(() => _testLogger.GetLogMessages().Count(message => message.LogLevel == LogLevel.Error) == 1, TimeToWaitForBufferedRetry));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    public async Task OmfWriter_SendMessage_NotFound_MetadataInQueue(MessageType messageType)
    {
        _testEndpoint.StopListening();
        _testEndpoint.SetHttpResponse(HttpStatusCode.NotFound);
        _testEndpoint.StartListening();

        var isFirstMessage = true;
        const string TestDataMessage1 = "DataMessage1";
        const string TestDataMessage2 = "DataMessage2";
        const string TestMetadataMessage = "MetadataMessage";

        var dataMessage1 = new SerializedOmfMessage(MessageType.DynamicData, Encoding.UTF8.GetBytes(TestDataMessage1), MessageAction.Default);
        var dataMessage2 = new SerializedOmfMessage(MessageType.DynamicData, Encoding.UTF8.GetBytes(TestDataMessage2), MessageAction.Default);
        var metadataMessage = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(TestMetadataMessage), MessageAction.Default);

        _testLogger.ClearLog();
        _testEndpoint.SetMessageReceivedAction(EndpointReceivedMessage);

        _omfWriter.SendMessage(dataMessage1);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(TestDataMessage1), TimeToWaitForBufferedRetry));

        _testEndpoint.SetHttpResponse(HttpStatusCode.Accepted);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(TestMetadataMessage), TimeToWaitForBufferedRetry));
        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestDataMessage1) == 2, TimeToWaitForBufferedRetry));

        _testEndpoint.SetHttpResponse(HttpStatusCode.NotFound);

        _omfWriter.SendMessage(dataMessage2);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestDataMessage2) == 1, TimeToWaitForBufferedRetry));

        await Task.Delay(TimeToWaitForBufferedRetry);

        Assert.Equal(1, _testEndpoint.NumberOfTimesMessageReceived(TestDataMessage2));

        void EndpointReceivedMessage()
        {
            if (isFirstMessage)
            {
                _omfWriter.SendMessage(metadataMessage);
                isFirstMessage = false;
            }
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void OmfWriter_SendMessage_Delay_TimeSpan(HttpStatusCode statusCode)
    {
        var delay = TimeSpan.FromSeconds(1);
        _testEndpoint.StopListening();
        _testEndpoint.SetHttpResponse(statusCode, new Dictionary<string, string> { { "Retry-After", Math.Round(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture) } });
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_dfgewt23563";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) >= 4, TimeToWaitForBufferedRetry));
        Assert.True(_testEndpoint.NumberOfTimesMessageReceived(TestMessage) < 7);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void OmfWriter_SendMessage_Delay_DateTime(HttpStatusCode statusCode)
    {
        var delay = DateTime.UtcNow.AddSeconds(2);
        _testEndpoint.StopListening();
        _testEndpoint.SetHttpResponse(statusCode, new Dictionary<string, string> { { "Retry-After", delay.ToString("R") } });
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_dfgewt23563";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg);

        SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) == 1);

        _testEndpoint.SetHttpResponse(HttpStatusCode.Accepted);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) == 2, RetryDelay));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void OmfWriter_SendMessage_Delay_BadDateTime(HttpStatusCode statusCode)
    {
        var delay = DateTime.UtcNow.AddSeconds(-5);
        _testEndpoint.StopListening();
        _testEndpoint.SetHttpResponse(statusCode, new Dictionary<string, string> { { "Retry-After", delay.ToString("R") } });
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_dfgewt23563";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) >= 1, 2000));

        _testEndpoint.SetHttpResponse(HttpStatusCode.Accepted);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) >= 2, 2000));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void OmfWriter_SendMessage_Delay_NoHeader(HttpStatusCode statusCode)
    {
        _testEndpoint.StopListening();
        _testEndpoint.SetHttpResponse(statusCode);
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_dfgewt23563";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) == 1, RetryDelay));
    }

    [Fact]
    public async Task OmfWriter_SendMessage_SingleMessageLogged_OnReconnect_NoBuffer()
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.Unauthorized);
        _testEndpoint.StartListening();

        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _omfWriter.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testLogger.ContainsMessage(SendMessageFailureString), LogMessageMaxWaitTime));

        _testEndpoint.SetHttpResponse(HttpStatusCode.Accepted);

        _omfWriter.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testLogger.ContainsMessage(ReconnectSuccessString), SuccessfulSendExpectedWaitTime));

        _testLogger.ClearLog();

        _omfWriter.SendMessage(msg);

        await Task.Delay(SuccessfulSendExpectedWaitTime);

        Assert.False(_testLogger.ContainsMessage(ReconnectSuccessString));
    }

    [Fact]
    public async Task OmfWriter_SendMessage_SingleMessageLogged_OnReconnect_Buffered()
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.Unauthorized);
        _testEndpoint.StartListening();

        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Container, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testLogger.ContainsMessage(SendMessageFailureString), LogMessageMaxWaitTime));

        _testEndpoint.SetHttpResponse(HttpStatusCode.Accepted);

        _omfWriterBuffered.SendMessage(msg);
        Assert.True(SpinWait.SpinUntil(() => _testLogger.ContainsMessage(ReconnectSuccessString), SuccessfulSendExpectedWaitTime));

        _testLogger.ClearLog();

        _omfWriterBuffered.SendMessage(msg);
        await Task.Delay(SuccessfulSendExpectedWaitTime);

        Assert.False(_testLogger.ContainsMessage(ReconnectSuccessString));
    }

    [Theory]
    [MemberData(nameof(GetInvalidConfigUpdates))]
    public void OmfWriter_UpdateConfigurationInvalid_Fails(EndpointConfigurationBase config)
    {
        Assert.Throws<InvalidOperationException>(() => _omfWriter.UpdateConfiguration(config));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public async Task OmfWriter_SendMessage_Success(MessageType messageType)
    {
        var dataCount = 10;
        _testEndpoint.StartListening();

        _testLogger.ClearLog();

        await GenerateAndSendOmfMessageAsync(messageType, false, dataCount);

        Assert.True(_omfWriter.GetAndResetEgressedValuesCounter() == dataCount);
        Assert.True(_omfWriter.GetAndResetEgressedValuesCounter() == 0);
    }

    [Fact]
    public void OmfWriter_SendMessage_Success_CountsResources()
    {
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_resourceCounts_success";
        var resourceCounts = new OmfResourceCounts(10, 2, 3);
        var msg = new SerializedOmfMessage(MessageType.Instance, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Create, 15, OmfVersion.Omf20)
        {
            ResourceCounts = resourceCounts,
        };

        _omfWriter.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) == 1, TimeToWaitForBufferedRetry));

        var egressed = default(OmfResourceCounts);
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                var counts = _omfWriter.GetAndResetEgressedResourceCounters();
                egressed = new OmfResourceCounts(egressed.StreamingValues + counts.StreamingValues, egressed.Assets + counts.Assets, egressed.Events + counts.Events);
                return egressed == resourceCounts;
            },
            SuccessfulSendExpectedWaitTime));

        Assert.True(_omfWriter.GetAndResetEgressedResourceCounters().IsEmpty);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    public void OmfWriter_SendMessage_Rejected_DoesNotCountResources(HttpStatusCode statusCode)
    {
        _testEndpoint.SetHttpResponse(statusCode);
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_resourceCounts_rejected";
        var msg = new SerializedOmfMessage(MessageType.Instance, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Create, 15, OmfVersion.Omf20)
        {
            ResourceCounts = new OmfResourceCounts(10, 2, 3),
        };

        _omfWriter.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) >= 1, TimeToWaitForBufferedRetry));

        // The existing IORate counter still counts processed-but-rejected messages; the per-resource counters only count delivered ones.
        Assert.True(SpinWait.SpinUntil(() => _omfWriter.GetAndResetEgressedValuesCounter() == 15, SuccessfulSendExpectedWaitTime));
        Assert.True(_omfWriter.GetAndResetEgressedResourceCounters().IsEmpty);
    }

    [Fact]
    public void OmfWriterWithBuffering_SendMessage_RetriedThenDelivered_CountsResourcesOnce()
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.ServiceUnavailable, new Dictionary<string, string> { { "Retry-After", "1" } });
        _testEndpoint.StartListening();

        const string TestMessage = "testMsg_resourceCounts_retried";
        var resourceCounts = new OmfResourceCounts(10, 2, 3);
        var msg = new SerializedOmfMessage(MessageType.Instance, Encoding.UTF8.GetBytes(TestMessage), MessageAction.Create, 15, OmfVersion.Omf20)
        {
            ResourceCounts = resourceCounts,
        };

        _omfWriterBuffered.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) >= 2, TimeToWaitForBufferedRetry));
        Assert.True(_omfWriterBuffered.GetAndResetEgressedResourceCounters().IsEmpty);

        var attemptsBeforeRecovery = _testEndpoint.NumberOfTimesMessageReceived(TestMessage);
        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(TestMessage) > attemptsBeforeRecovery, TimeToWaitForBufferedRetry));
        Thread.Sleep(SuccessfulSendExpectedWaitTime);

        Assert.Equal(resourceCounts, _omfWriterBuffered.GetAndResetEgressedResourceCounters());
        Assert.True(_omfWriterBuffered.GetAndResetEgressedResourceCounters().IsEmpty);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.StaticData)]
    [InlineData(MessageType.DynamicData)]
    public async Task OmfWriter_SendMessage_TraceResponseLogMessages(MessageType messageType)
    {
        _testEndpoint.StartListening();

        _testLogger.ClearLog();
        _testLogger.CurrentLogLevel = LogLevel.Information;

        await GenerateAndSendOmfMessageAsync(messageType, false);

        Assert.False(_testLogger.ContainsMessage(GoodHttpResponseString));

        _testLogger.ClearLog();
        _testLogger.CurrentLogLevel = LogLevel.Debug;

        await GenerateAndSendOmfMessageAsync(messageType, false);

        Assert.False(_testLogger.ContainsMessage(GoodHttpResponseString));

        _testLogger.ClearLog();
        _testLogger.CurrentLogLevel = LogLevel.Trace;

        await GenerateAndSendOmfMessageAsync(messageType, false);

        Assert.True(_testLogger.ContainsMessage(GoodHttpResponseString));
    }

    [Theory]
    [InlineData(MessageType.Type, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.Container, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.Data, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.StaticData, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.Type, HttpStatusCode.Conflict)]
    [InlineData(MessageType.Container, HttpStatusCode.Conflict)]
    [InlineData(MessageType.Data, HttpStatusCode.Conflict)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.Conflict)]
    [InlineData(MessageType.StaticData, HttpStatusCode.Conflict)]
    [InlineData(MessageType.StaticData, HttpStatusCode.InternalServerError)]
    [InlineData(MessageType.Type, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.Container, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.Data, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.StaticData, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.Type, HttpStatusCode.NotFound)]
    [InlineData(MessageType.Container, HttpStatusCode.NotFound)]
    [InlineData(MessageType.Data, HttpStatusCode.NotFound)]
    [InlineData(MessageType.StaticData, HttpStatusCode.NotFound)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.NotFound)]
    public async Task OmfWriter_SendMessage_DebugEndpointResponseLogMessages(MessageType messageType, HttpStatusCode responseStatus)
    {
        var responseStatusMessage = $"{(int)responseStatus} ({responseStatus})";

        _testEndpoint.SetHttpResponse(responseStatus);
        _testEndpoint.StartListening();

        _testLogger.ClearLog();
        _testLogger.CurrentLogLevel = LogLevel.Information;

        await GenerateAndSendOmfMessageAsync(messageType, true);

        Assert.False(_testLogger.ContainsMessage(responseStatusMessage));

        _testLogger.ClearLog();
        _testLogger.CurrentLogLevel = LogLevel.Debug;

        await GenerateAndSendOmfMessageAsync(messageType, true);

        Assert.True(_testLogger.ContainsMessage(responseStatusMessage));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.StaticData)]
    [InlineData(MessageType.DynamicData)]
    public async Task OmfWriter_SendMessage_Buffered_Success(MessageType messageType)
    {
        _testEndpoint.StartListening();

        _testLogger.ClearLog();

        await GenerateAndSendOmfMessageAsync(messageType, false);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.StaticData)]
    [InlineData(MessageType.DynamicData)]
    public async Task OmfWriter_SendMessage_BadResponseCode(MessageType messageType)
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.BadRequest);
        _testEndpoint.StartListening();

        _testLogger.ClearLog();

        await GenerateAndSendOmfMessageAsync(messageType, true);
    }

    [Theory]
    [InlineData(MessageType.Type, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.Container, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.Data, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.StaticData, HttpStatusCode.BadRequest)]
    [InlineData(MessageType.Type, HttpStatusCode.Conflict)]
    [InlineData(MessageType.Container, HttpStatusCode.Conflict)]
    [InlineData(MessageType.Data, HttpStatusCode.Conflict)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.Conflict)]
    [InlineData(MessageType.StaticData, HttpStatusCode.Conflict)]
    [InlineData(MessageType.StaticData, HttpStatusCode.InternalServerError)]
    [InlineData(MessageType.Type, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.Container, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.Data, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.StaticData, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.NotImplemented)]
    [InlineData(MessageType.Type, HttpStatusCode.Forbidden)]
    [InlineData(MessageType.Container, HttpStatusCode.Forbidden)]
    [InlineData(MessageType.Data, HttpStatusCode.Forbidden)]
    [InlineData(MessageType.StaticData, HttpStatusCode.Forbidden)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.Forbidden)]
    public void OmfWriter_SendMessage_Buffered_NoRepeatError_DoesNotBlockBuffer(MessageType messageType, HttpStatusCode statusCode)
    {
        _testEndpoint.SetHttpResponse(statusCode);
        _testEndpoint.StartListening();

        var testMessage = _serializer.Serialize("testMsg_asjddfh31471724");
        var msg = new SerializedOmfMessage(messageType, testMessage, MessageAction.Default);

        _testLogger.ClearLog();

        _omfWriterBuffered.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(messageType).ToString() },
        };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(_serializer.Deserialize<string>(testMessage), expectedHeaders), TimeToWaitForBufferedRetry));
        Assert.True(SpinWait.SpinUntil(() => _testLogger.AreErrorsWarningsInLog(), TimeToWaitForBufferedRetry));

        var testMessage2 = _serializer.Serialize("testMsg_osadfshfsd");
        var msg2 = new SerializedOmfMessage(messageType, testMessage2, MessageAction.Default);

        _omfWriterBuffered.SendMessage(msg2);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(_serializer.Deserialize<string>(testMessage2), expectedHeaders), TimeToWaitForBufferedRetry));
    }

    [Theory]
    [InlineData(MessageType.Type, HttpStatusCode.InternalServerError)]
    [InlineData(MessageType.Container, HttpStatusCode.InternalServerError)]
    [InlineData(MessageType.Data, HttpStatusCode.InternalServerError)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.InternalServerError)]
    [InlineData(MessageType.Type, HttpStatusCode.ServiceUnavailable)]
    [InlineData(MessageType.Container, HttpStatusCode.ServiceUnavailable)]
    [InlineData(MessageType.Data, HttpStatusCode.ServiceUnavailable)]
    [InlineData(MessageType.DynamicData, HttpStatusCode.ServiceUnavailable)]
    [InlineData(MessageType.StaticData, HttpStatusCode.ServiceUnavailable)]
    public void OmfWriter_SendMessage_Buffered_Blocking_BlockBuffer(MessageType messageType, HttpStatusCode statusCode)
    {
        _testEndpoint.SetHttpResponse(statusCode);
        _testEndpoint.StartListening();
        var rawTestMessage = "testMsg_asjddfh31471724";
        var testMessage = _serializer.Serialize(rawTestMessage);
        var msg = new SerializedOmfMessage(messageType, testMessage, MessageAction.Default);

        var delay = TimeSpan.FromSeconds(1);
        _testEndpoint.SetHttpResponse(statusCode, new Dictionary<string, string> { { "Retry-After", Math.Round(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture) } });

        _omfWriterBuffered.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(messageType).ToString() },
        };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(rawTestMessage, expectedHeaders), TimeToWaitForBufferedRetry));
        Assert.True(SpinWait.SpinUntil(() => _testLogger.AreErrorsWarningsInLog(), TimeToWaitForBufferedRetry));
        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.NumberOfTimesMessageReceived(rawTestMessage) >= 2, RetryDelay));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.StaticData)]
    [InlineData(MessageType.DynamicData)]
    public void OmfWriterWithBuffering_SendMessage_EndpointOffline_MessageBufferedAndRetried(MessageType messageType)
    {
        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(messageType, _serializer.Serialize(testMessage), MessageAction.Default);

        _testLogger.ClearLog();

        _omfWriterBuffered.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(messageType).ToString() },
        };

        // A message in the log should indicate the message failed to send and will be retried
        Assert.True(SpinWait.SpinUntil(() => _testLogger.AreErrorsWarningsInLog(), TimeToWaitForBufferedRetry));
        Assert.False(_testEndpoint.VerifyMessageReceived(testMessage, expectedHeaders));

        _testEndpoint.StartListening();

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(testMessage, expectedHeaders), TimeToWaitForBufferedRetry));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    public void OmfWriterWithBuffering_SendMessage_UsesCompression(MessageType messageType)
    {
        var configWithCompression = new EndpointConfigurationBase
        {
            Endpoint = TestUri,
            UserName = UserName,
            Password = Password,
        };

        var compressor = new GZipCompressor();

        // in-memory buffering
        var bufferingConfiguration = new BufferingConfiguration
        {
            EnablePersistentBuffering = false,
            BufferLocation = _defaultBufferLocation,
            MaxBufferSizeMB = 1,
        };

        using var omfBufferedWriter = new OmfWriter(
            configWithCompression,
            _testLogger,
            _serializer,
            compressor,
            _dataProtector,
            bufferingConfiguration,
            new ApplicationManifest(),
            "UnitTest",
            string.Empty,
            OmfWriterType.Data,
            Action);

        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessage = "testMsg_asjddfh31471724";

        omfBufferedWriter.SendMessage(new SerializedOmfMessage(messageType, compressor.Compress(_serializer.Serialize(testMessage)), MessageAction.Default));

        var expectedHeaders = new Dictionary<string, string>()
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
            { EndpointManagerConstants.MessageCompressionHeaderKey, "gzip" }, // This header indicates compression was used
        };

        // The TestEndpoint attempts to decompress a received message when the compression header is set, so we will only find the message using the original text if
        // the message was compressed by the DiskBufferedOmfWriter
        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(testMessage, expectedHeaders), SuccessfulSendExpectedWaitTime));

        static void Action(bool obj)
        {
        }
    }

    [Fact]
    public void OmfWriterFactory_DeleteBufferForDisk()
    {
        _testLogger.ClearLog();
        var mockDataProtector = new Mock<IEdgeDataProtector>();
        mockDataProtector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("123");

        var factory = new OmfWriterFactory(mockDataProtector.Object, new OmfJsonSerializer(), new JsonConfigurationProvider(AppDataPath), new ApplicationManifest());

        var omfEndpointConfig = new EndpointConfigurationBase
        {
            Id = Guid.NewGuid().ToString(),
            Endpoint = "https://localhost:5465/api/omf/",
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfiguration = new BufferingConfiguration
        {
            EnablePersistentBuffering = true,
            BufferLocation = _defaultBufferLocation,
            MaxBufferSizeMB = 20,
        };

        var omfWriter = factory.GetOmfWriterInstance(omfEndpointConfig, _testLogger, OmfWriterType.Health, bufferingConfiguration, Action);
        Assert.NotNull(omfWriter);
        omfWriter.Dispose();
        omfWriter.DeleteBuffers();
        Assert.Empty(_testLogger.GetLogMessages());
        omfWriter.DeleteBuffers();
        Assert.Empty(_testLogger.GetLogMessages());

        static void Action(bool obj)
        {
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OmfWriterFactory_DeleteBufferForNonDisk(bool bufferingEnabled)
    {
        _testLogger.ClearLog();
        var mockDataProtector = new Mock<IEdgeDataProtector>();
        mockDataProtector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("123");

        var factory = new OmfWriterFactory(mockDataProtector.Object, new OmfJsonSerializer(),
            new JsonConfigurationProvider(AppDataPath), new ApplicationManifest());

        var omfEndpointConfig = new EndpointConfigurationBase
        {
            Id = Guid.NewGuid().ToString(),
            Endpoint = "https://localhost:5465/api/omf/",
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfiguration = new BufferingConfiguration
        {
            EnablePersistentBuffering = bufferingEnabled,
            BufferLocation = _defaultBufferLocation,
        };

        var omfWriter = factory.GetOmfWriterInstance(omfEndpointConfig, _testLogger, OmfWriterType.Health,
            bufferingConfiguration, Action);

        Assert.NotNull(omfWriter);
        omfWriter.Dispose();
        omfWriter.DeleteBuffers();
        Assert.Empty(_testLogger.GetLogMessages());

        static void Action(bool obj)
        {
        }
    }

    [Fact]
    public void OmfWriter_SendMessage_SuccessfulUpdatesStatus()
    {
        _testEndpoint.StartListening();

        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Type, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _omfWriter.SendMessage(msg);
        Assert.True(SpinWait.SpinUntil(() => _endpointInError == false, SuccessfulSendExpectedWaitTime));
    }

    [Fact]
    public void OmfWriter_SendMessage_FailUpdatesStatus()
    {
        _testEndpoint.StartListening();
        _testEndpoint.SetHttpResponse(HttpStatusCode.BadRequest);
        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Type, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _omfWriter.SendMessage(msg);
        Assert.True(_endpointInError = true);
    }

    [Fact]
    public void OmfWriter_SendMessage_SuccessToFailUpdatesStatus()
    {
        _testEndpoint.StartListening();
        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Type, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _omfWriter.SendMessage(msg);
        Assert.True(SpinWait.SpinUntil(() => _endpointInError == false, SuccessfulSendExpectedWaitTime));

        _testEndpoint.SetHttpResponse(HttpStatusCode.Unauthorized);
        _omfWriter.SendMessage(msg);
        Assert.True(SpinWait.SpinUntil(() => _endpointInError == true, SuccessfulSendExpectedWaitTime));
    }

    [Fact]
    public void OmfWriter_SendMessage_FailToSuccessUpdatesStatus()
    {
        _testEndpoint.StartListening();
        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(MessageType.Type, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default);

        _testEndpoint.SetHttpResponse(HttpStatusCode.Unauthorized);
        _omfWriter.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _endpointInError == true, SuccessfulSendExpectedWaitTime));

        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _omfWriter.SendMessage(msg);
        Assert.True(SpinWait.SpinUntil(() => _endpointInError == false, SuccessfulSendExpectedWaitTime));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _testEndpoint?.Dispose();
            _omfWriter?.Dispose();
            _omfWriterBuffered?.Dispose();
            _omfWriterBuffered?.DeleteBuffers();
        }

        _disposed = true;
    }

    private static MessageType ToExpectedMessageType(MessageType messageType)
    {
        if (messageType == MessageType.StaticData || messageType == MessageType.DynamicData)
        {
            return MessageType.Data;
        }

        return messageType;
    }

    private static string GetBasicAuthString(string username = UserName, string password = Password)
    {
        return EndpointManagerConstants.BasicAuthorizationType + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
    }

    private async Task GenerateAndSendOmfMessageAsync(MessageType messageType, bool expectErrorsOrWarnings, int dataCount = 10)
    {
        var testMessage = "testMsg_asjddfh31471724";
        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(testMessage), MessageAction.Default, dataCount);

        _omfWriter.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(messageType).ToString() },
        };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(testMessage, expectedHeaders), TimeToWaitForBufferedRetry));

        if (expectErrorsOrWarnings)
        {
            Assert.True(SpinWait.SpinUntil(() => _testLogger.AreErrorsWarningsInLog(), TimeToWaitForBufferedRetry * 2));
        }
        else
        {
            Assert.False(SpinWait.SpinUntil(() => _testLogger.AreErrorsWarningsInLog(), TimeToWaitForBufferedRetry));
        }

        await Task.Delay(SendOmfMessageDelay);
    }
}
