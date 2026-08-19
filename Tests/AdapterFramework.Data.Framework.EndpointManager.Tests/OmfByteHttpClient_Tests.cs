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
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Compression;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

public class OmfByteHttpClient_Tests : IDisposable
{
    private const string TestUri = "http://localhost:9876/api/omf/";
    private const string UserName = @"dev\user";
    private const string Password = "password";
    private const string MessageFormat = "json";
    private const string Id = "ID";
    private const string TestMessage = "testMsg_asjddfh31471724";
    private readonly TestEndpoint _testEndpoint;
    private readonly EndpointConfigurationBase _testOmfEndpointConfiguration;
    private readonly IEdgeDataProtector _dataProtector;
    private readonly string _protectedPassword;
    private readonly Mock<IApplicationManifest> _mockApplicationManifest;
    private readonly IEdgeEventProvider _edgeEventProvider;
    private readonly string _debugLogsLocation;
    private OmfByteHttpClient _omfByteHttpClient;
    private bool _disposed;
        
    public OmfByteHttpClient_Tests()
    {
        _debugLogsLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            EdgeSystemConstants.AdapterFrameworkDirectoryName,
            "EgressLogs", " ")
            .TrimEnd();

        TestUtilities.CleanupDirectories(_debugLogsLocation);

        var logger = new TestLogger();
        _dataProtector = TestUtilities.CreateSecretsManagerInstance(null, logger, null);
        _protectedPassword = _dataProtector.Protect(Password);
        _mockApplicationManifest = new Mock<IApplicationManifest>();
        _mockApplicationManifest.Setup(x => x.IsEdgeDataStore).Returns(false);
        _edgeEventProvider = new TestEdgeEventProvider();

        _testOmfEndpointConfiguration = new EndpointConfigurationBase
        {
            Id = Id,
            Endpoint = TestUri,
            UserName = UserName,
            Password = _protectedPassword,
            ValidateEndpointCertificate = true,
        };

        _omfByteHttpClient = new OmfByteHttpClient(
            _testOmfEndpointConfiguration,
            _dataProtector,
            _mockApplicationManifest.Object,
            logger,
            null,
            MessageFormat,
            _debugLogsLocation,
            _edgeEventProvider);

