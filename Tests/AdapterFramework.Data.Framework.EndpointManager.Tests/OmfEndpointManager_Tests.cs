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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Compression;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

public class OmfEndpointManager_Tests : IDisposable
{
    private const int BufferedMessageWaitTime = 10_000;
    private const int MaximumWaitTime = 60_000;
    private const string TestComponentId = "TestComponentId";
    private const string TestFacetId = "TestFacetId";
    private const string UserName = "test";
    private const string Password = "test";
    private const string CommonApplicationDataDir = "UnitTests";
    private const string DefaultFacetName = "DataEndpoints";
    private const string EndpointUpdatedMessage = "OMF Data Endpoint configuration has been updated";

    private readonly TestEndpoint _testEndpoint;
    private readonly string _testUri;
    private readonly JsonConfigurationProvider _configurationProvider = new(CommonApplicationDataDir);
    private readonly string _defaultBufferLocation;
    private readonly IList<HttpRequestExecutionInfo> _httpRequestExecutionInfos;
    private readonly TestEdgeEventProvider _edgeEventProvider;

    private CancellationTokenSource _cancellationTokenSource;
    private DeviceStatus _deviceStatus;
    private bool _disposed;

    public OmfEndpointManager_Tests()
    {
        // Initialize test endpoint with dynamic port for test isolation
        var port = GetAvailablePort();
        _testUri = $"http://localhost:{port}/api/omf/";
        _testEndpoint = new TestEndpoint(_testUri);
        
        _edgeEventProvider = new TestEdgeEventProvider();
        
        // Use unique buffer location per test instance to avoid conflicts
        var uniqueId = Guid.NewGuid().ToString("N");
        _defaultBufferLocation = Path.Combine(
            Path.GetTempPath(),
            "UnitTests",
            uniqueId,
            EdgeSystemConstants.BuffersDirectoryName);

        _cancellationTokenSource = new CancellationTokenSource();
        _httpRequestExecutionInfos = new List<HttpRequestExecutionInfo>();
    }

    #region Test Data

    public static IEnumerable<object[]> OneNullConfig =>
        new List<object[]>
        {
            new object[] { null, new EndpointConfigurationBase() },
            new object[] { new EndpointConfigurationBase(), null },
        };

