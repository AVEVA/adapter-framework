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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Compression;
using AdapterFramework.Data.Framework.EndpointManager;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests;

public class HttpClientWrapper_Tests : IDisposable
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
    private OmfByteHttpClient _omfByteHttpClient;
    private bool _disposed;

    public HttpClientWrapper_Tests()
    {
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
            new GZipCompressor(),
            string.Empty,
            MessageFormat,
            _edgeEventProvider);

        _testEndpoint = new TestEndpoint(TestUri);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpClientWrapper_ValidateEndpointCertificate(bool validateCertificate)
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
        _omfByteHttpClient = new OmfByteHttpClient(testOmfEndpointConfiguration, _dataProtector, _mockApplicationManifest.Object, testLogger, null, string.Empty, MessageFormat);

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
    public async Task HttpClientWrapper_AuthenticationHeaders(string username, string password)
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        _omfByteHttpClient.Dispose();

        var testOmfEndpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = TestUri,
            UserName = username,
            Password = password,
        };

        _omfByteHttpClient = new OmfByteHttpClient(testOmfEndpointConfiguration, _dataProtector, _mockApplicationManifest.Object, new TestLogger(), null, string.Empty, MessageFormat);

        var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
        };

        Assert.True(!string.IsNullOrEmpty(username)
            ? _testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders)
            : _testEndpoint.VerifyMessageReceived(TestMessage));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(UserName, Password)]
    public async Task HttpClientWrapper_AcceptVerbosityHeader(string username, string password)
    {
        _testEndpoint.SetHttpResponse(HttpStatusCode.OK);
        _testEndpoint.StartListening();

        var testMessageBytes = Encoding.UTF8.GetBytes(TestMessage);

        _omfByteHttpClient.Dispose();

        var testOmfEndpointConfiguration = new EndpointConfigurationBase
        {
            Endpoint = TestUri,
            UserName = username,
            Password = password,
        };

        _omfByteHttpClient = new OmfByteHttpClient(testOmfEndpointConfiguration, _dataProtector, _mockApplicationManifest.Object, new TestLogger(), null, string.Empty, MessageFormat);

        var response = await _omfByteHttpClient.SendMessageAsync(MessageType.Data, testMessageBytes, MessageAction.Default, OmfVersion.Omf12, CancellationToken.None);

        Assert.Equal(ResponseStatusEnum.Success, response.ResponseStatus);

        var expectedHeaders = new Dictionary<string, string>
        {
            { EndpointManagerConstants.AcceptVerbosityKey, EndpointManagerConstants.AcceptVerbosityValue },
        };

        Assert.True(!string.IsNullOrEmpty(username)
            ? _testEndpoint.VerifyMessageReceived(TestMessage, expectedHeaders)
            : _testEndpoint.VerifyMessageReceived(TestMessage));
    }

    [Fact]
    public async Task HttpClientWrapper_ProtectedPasswordToken_ClientUnprotectsPasswordToken()
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

        _omfByteHttpClient = new OmfByteHttpClient(testOmfEndpointConfiguration, _dataProtector, _mockApplicationManifest.Object, new TestLogger(), null, string.Empty, MessageFormat);

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