        _testEndpoint = new TestEndpoint(TestUri);
    }

    public static IEnumerable<object[]> GetValidConfigUpdates()
    {
        var protector = TestUtilities.CreateDataProtectorInstance(null, null);
        yield return new object[]
        {
                new EndpointConfigurationBase
                {
                    Id = Id,
                    Endpoint = TestUri,
                    UserName = "abcde",
                    Password = protector.Protect(Password),
                },
        };
        yield return new object[]
        {
            new EndpointConfigurationBase
            {
                Id = Id,
                Endpoint = TestUri,
                UserName = UserName,
                Password = protector.Protect("abcde"),
            },
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.DynamicData)]
    [InlineData(MessageType.StaticData)]
    public async Task OmfByteHttpClient_SendMessageAsyncAdapter_Success(MessageType messageType)
    {
        var expectedStatusCode = HttpStatusCode.OK;
        _testEndpoint.SetHttpResponse(expectedStatusCode);
        _testEndpoint.StartListening();

        var egressEvents = new List<HttpRequestExecutionInfo>();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        var response = await _omfByteHttpClient.SendMessageAsync(messageType, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);
        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(messageType).ToString() },
        };

        Assert.True(_testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders));

        // Verify that the event was raised and contains the correct information
        var egressEvent = await _edgeEventProvider.EdgeEventChannel.Reader.ReadAsync();
        Assert.NotNull(egressEvent);
        Assert.Equal(expectedStatusCode, (egressEvent as HttpRequestExecutionInfo)?.HttpStatusCode);
        Assert.Equal(messageType, (egressEvent as HttpRequestExecutionInfo)?.MessageType);

        // Verify that no more events are in the channel
        Assert.False(_edgeEventProvider.EdgeEventChannel.Reader.TryRead(out var _), "Channel should contain only one event.");
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.DynamicData)]
    [InlineData(MessageType.StaticData)]
    public async Task OmfByteHttpClient_SendMessageAsyncEdsStorage_Success(MessageType messageType)
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        var mockApplicationManifest = new Mock<IApplicationManifest>();
        mockApplicationManifest.Setup(x => x.IsEdgeDataStore).Returns(true);

        _omfByteHttpClient = new OmfByteHttpClient(
            _testOmfEndpointConfiguration,
            _dataProtector,
            mockApplicationManifest.Object,
            new TestLogger(),
            null,
            MessageFormat,
            _debugLogsLocation);

        var response = await _omfByteHttpClient.SendMessageAsync(messageType, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);
        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.EdsCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(messageType).ToString() },
        };

        Assert.True(_testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OmfByteHttpClient_ValidateEndpointCertificate(bool validateCertificate)
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        _omfByteHttpClient.Dispose();

        var testOmfEndpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = TestUri,
            UserName = UserName,
            Password = _protectedPassword,
            ValidateEndpointCertificate = validateCertificate,
        };

        var testLogger = new TestLogger();
        _omfByteHttpClient = new OmfByteHttpClient(
            testOmfEndpointConfiguration,
            _dataProtector,
            _mockApplicationManifest.Object,
            testLogger,
            null,
            MessageFormat,
            string.Empty);

        var warningPrinted = WasEndpointCertificateValidationWarningPrinted(testLogger);

        if (validateCertificate == false && new Uri(TestUri).Scheme != Uri.UriSchemeHttp)
        {
            Assert.True(warningPrinted);
        }
        else
        {
            Assert.False(warningPrinted);
        }

        var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, MessageType.Data.ToString() },
        };

        Assert.True(_testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(UserName, Password)]
    public async Task OmfByteHttpClient_AuthenticationHeaders(string userName, string passWord)
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        _omfByteHttpClient.Dispose();

        var testOmfEndpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = TestUri,
            UserName = userName,
            Password = passWord,
        };

        _omfByteHttpClient = new OmfByteHttpClient(
            testOmfEndpointConfiguration,
            _dataProtector,
            _mockApplicationManifest.Object,
            new TestLogger(),
            null,
            MessageFormat,
            _debugLogsLocation);

        var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
        };

        Assert.False(Directory.Exists(_debugLogsLocation));
        Assert.True(!string.IsNullOrEmpty(userName)
            ? _testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders)
            : _testEndpoint.VerifyMessageReceived(TestMessage));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(UserName, Password)]
    public async Task OmfByteHttpClient_AcceptVerbosityHeader(string userName, string passWord)
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        _omfByteHttpClient.Dispose();

        var testOmfEndpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = TestUri,
            UserName = userName,
            Password = passWord,
        };

        _omfByteHttpClient = new OmfByteHttpClient(
            testOmfEndpointConfiguration,
            _dataProtector,
            _mockApplicationManifest.Object,
            new TestLogger(),
            null,
            MessageFormat,
            _debugLogsLocation);

        var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AcceptVerbosityKey, EndpointManagerConstants.AcceptVerbosityValue },
        };

        Assert.True(!string.IsNullOrEmpty(userName)
            ? _testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders)
            : _testEndpoint.VerifyMessageReceived(TestMessage));
    }

    [Fact]
    public async Task OmfByteHttpClient_ProtectedPasswordToken_ClientUnprotectsPasswordToken()
    {
        var protectedPasswordToken = _dataProtector.Protect(Password);
        Assert.NotEqual(Password, protectedPasswordToken);

        _omfByteHttpClient.Dispose();
        var testOmfEndpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = TestUri,
            UserName = UserName,
            Password = protectedPasswordToken,
            ValidateEndpointCertificate = true,
        };

        _omfByteHttpClient = new OmfByteHttpClient(
            testOmfEndpointConfiguration,
            _dataProtector,
            _mockApplicationManifest.Object,
            new TestLogger(),
            null,
            MessageFormat,
            _debugLogsLocation);

        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);
        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, MessageType.Data.ToString() },
        };

        Assert.True(_testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders));
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.DynamicData)]
    [InlineData(MessageType.StaticData)]
    public async Task OmfByteHttpClient_SendMessageAsync_CompressionHeaderSet(MessageType messageType)
    {
        _omfByteHttpClient.Dispose();
        const string CreateAction = "create";
        const string UpdateAction = "update";
        var compressor = new GZipCompressor();

        _omfByteHttpClient = new OmfByteHttpClient(
            _testOmfEndpointConfiguration,
            _dataProtector,
            _mockApplicationManifest.Object,
            new TestLogger(),
            compressor,
            MessageFormat,
            _debugLogsLocation);

        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = await compressor.CompressAsync(Encoding.UTF8.GetBytes(TestMessage));

        var response = await _omfByteHttpClient.SendMessageAsync(messageType, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);
        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(messageType).ToString() },
            { EndpointManagerConstants.MessageCompressionHeaderKey, compressor.Compression },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.ActionString, messageType == MessageType.StaticData || messageType == MessageType.Container ? UpdateAction : CreateAction },
        };

        Assert.True(_testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders));
    }

    [Theory]
    [MemberData(nameof(GetValidConfigUpdates))]
    public async Task OmfWriter_UpdateConfigurationValid(EndpointConfigurationBase configuration)
    {
        _omfByteHttpClient.Dispose();

        var testLogger = new TestLogger();

        var compressor = new GZipCompressor();

        _omfByteHttpClient = new OmfByteHttpClient(
            _testOmfEndpointConfiguration,
            _dataProtector,
            _mockApplicationManifest.Object,
            testLogger,
            compressor,
            MessageFormat,
            _debugLogsLocation);

        _omfByteHttpClient.UpdateConfiguration(configuration);

        _testEndpoint.StartListening();

        var testMessageBytes = compressor.Compress(Encoding.UTF8.GetBytes(TestMessage));

        testLogger.ClearLog();

        var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);
        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString(configuration.UserName, _dataProtector.Unprotect(configuration.Password)) },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, MessageType.Data.ToString() },
        };

        Assert.True(_testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders));
        Assert.False(testLogger.AreErrorsWarningsInLog());
    }

    [Theory]
    [InlineData(MessageType.Type)]
    [InlineData(MessageType.Container)]
    [InlineData(MessageType.Data)]
    [InlineData(MessageType.DynamicData)]
    [InlineData(MessageType.StaticData)]
    public async Task OmfWriter_HttpTrafficDebug_RuntimeChanges(MessageType messageType)
    {
        try
        {
            _omfByteHttpClient.Dispose();

            var testLogger = new TestLogger();
            var compressor = new GZipCompressor();
            var endpointConfiguration = _testOmfEndpointConfiguration;
            endpointConfiguration.DebugExpiration = DateTime.Now.AddMinutes(1);

            _omfByteHttpClient = new OmfByteHttpClient(
                endpointConfiguration,
                _dataProtector,
                _mockApplicationManifest.Object,
                testLogger,
                compressor,
                MessageFormat,
                _debugLogsLocation);

            _testEndpoint.StartListening();

            var testMessageBytes = compressor.Compress(Encoding.UTF8.GetBytes(TestMessage));
            var response = await _omfByteHttpClient.SendMessageAsync(messageType, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

            Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

            var omfMessageType = OmfByteHttpClient.GetOmfMessageType(messageType);

            var traceDirectoryPath = Path.Combine(_debugLogsLocation, omfMessageType.ToString());

            Assert.True(Directory.Exists(traceDirectoryPath));
            Assert.Equal(2, Directory.GetFiles(traceDirectoryPath).Length);

            // Disable debug logs and check that we no longer log anything
            endpointConfiguration.DebugExpiration = null;

            _omfByteHttpClient.UpdateConfiguration(endpointConfiguration);

            response = await _omfByteHttpClient.SendMessageAsync(messageType, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

            Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);
            Assert.Equal(2, Directory.GetFiles(traceDirectoryPath).Length);

            // Enable logging again to double check runtime change handling
            endpointConfiguration.DebugExpiration = DateTime.Now.AddMinutes(1);
            _omfByteHttpClient.UpdateConfiguration(endpointConfiguration);

            response = await _omfByteHttpClient.SendMessageAsync(messageType, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

            Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);
            Assert.Equal(4, Directory.GetFiles(traceDirectoryPath).Length);
        }
        finally
        {
            Directory.Delete(_debugLogsLocation, true);
        }
    }

    [Fact]
    public async Task OmfWriter_HttpTrafficDebug_Request_Response_Contents()
    {
        try
        {
            _omfByteHttpClient.Dispose();

            var testLogger = new TestLogger();
            var compressor = new GZipCompressor();
            var endpointConfiguration = _testOmfEndpointConfiguration;
            endpointConfiguration.DebugExpiration = DateTime.Now.AddMinutes(1);

            _omfByteHttpClient = new OmfByteHttpClient(
                endpointConfiguration,
                _dataProtector,
                _mockApplicationManifest.Object,
                testLogger,
                compressor,
                MessageFormat,
                _debugLogsLocation);

            _testEndpoint.StartListening();

            var testMessageBytes = compressor.Compress(Encoding.UTF8.GetBytes(TestMessage));
            var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

            var expectedHeaders = new Dictionary<string, string>
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.MessageTypeHeaderKey, ToExpectedMessageType(MessageType.Data).ToString() },
                { EndpointManagerConstants.MessageFormatString, "json" },
                { EndpointManagerConstants.ActionString, "create" },
                { EndpointManagerConstants.OmfVersionString, EndpointManagerConstants.OmfVersionNumberString },
                { EndpointManagerConstants.MessageCompressionHeaderKey, compressor.Compression },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.AcceptVerbosityKey, EndpointManagerConstants.AcceptVerbosityValue },
            };

            Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);
            Assert.True(_testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders));

            var traceDirectoryPath = Path.Combine(_debugLogsLocation, MessageType.Data.ToString());

            Assert.True(Directory.Exists(traceDirectoryPath));

            var debugFiles = Directory.GetFiles(traceDirectoryPath);

            Assert.Equal(2, debugFiles.Length);

            expectedHeaders.Remove(EndpointManagerConstants.AuthorizationHeaderName);

            var expectedRequestContents = $"{string.Join("; ", expectedHeaders.Select(x => x.Key + "=" + x.Value))}{Environment.NewLine}{Environment.NewLine}{TestMessage}";

            for (var i = 0; i < debugFiles.Length; i++)
            {
                if (debugFiles[i].Contains("Request"))
                {
                    Assert.Equal(expectedRequestContents, await File.ReadAllTextAsync(debugFiles[i]));
                }
                else
                {
                    Assert.Contains(HttpStatusCode.OK.ToString(), await File.ReadAllTextAsync(debugFiles[i]));
                }
            }
        }
        finally
        {
            Directory.Delete(_debugLogsLocation, true);
        }
    }

    [Fact]
    public async Task OmfWriter_HttpTrafficDebug_DebugExpiration_Expires()
    {
        try
        {
            _omfByteHttpClient.Dispose();

            var testLogger = new TestLogger();
            var compressor = new GZipCompressor();
            var endpointConfiguration = _testOmfEndpointConfiguration;
            endpointConfiguration.DebugExpiration = DateTime.Now.AddSeconds(1.5);

            _omfByteHttpClient = new OmfByteHttpClient(
                endpointConfiguration,
                _dataProtector,
                _mockApplicationManifest.Object,
                testLogger,
                compressor,
                MessageFormat,
                _debugLogsLocation);

            _testEndpoint.StartListening();

            var testMessageBytes = compressor.Compress(Encoding.UTF8.GetBytes(TestMessage));
            var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

            Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

            var traceDirectoryPath = Path.Combine(_debugLogsLocation, MessageType.Data.ToString());

            Assert.True(Directory.Exists(traceDirectoryPath));
            Assert.Equal(2, Directory.GetFiles(traceDirectoryPath).Length);

            await Task.Delay(1_600);

            // send another message and check that no new debug files are created
            response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

            Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);
            Assert.Equal(2, Directory.GetFiles(traceDirectoryPath).Length);
        }
        finally
        {
            Directory.Delete(_debugLogsLocation, true);
        }
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
            _omfByteHttpClient?.Dispose();
        }

        _disposed = true;
    }

    private static MessageType ToExpectedMessageType(MessageType messageType)
    {
        if (messageType == MessageType.DynamicData || messageType == MessageType.StaticData)
        {
            return MessageType.Data;
        }

        return messageType;
    }

    private static string GetBasicAuthString(string username = UserName, string password = Password)
    {
        return EndpointManagerConstants.BasicAuthorizationType + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
    }

    private static bool WasEndpointCertificateValidationWarningPrinted(TestLogger logger)
    {
        var warningPrinted = false;

        foreach (var logMessageEntry in logger.GetLogMessages())
        {
            if (logMessageEntry.LogLevel == LogLevel.Warning &&
                logMessageEntry.LogMessage.Contains($"Endpoint certificate validation is disabled for '{TestUri}' endpoint.", StringComparison.InvariantCulture))
            {
                warningPrinted = true;
            }
        }

        return warningPrinted;
    }
}