    public static IEnumerable<object[]> EqualConfigs =>
        new List<object[]>
        {
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", Password = "AdapterFramework", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", Password = "AdapterFramework" } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientSecret = "12345", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientSecret = "12345", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientId = "12345", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientId = "12345", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", UserName = "PersonA", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", UserName = "PersonA", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", TokenEndpoint = "http://valid.com", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", TokenEndpoint = "http://valid.com", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ValidateEndpointCertificate = true, }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ValidateEndpointCertificate = true, } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", } },
        };

    public static IEnumerable<object[]> DifferentConfigs =>
        new List<object[]>
        {
            new object[] { new EndpointConfigurationBase() { UserName = "PersonA", }, new EndpointConfigurationBase() { UserName = "PersonB", } },
            new object[] { new EndpointConfigurationBase() { Password = "AdapterFramework", }, new EndpointConfigurationBase() { Password = "test", } },
            new object[] { new EndpointConfigurationBase() { Endpoint = "12345", }, new EndpointConfigurationBase() { Endpoint = "23456", } },
            new object[] { new EndpointConfigurationBase() { ClientId = "12345", }, new EndpointConfigurationBase() { ClientId = "23456", } },
            new object[] { new EndpointConfigurationBase() { ClientSecret = "AdapterFramework", }, new EndpointConfigurationBase() { ClientSecret = "adapterframework", } },
            new object[] { new EndpointConfigurationBase() { TokenEndpoint = "http://valid.com", }, new EndpointConfigurationBase() { TokenEndpoint = "http://invalid.com", } },
            new object[] { new EndpointConfigurationBase() { ValidateEndpointCertificate = true, }, new EndpointConfigurationBase() { ValidateEndpointCertificate = false, } },
        };

    public static IEnumerable<object[]> UpdatedConfigs =>
        new List<object[]>
        {
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", UserName = "PersonA", }, new EndpointConfigurationBase() { Endpoint = "http://valid.com", Id = "1", UserName = "PersonB", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", Password = "AdapterFramework", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", Password = "test", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid2.com", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientId = "12345", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientId = "23456", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientSecret = "AdapterFramework", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ClientSecret = "adapterframework", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", TokenEndpoint = "http://valid.com", }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", TokenEndpoint = "http://invalid.com", } },
            new object[] { new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ValidateEndpointCertificate = true, }, new EndpointConfigurationBase() { Id = "1", Endpoint = "http://valid.com", ValidateEndpointCertificate = false, } },
        };

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void OmfEndpointManager_Ctor()
    {
        // Just what is needed to send data to an endpoint
        // A configuration utilizing all of the options
        IEnumerable<EndpointConfigurationBase> configList = new List<EndpointConfigurationBase>
        {
            new EndpointConfigurationBase()
            {
                Id = "Hello",
                Endpoint = "https://localhost:5465/api/omf",
                UserName = UserName,
                Password = Password,
            },
            new EndpointConfigurationBase()
            {
                Id = "Cow",
                Endpoint = "https://localhost:5465/api2/omf",
                UserName = UserName,
                Password = Password,
            },
        };

        using var omfEndpointManager = CreateEndpointManager(configList.ToArray(), out _, out var testLogger);
        Assert.False(testLogger.AreErrorsWarningsInLog());
    }

    [Theory]
    [ClassData(typeof(TestDataGenerator))]
    public void OmfEndpointManager_SendMessage_NoBufferingOrCompression(string message, MessageType messageType)
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfig = new BufferingConfiguration
        {
            EnablePersistentBuffering = false,
            BufferLocation = _defaultBufferLocation,
        };

        ICollection<string> errors = new List<string>();
        var configList = new[] { config };

        using var omfEndpointManager = CreateEndpointManager(configList, out var configurationProvider, out var testLogger);
        configurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out bufferingConfig, out errors)).Returns(true);

        omfEndpointManager.Initialize("UnitTests", "TestFacet");
        omfEndpointManager.SetDeviceStatusHandler((x) => _deviceStatus = x);

        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(message), MessageAction.Default);

        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>()
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
            };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders), BufferedMessageWaitTime));
        Assert.True(testLogger.IsOnlyHttpUsageAndCryptoExInLog());
        Assert.Equal(DeviceStatus.Good, _deviceStatus);
        Assert.True(testLogger.ContainsMessage(_testUri));
        Assert.True(testLogger.ContainsMessage(bufferingConfig.EnablePersistentBuffering.ToString(CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OmfEndpointManager_ProtectedPassword_PasswordUnprotected(bool isBufferingEnabled)
    {
        _testEndpoint.StartListening();

        var mockDataProtector = new Mock<IEdgeDataProtector>();
        mockDataProtector.Setup(x => x.Protect(It.IsAny<string>())).Returns("123");
        mockDataProtector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns(Password);

        var protectedPassword = mockDataProtector.Object.Protect(Password);
        Assert.NotEqual(Password, protectedPassword);

        var config = new EndpointConfigurationBase
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = protectedPassword,
        };

        var bufferingConfig = new BufferingConfiguration
        {
            EnablePersistentBuffering = isBufferingEnabled,
            BufferLocation = _defaultBufferLocation,
        };

        ICollection<string> errors = new List<string>();
        var configList = new[] { config };

        using var omfEndpointManager = CreateEndpointManager(configList, out var mockConfigurationProvider, out _);
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out bufferingConfig, out errors)).Returns(true);

        omfEndpointManager.Initialize(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.HealthEndpointsFacetName);

        var omfDataMessage = TestOmfGenerator.GetDataMessage();
        var msg = new SerializedOmfMessage(MessageType.Data, Encoding.UTF8.GetBytes(omfDataMessage), MessageAction.Default);

        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>()
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.MessageTypeHeaderKey, MessageType.Data.ToString() },
            };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(omfDataMessage, expectedHeaders), BufferedMessageWaitTime));
        Assert.Equal(DeviceStatus.Good, _deviceStatus);
    }

    [Theory]
    [ClassData(typeof(TestDataGenerator))]
    public void OmfEndpointManager_SendMessage_BufferingEnabled(string message, MessageType messageType)
    {
        var config = new EndpointConfigurationBase
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfig = new BufferingConfiguration
        {
            EnablePersistentBuffering = true,
            BufferLocation = _defaultBufferLocation,
        };

        ICollection<string> errors = new List<string>();
        var configList = new[] { config };

        using var omfEndpointManager = CreateEndpointManager(configList, out var mockConfigurationProvider, out var testLogger);
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(),
            out bufferingConfig, out errors)).Returns(true);

        omfEndpointManager.Initialize(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.HealthEndpointsFacetName);

        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(message), MessageAction.Default);

        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
            };

        Assert.False(_testEndpoint.VerifyMessageReceived(message, expectedHeaders));

        // Ensure endpoint is offline long enough to timeout at least the first message post
        // A message should be logged regarding failure to send while endpoint is offline
        Assert.True(SpinWait.SpinUntil(() => testLogger.AreErrorsWarningsInLog(), BufferedMessageWaitTime),
            "Connection error wasn't logged.");
        Assert.True(SpinWait.SpinUntil(() => _deviceStatus == DeviceStatus.DeviceInError, BufferedMessageWaitTime));

        // Enable the endpoint to ensure the buffered messages are received when it comes back online
        _testEndpoint.StartListening();

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders),
                BufferedMessageWaitTime), "Message wasn't received.");
        Assert.True(SpinWait.SpinUntil(() => _deviceStatus == DeviceStatus.Good, BufferedMessageWaitTime));
    }

    [Theory]
    [ClassData(typeof(TestDataGenerator))]
    public void OmfEndpointManager_SendMessage_CompressionEnabled(string message, MessageType messageType)
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfig = new BufferingConfiguration
        {
            EnablePersistentBuffering = false,
            BufferLocation = _defaultBufferLocation,
        };

        ICollection<string> errors = new List<string>();

        var configList = new[] { config };

        var compressor = new GZipCompressor();
        using var omfEndpointManager = CreateEndpointManager(configList, out var mockConfigurationProvider, out var testLogger, compressor);
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out bufferingConfig, out errors)).Returns(true);

        omfEndpointManager.Initialize(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.HealthEndpointsFacetName);

        var msg = new SerializedOmfMessage(messageType, compressor.Compress(Encoding.UTF8.GetBytes(message)), MessageAction.Default);

        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
                { EndpointManagerConstants.MessageCompressionHeaderKey, compressor.Compression },
            };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders), BufferedMessageWaitTime));
        Assert.True(testLogger.IsOnlyHttpUsageAndCryptoExInLog());
        Assert.Equal(DeviceStatus.Good, _deviceStatus);
    }

    [Fact]
    public void OmfEndpointManager_ConfigUpdate_RemovesWriters()
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfig = new BufferingConfiguration
        {
            EnablePersistentBuffering = false,
            BufferLocation = _defaultBufferLocation,
        };

        var configList = new[] { config };
        ICollection<string> errors = new List<string>();

        using var omfEndpointManager = CreateEndpointManager(configList, out var mockConfigurationProvider, out _);

        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(
            It.IsAny<string>(), It.IsAny<string>(), out bufferingConfig, out errors))
            .Returns(true);

        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        var oldConfigs = configList;
        var newConfigs = Array.Empty<EndpointConfigurationBase>();
        var args = new ConfigurationChangedEventArgs(oldConfigs, newConfigs);
        omfEndpointManager.AddRemoveEndpoints(args, OmfWriterType.Health);

        var bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var field = typeof(OmfEndpointManager).GetField("_createdWriters", bindFlags);
        var dict = field?.GetValue(omfEndpointManager) as Dictionary<string, IOmfWriter>;
        Assert.True(dict?.Count == 0);
        Assert.Equal(DeviceStatus.Good, _deviceStatus);
    }

    [Fact]
    public void OmfEndpointManager_GetAndResetDataCounter_GetsCountersNoData()
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var config2 = new EndpointConfigurationBase()
        {
            Id = "Hello2",
            Endpoint = "http://localhost:5466/api/omf/",
            UserName = UserName,
            Password = Password,
        };

        var configList = new[] { config, config2 };

        using var omfEndpointManager = CreateEndpointManager(configList, out _, out _);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        SpinWait.SpinUntil(() => configList.Length == omfEndpointManager.GetAndResetEgressedValuesCounters().Count, MaximumWaitTime);

        var counters = omfEndpointManager.GetAndResetEgressedValuesCounters();
        Assert.True(counters[config.Id] == 0);
        Assert.True(counters[config2.Id] == 0);
    }

    [Fact]
    public void OmfEndpointManager_GetAndResetDataCounter_GetsCountersDataCorrectly()
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var config2 = new EndpointConfigurationBase()
        {
            Id = "Hello2",
            Endpoint = "http://localhost:5466/api/omf/",
            UserName = UserName,
            Password = Password,
        };

        var configList = new[] { config, config2 };

        using var omfEndpointManager = CreateEndpointManager(configList, out _, out _);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        var message = TestOmfGenerator.GetDataMessage();
        var msg = new SerializedOmfMessage(MessageType.Data, Encoding.UTF8.GetBytes(message), MessageAction.Default, 1);
        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.MessageTypeHeaderKey, MessageType.Data.ToString() },
            };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders),
                BufferedMessageWaitTime), "Message wasn't received.");

        var counters = omfEndpointManager.GetAndResetEgressedValuesCounters();
        Assert.Equal(configList.Length, counters.Count);
        Assert.Equal(1, counters[config.Id]);
        Assert.True(counters[config2.Id] == 0);

        counters = omfEndpointManager.GetAndResetEgressedValuesCounters();
        Assert.Equal(configList.Length, counters.Count);
        Assert.True(counters[config.Id] == 0);
        Assert.True(counters[config2.Id] == 0);
    }

    [Fact]
    public void OmfEndpointManager_UpdateRequired_BothNull()
    {
        using var omfEndpointManager = CreateEndpointManager(Array.Empty<EndpointConfigurationBase>(), out _, out _);

        Assert.False(omfEndpointManager.RequiresUpdate(null, null));
    }

    [Theory]
    [MemberData(nameof(EqualConfigs))]
    public void OmfEndpointManager_UpdateRequired_AreEqual(EndpointConfigurationBase config1, EndpointConfigurationBase config2)
    {
        using var omfEndpointManager = CreateEndpointManager(Array.Empty<EndpointConfigurationBase>(), out _, out _);

        Assert.False(omfEndpointManager.RequiresUpdate(config1, config2));
    }

    [Theory]
    [MemberData(nameof(DifferentConfigs))]
    public void OmfEndpointManager_UpdateRequired_AreDifferent(EndpointConfigurationBase config1, EndpointConfigurationBase config2)
    {
        var configs = new[] { config1, config2 };

        var configProtector = TestUtilities.CreateConfigurationProtector(new TestLogger());
        configProtector.ProtectSecrets(ref configs[0], TestComponentId, TestFacetId);
        configProtector.ReconcileSecretsChange(ref configs[0], true);
        configProtector.ProtectSecrets(ref configs[1], TestComponentId, TestFacetId);

        configProtector.ReplaceSecretIdsWithEncryptedSecrets(ref config1);
        configProtector.ReconcileSecretsChange(ref configs[1], true);

        using var omfEndpointManager = CreateEndpointManager(Array.Empty<EndpointConfigurationBase>(), out _, out _, configurationProtector: configProtector);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        Assert.True(omfEndpointManager.RequiresUpdate(config1, config2));
    }

    [Theory]
    [MemberData(nameof(UpdatedConfigs))]
    public void OmfEndpointManager_EndpointUpdated_MetadataResend(EndpointConfigurationBase config1, EndpointConfigurationBase config2)
    {
        var configs = new[] { config1, config2 };

        var configProtector = TestUtilities.CreateConfigurationProtector(new TestLogger());
        configProtector.ProtectSecrets(ref configs[0], TestComponentId, TestFacetId);
        configProtector.ReconcileSecretsChange(ref configs[0], true);
        configProtector.ProtectSecrets(ref configs[1], TestComponentId, TestFacetId);

        var configOld = new[] { config1 };
        configProtector.ReplaceSecretIdsWithEncryptedSecrets(ref configOld);
        configProtector.ReconcileSecretsChange(ref configs[1], true);

        using var omfEndpointManager = CreateEndpointManager(configs, out _, out var testLogger, configurationProtector: configProtector);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        var configurationUpdate = new ConfigurationChangedEventArgs(configOld, new[] { config2 });
        var metadataResend = omfEndpointManager.AddRemoveEndpoints(configurationUpdate, OmfWriterType.Data);

        if (config1.Endpoint != config2.Endpoint)
        {
            Assert.True(metadataResend);
        }
        else
        {
            Assert.False(metadataResend);
        }

        Assert.Contains(testLogger.GetLogMessages(), m => m.LogMessage.StartsWith(EndpointUpdatedMessage, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(EqualConfigs))]
    public void OmfEndpointManager_NotUpdatedConfig_NoChange(EndpointConfigurationBase config1, EndpointConfigurationBase config2)
    {
        var configs = new[] { config1, config2 };

        var configProtector = TestUtilities.CreateConfigurationProtector(new TestLogger());
        configProtector.ProtectSecrets(ref configs, TestComponentId, TestFacetId);

        using var omfEndpointManager = CreateEndpointManager(configs, out _, out var testLogger);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        var configurationUpdate = new ConfigurationChangedEventArgs(new[] { config1 }, new[] { config2 });
        var metadataResend = omfEndpointManager.AddRemoveEndpoints(configurationUpdate, OmfWriterType.Data);

        Assert.False(metadataResend);
        Assert.DoesNotContain(testLogger.GetLogMessages(), m => m.LogMessage.StartsWith(EndpointUpdatedMessage, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OmfEndpointManager_OneBadEndpoint_StatusDeviceInError()
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var config2 = new EndpointConfigurationBase()
        {
            Id = "Bad",
            Endpoint = "http://localhost:9999",
            UserName = UserName,
            Password = Password,
        };

        var configList = new[] { config, config2 };

        using var omfEndpointManager = CreateEndpointManager(configList, out _, out _);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        var messageType = MessageType.Type;
        var message = TestOmfGenerator.GetTypeMessage();
        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(message), MessageAction.Default);

        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>()
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
            };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders), BufferedMessageWaitTime));
        Assert.True(SpinWait.SpinUntil(() => _deviceStatus == DeviceStatus.DeviceInError, BufferedMessageWaitTime));
    }

    [Fact]
    public void OmfEndpointManager_OneBadEndpoint_RemoveBadEndpointStatusGood()
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var config2 = new EndpointConfigurationBase()
        {
            Id = "Bad",
            Endpoint = "http://localhost:9999",
            UserName = UserName,
            Password = Password,
        };

        var configurations = new[] { config, config2, };

        using var omfEndpointManager = CreateEndpointManager(configurations, out _, out _);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        var messageType = MessageType.Type;
        var message = TestOmfGenerator.GetTypeMessage();
        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(message), MessageAction.Default);

        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>()
            {
                { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
                { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
                { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
            };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders), BufferedMessageWaitTime));
        Assert.True(SpinWait.SpinUntil(() => _deviceStatus == DeviceStatus.DeviceInError, BufferedMessageWaitTime));

        var args = new ConfigurationChangedEventArgs(configurations, new[] { config });
        omfEndpointManager.AddRemoveEndpoints(args, OmfWriterType.Data);

        Assert.True(SpinWait.SpinUntil(() => _deviceStatus == DeviceStatus.Good, BufferedMessageWaitTime));
    }

    [Theory]
    [InlineData("DataEndpoints", true)]
    [InlineData("HealthEndpoints", false)]
    public async Task OmfEndpointManager_EventListener_Registration_WriterType(string facetName, bool egressHandlerExecuted)
    {
        _testEndpoint.StartListening();

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello" + Guid.NewGuid(),
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var config2 = new EndpointConfigurationBase()
        {
            Id = "Bad",
            Endpoint = "http://localhost:9999",
            UserName = UserName,
            Password = Password,
        };

        var configurations = new[] { config, config2, };

        using var omfEndpointManager = CreateEndpointManager(configurations, out _, out _);
        omfEndpointManager.Initialize("UnitTest", facetName);
        _testEndpoint.ClearRequests();
        await Task.Delay(500);
        _httpRequestExecutionInfos.Clear();

        var messageType = MessageType.Type;
        var message = TestOmfGenerator.GetTypeMessage();
        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(message), MessageAction.Default);

        omfEndpointManager.SendMessage(msg);

        var expectedHeaders = new Dictionary<string, string>()
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
        };

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders), BufferedMessageWaitTime));
        Assert.True(SpinWait.SpinUntil(() => _deviceStatus == DeviceStatus.DeviceInError, BufferedMessageWaitTime));
        RemoveHttpRequestExecutedHandler();
        var args = new ConfigurationChangedEventArgs(configurations, new[] { config });
        omfEndpointManager.AddRemoveEndpoints(args, OmfWriterType.Data);
        Assert.Equal(egressHandlerExecuted, _httpRequestExecutionInfos.Any());

        if (egressHandlerExecuted)
        {
            var expectedIds = new HashSet<string>() { config.Id, config2.Id };

            Assert.Equal(2, _httpRequestExecutionInfos.Count);
            foreach (var requestExecutionInfo in _httpRequestExecutionInfos)
            {
                Assert.Contains(requestExecutionInfo.EndpointId, expectedIds);
                if (requestExecutionInfo.EndpointId.Equals(config.Id))
                {
                    Assert.Equal(HttpStatusCode.OK, requestExecutionInfo.HttpStatusCode);
                    Assert.Null(requestExecutionInfo.Exception);
                }
                else
                {
                    Assert.Null(requestExecutionInfo.HttpStatusCode);
                    Assert.NotNull(requestExecutionInfo.Exception);
                }

                expectedIds.Remove(requestExecutionInfo.EndpointId);
            }
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void OmfEndpointManager_EventListener_HttpStatusCodes(HttpStatusCode statusCode)
    {
        _testEndpoint.StartListening();
        var messageType = MessageType.Type;

        var expectedHeaders = new Dictionary<string, string>()
        {
            { EndpointManagerConstants.AuthorizationHeaderName, GetBasicAuthString() },
            { EndpointManagerConstants.CsrfKey, EndpointManagerConstants.AdapterCsrfValue },
            { EndpointManagerConstants.MessageTypeHeaderKey, messageType.ToString() },
        };

        _testEndpoint.SetHttpResponse(statusCode, expectedHeaders);

        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var configurations = new[] { config, };

        using var omfEndpointManager = CreateEndpointManager(configurations, out _, out _);
        omfEndpointManager.Initialize("UnitTest", DefaultFacetName);

        var message = TestOmfGenerator.GetTypeMessage();
        var msg = new SerializedOmfMessage(messageType, Encoding.UTF8.GetBytes(message), MessageAction.Default);

        _httpRequestExecutionInfos.Clear();
        _testEndpoint.ClearRequests();

        omfEndpointManager.SendMessage(msg);

        Assert.True(SpinWait.SpinUntil(() => _testEndpoint.VerifyMessageReceived(message, expectedHeaders), BufferedMessageWaitTime));
        Assert.True(SpinWait.SpinUntil(() => _httpRequestExecutionInfos.Count > 0, BufferedMessageWaitTime));
        Assert.Equal(config.Id, _httpRequestExecutionInfos[0].EndpointId);
        Assert.Equal(statusCode, _httpRequestExecutionInfos[0].HttpStatusCode);
        Assert.Null(_httpRequestExecutionInfos[0].Exception);
    }

    [Fact]
    public async Task OmfEndpointManager_ResetDataBuffers_StartedAndCompleted()
    {
        var config = new EndpointConfigurationBase()
        {
            Id = "Hello",
            Endpoint = _testUri,
            UserName = UserName,
            Password = Password,
        };

        var configList = new[] { config };
        using var omfEndpointManager = CreateEndpointManager(configList, out _, out var testLogger);
        omfEndpointManager.Initialize("UnitTests", "TestFacet");
        await omfEndpointManager.ResetDataBuffersAsync();
        var logMessages = testLogger.GetLogMessages();
        Assert.Contains(logMessages, x => x.LogMessage.Contains("Started", StringComparison.Ordinal));
        Assert.Contains(logMessages, x => x.LogMessage.Contains("Completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OmfEndpointManager_ResetDataBuffers_NoOmfWriters()
    {
        EndpointConfigurationBase[] configList = Array.Empty<EndpointConfigurationBase>();
        using var omfEndpointManager = CreateEndpointManager(configList, out _, out var testLogger);
        omfEndpointManager.Initialize("UnitTests", "TestFacet");
        await omfEndpointManager.ResetDataBuffersAsync();
        var logMessages = testLogger.GetLogMessages();
        Assert.Contains(logMessages, x => x.LogMessage.Contains("Aborted", StringComparison.Ordinal));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _testEndpoint?.Dispose();

            try
            {
                var pathToCleanup = _configurationProvider.GetCommonApplicationDataDirectoryPath();
                if (Directory.Exists(pathToCleanup))
                {
                    Directory.Delete(pathToCleanup, true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up {nameof(OmfEndpointManager_Tests)}: {ex}");
            }
        }

        _disposed = true;
    }

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    private static string GetBasicAuthString(string username = UserName, string password = Password)
    {
        return EndpointManagerConstants.BasicAuthorizationType + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
    }

    private void RemoveHttpRequestExecutedHandler()
    {
        _cancellationTokenSource.Cancel();
    }

    private OmfEndpointManager CreateEndpointManager(EndpointConfigurationBase[] endpoints, out Mock<IConfigurationProvider> mockConfigurationProvider, out TestLogger logger, ICompressor compressor = null, IConfigurationProtector configurationProtector = null)
    {
        _ = ProcessEventsAsync(_cancellationTokenSource.Token);

        ICollection<string> errors = new List<string>();

        mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(
                It.IsAny<string>(), It.IsAny<string>(), out endpoints, out errors))
            .Returns(true);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AdapterFramework", "Health"));

        var mockDataProtector = new Mock<IEdgeDataProtector>();
        mockDataProtector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns(Password);

        var factory = new OmfWriterFactory(
            mockDataProtector.Object,
            new OmfJsonSerializer(),
            mockConfigurationProvider.Object,
            new ApplicationManifest(),
            compressor,
            _edgeEventProvider);

        logger = new TestLogger();
        var mockLogManager = new Mock<ILogManager>();
        mockLogManager.Setup(manager => manager.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>()))
            .Returns(logger);

        configurationProtector ??= TestUtilities.CreateConfigurationProtector(logger);

        var omfEndpointManager = new OmfEndpointManager(
            mockConfigurationProvider.Object,
            factory,
            mockLogManager.Object,
            configurationProtector);

        omfEndpointManager.SetDeviceStatusHandler((x) => _deviceStatus = x);
        return omfEndpointManager;
    }

    private async Task ProcessEventsAsync(CancellationToken token)
    {
        var reader = _edgeEventProvider.EdgeEventChannel.Reader;
        while (await reader.WaitToReadAsync(token))
        {
            while (reader.TryRead(out var evt))
            {
                switch (evt)
                {
                    case HttpRequestExecutionInfo executionInfo:
                        _httpRequestExecutionInfos.Add(executionInfo);
                        break;
                    default:
                        throw new InvalidOperationException($"Unexpected event type: {evt.GetType()}");
                }
            }
        }
    }

    internal class TestDataGenerator : IEnumerable<object[]>
    {
        private readonly IEnumerable<object[]> _data = new List<object[]>
        {
            new object[] { TestOmfGenerator.GetContainerMessage(), MessageType.Container },
            new object[] { TestOmfGenerator.GetTypeMessage(), MessageType.Type },
            new object[] { TestOmfGenerator.GetDataMessage(), MessageType.Data },
        };

        public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
