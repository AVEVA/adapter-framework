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
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests;

public class ComponentConfigurationProvider_Tests
{
    public const string UnitTestComponentId = "UnitTest";

    private const string ErrorMessage = "Something bad happened.";

    private delegate void TryGetConfigurationCallback(string id, string facet, out TestConfiguration config, out ICollection<string> errors);
    private delegate void TryGetConfigurationArrayCallback(string id, string facet, out TestConfiguration[] configs, out ICollection<string> errors);

    private delegate void TrySaveConfigurationCallback(string id, string facet, TestConfiguration config, out ICollection<string> errors);
    private delegate void TrySaveConfigurationArrayCallback(string id, string facet, TestConfiguration[] configs, out ICollection<string> errors);
    private delegate void TryMoveCorruptedConfigurationCallback(string componentId, string configurationName, out string errorMessage);

    [Theory]
    [ClassData(typeof(TestDataGenerator))]
    public void EdgeComponentConfigurationProvider_Constructor_InvalidArgs(IConfigurationProvider configurationProvider, string componentId)
    {
        var exceptionThrown = false;
        IComponentConfigurationProvider componentConfigurationProvider = null;
        var mockLogger = new Mock<ILogger>();

        try
        {
            componentConfigurationProvider = new ComponentConfigurationProvider(configurationProvider, componentId, mockLogger.Object);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.Null(componentConfigurationProvider);
        Assert.True(exceptionThrown);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_Constructor_InvalidLogger()
    {
        var exceptionThrown = false;
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        IComponentConfigurationProvider componentConfigurationProvider = null;

        try
        {
            componentConfigurationProvider = new ComponentConfigurationProvider(mockConfigurationProvider.Object, UnitTestComponentId, null);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.Null(componentConfigurationProvider);
        Assert.True(exceptionThrown);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_GetConfiguration()
    {
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors = null;
        TestConfiguration outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out outErrors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, out TestConfiguration config, out ICollection<string> errors) =>
            {
                config = new TestConfiguration();
                errors = new List<string>();
            })).Returns(true);

        var mockLogger = new Mock<ILogger>();
        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        var result = edgeComponentConfigurationProvider.GetConfiguration<TestConfiguration>(TestConfiguration.ConfigName);
        Assert.NotNull(result);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_GetArrayConfiguration()
    {
        var retConfig = new TestConfiguration[1];
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors = null;
        TestConfiguration[] outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out outErrors))
            .Callback(new TryGetConfigurationArrayCallback((string id, string facet, out TestConfiguration[] configs, out ICollection<string> errors) =>
            {
                configs = retConfig;
                errors = new List<string>();
            })).Returns(true);

        var mockLogger = new Mock<ILogger>();
        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        var result = edgeComponentConfigurationProvider.GetArrayConfiguration<TestConfiguration>(TestConfiguration.ConfigName);
        Assert.NotNull(result);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_GetConfiguration_No_Log_Error_When_Config_Does_Not_Exist()
    {
        var errorMessage = string.Empty;
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        ICollection<string> outErrors = null;
        TestConfiguration outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out outErrors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, out TestConfiguration config, out ICollection<string> errors) =>
            {
                config = null;
                errors = new List<string>();
            })).Returns(false);

        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                errorMessage = message.ToString();
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        Assert.Null(edgeComponentConfigurationProvider.GetConfiguration<TestConfiguration>(TestConfiguration.ConfigName));
        Assert.Empty(errorMessage);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_GetConfigurationInvalidConfig()
    {
        var errorMessage = string.Empty;
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        ICollection<string> outErrors = null;
        TestConfiguration outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out outErrors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, out TestConfiguration config, out ICollection<string> errors) =>
            {
                config = null;
                errors = new List<string> { ErrorMessage };
            })).Returns(false);

        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                errorMessage = message.ToString();
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        Assert.Null(edgeComponentConfigurationProvider.GetConfiguration<TestConfiguration>(TestConfiguration.ConfigName));
        Assert.Contains(ErrorMessage, errorMessage);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_GetArrayConfigurationInvalidConfig_No_Log_Error_When_Config_Does_Not_Exist()
    {
        var errorMessage = string.Empty;
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        ICollection<string> outErrors = null;
        TestConfiguration[] outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out outErrors))
            .Callback(new TryGetConfigurationArrayCallback((string id, string facet, out TestConfiguration[] configs, out ICollection<string> errors) =>
            {
                configs = null;
                errors = new List<string>();
            }));

        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                errorMessage = message.ToString();
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        Assert.Null(edgeComponentConfigurationProvider.GetArrayConfiguration<TestConfiguration>(TestConfiguration.ConfigName));
        Assert.Empty(errorMessage);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_GetArrayConfigurationInvalidConfig()
    {
        var errorMessage = string.Empty;
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        ICollection<string> outErrors = null;
        TestConfiguration[] outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out outErrors))
            .Callback(new TryGetConfigurationArrayCallback((string id, string facet, out TestConfiguration[] configs, out ICollection<string> errors) =>
            {
                configs = null;
                errors = new List<string> { ErrorMessage };
            }));

        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                errorMessage = message.ToString();
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        Assert.Null(edgeComponentConfigurationProvider.GetArrayConfiguration<TestConfiguration>(TestConfiguration.ConfigName));
        Assert.Contains(ErrorMessage, errorMessage);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveConfiguration()
    {
        var configSaved = false;
        var configToSave = new TestConfiguration();

        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors = new List<string>();
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration>(), out outErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, TestConfiguration _, out ICollection<string> errors) =>
            {
                configSaved = true;
                errors = new List<string>();
            })).Returns(true);

        var mockLogger = new Mock<ILogger>();
        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        edgeComponentConfigurationProvider.SaveConfiguration(TestConfiguration.ConfigName, configToSave);

        Assert.True(configSaved);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveArrayConfiguration()
    {
        var configToSave = new TestConfiguration[1];
        var configSaved = false;

        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors = new List<string>();
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration[]>(), out outErrors))
            .Callback(new TrySaveConfigurationArrayCallback((string id, string facet, TestConfiguration[] _, out ICollection<string> errors) =>
            {
                configSaved = true;
                errors = new List<string>();
            })).Returns(true);

        var mockLogger = new Mock<ILogger>();
        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        edgeComponentConfigurationProvider.SaveArrayConfiguration(TestConfiguration.ConfigName, configToSave);
        Assert.True(configSaved);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveConfiguration_InvalidConfig()
    {
        var configSaved = true;
        var configToSave = new TestConfiguration();
        var errorMessage = string.Empty;

        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors = null;
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration>(), out outErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, TestConfiguration _, out ICollection<string> errors) =>
            {
                configSaved = false;
                errors = new List<string> { ErrorMessage };
            })).Returns(false);

        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                errorMessage = message.ToString();
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        edgeComponentConfigurationProvider.SaveConfiguration(TestConfiguration.ConfigName, configToSave);

        Assert.False(configSaved);
        Assert.Contains(ErrorMessage, errorMessage);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveConfiguration_InvalidOriginalConfig_MoveSucceeded()
    {
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        var getCalled = false;
        ICollection<string> getErrors = null;
        TestConfiguration outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out getErrors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, out TestConfiguration configs, out ICollection<string> errors) =>
            {
                getCalled = true;
                configs = null;
                errors = new List<string> { ErrorMessage };
            }));

        var saveCalled = false;
        ICollection<string> saveErrors = null;
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration>(), out saveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, TestConfiguration _, out ICollection<string> errors) =>
            {
                saveCalled = true;
                errors = new List<string>();
            })).Returns(true);

        var moveCalled = false;
        string moveErrors = null;
        mockProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out moveErrors))
            .Callback(new TryMoveCorruptedConfigurationCallback((string componentId, string configName, out string errors) =>
            {
                moveCalled = true;
                errors = null;
            })).Returns(true);

        var logMessages = new List<string>();
        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        var configToSave = new TestConfiguration();
        edgeComponentConfigurationProvider.SaveConfiguration(TestConfiguration.ConfigName, configToSave);

        Assert.True(getCalled);
        Assert.True(moveCalled);
        Assert.True(saveCalled);
        Assert.Single(logMessages);
        Assert.Contains(ErrorMessage, logMessages[0]);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveConfiguration_InvalidOriginalConfig_MoveFailed()
    {
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        var getCalled = false;
        ICollection<string> getErrors = null;
        TestConfiguration outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out getErrors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, out TestConfiguration configs, out ICollection<string> errors) =>
            {
                getCalled = true;
                configs = null;
                errors = new List<string> { ErrorMessage };
            }));

        var saveCalled = false;
        ICollection<string> saveErrors = null;
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration>(), out saveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, TestConfiguration _, out ICollection<string> errors) =>
            {
                saveCalled = true;
                errors = new List<string>();
            })).Returns(true);

        var moveFailedMessage = "Move failed";
        var moveCalled = false;
        string moveErrors = null;
        mockProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out moveErrors))
            .Callback(new TryMoveCorruptedConfigurationCallback((string componentId, string configName, out string errors) =>
            {
                moveCalled = true;
                errors = moveFailedMessage;
            })).Returns(false);

        var logMessages = new List<string>();
        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        var configToSave = new TestConfiguration();
        edgeComponentConfigurationProvider.SaveConfiguration(TestConfiguration.ConfigName, configToSave);

        Assert.True(getCalled);
        Assert.True(moveCalled);
        Assert.True(saveCalled);
        Assert.Equal(2, logMessages.Count);
        Assert.Contains(ErrorMessage, logMessages[0]);
        Assert.Contains(moveFailedMessage, logMessages[1]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("test", null)]
    public void EdgeComponentConfigurationProvider_SaveConfiguration_InvalidInput(string configName, TestConfiguration config)
    {
        var configSaved = false;
        var exceptionThrown = false;

        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors = new List<string>();
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration>(), out outErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, TestConfiguration _, out ICollection<string> errors) =>
            {
                configSaved = true;
                errors = new List<string>();
            })).Returns(true);

        var mockLogger = new Mock<ILogger>();
        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);

        try
        {
            edgeComponentConfigurationProvider.SaveConfiguration(configName, config);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.False(configSaved);
        Assert.True(exceptionThrown);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("test", null)]
    public void EdgeComponentConfigurationProvider_SaveArrayConfiguration_InvalidInput(string configName, TestConfiguration[] config)
    {
        var configSaved = false;
        var exceptionThrown = false;

        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors = new List<string>();
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration>(), out outErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, TestConfiguration _, out ICollection<string> errors) =>
            {
                configSaved = true;
                errors = new List<string>();
            })).Returns(true);

        var mockLogger = new Mock<ILogger>();
        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);

        try
        {
            edgeComponentConfigurationProvider.SaveArrayConfiguration(configName, config);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.False(configSaved);
        Assert.True(exceptionThrown);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveArrayConfiguration_InvalidConfig()
    {
        var configSaved = true;
        var configToSave = new TestConfiguration[1];
        var errorMessage = string.Empty;

        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());
        ICollection<string> outErrors;
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration[]>(), out outErrors))
            .Callback(new TrySaveConfigurationArrayCallback((string id, string facet, TestConfiguration[] configs, out ICollection<string> errors) =>
            {
                configSaved = false;
                errors = new List<string> { ErrorMessage };
            })).Returns(false);

        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                errorMessage = message.ToString();
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);

        edgeComponentConfigurationProvider.SaveArrayConfiguration(TestConfiguration.ConfigName, configToSave);
        Assert.False(configSaved);
        Assert.Contains(ErrorMessage, errorMessage);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveArrayConfiguration_InvalidOriginalConfig_MoveSucceeded()
    {
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        var getCalled = false;
        ICollection<string> getErrors = null;
        TestConfiguration[] outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out getErrors))
            .Callback(new TryGetConfigurationArrayCallback((string id, string facet, out TestConfiguration[] configs, out ICollection<string> errors) =>
            {
                getCalled = true;
                configs = null;
                errors = new List<string> { ErrorMessage };
            }));

        var saveCalled = false;
        ICollection<string> saveErrors = null;
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration[]>(), out saveErrors))
            .Callback(new TrySaveConfigurationArrayCallback((string id, string facet, TestConfiguration[] _, out ICollection<string> errors) =>
            {
                saveCalled = true;
                errors = new List<string>();
            })).Returns(true);

        var moveCalled = false;
        string moveErrors = null;
        mockProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out moveErrors))
            .Callback(new TryMoveCorruptedConfigurationCallback((string componentId, string configName, out string errors) =>
            {
                moveCalled = true;
                errors = null;
            })).Returns(true);

        var logMessages = new List<string>();
        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        var configToSave = new TestConfiguration[1];
        edgeComponentConfigurationProvider.SaveArrayConfiguration(TestConfiguration.ConfigName, configToSave);

        Assert.True(getCalled);
        Assert.True(moveCalled);
        Assert.True(saveCalled);
        Assert.Single(logMessages);
        Assert.Contains(ErrorMessage, logMessages[0]);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_SaveArrayConfiguration_InvalidOriginalConfig_MoveFailed()
    {
        var mockProvider = GetMockConfigProvider(Array.Empty<EdgeComponentConfig>());

        var getCalled = false;
        ICollection<string> getErrors = null;
        TestConfiguration[] outTestConfiguration = null;
        mockProvider.Setup(cp => cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out outTestConfiguration, out getErrors))
            .Callback(new TryGetConfigurationArrayCallback((string id, string facet, out TestConfiguration[] configs, out ICollection<string> errors) =>
            {
                getCalled = true;
                configs = null;
                errors = new List<string> { ErrorMessage };
            }));

        var saveCalled = false;
        ICollection<string> saveErrors = null;
        mockProvider.Setup(cp => cp.TrySaveConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, It.IsAny<TestConfiguration[]>(), out saveErrors))
            .Callback(new TrySaveConfigurationArrayCallback((string id, string facet, TestConfiguration[] _, out ICollection<string> errors) =>
            {
                saveCalled = true;
                errors = new List<string>();
            })).Returns(true);

        var moveFailedMessage = "Move failed";
        var moveCalled = false;
        string moveErrors = null;
        mockProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out moveErrors))
            .Callback(new TryMoveCorruptedConfigurationCallback((string componentId, string configName, out string errors) =>
            {
                moveCalled = true;
                errors = moveFailedMessage;
            })).Returns(false);

        var logMessages = new List<string>();
        var mockLogger = new Mock<ILogger>();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);
        var configToSave = new TestConfiguration[1];
        edgeComponentConfigurationProvider.SaveArrayConfiguration(TestConfiguration.ConfigName, configToSave);

        Assert.True(getCalled);
        Assert.True(moveCalled);
        Assert.True(saveCalled);
        Assert.Equal(2, logMessages.Count);
        Assert.Contains(ErrorMessage, logMessages[0]);
        Assert.Contains(moveFailedMessage, logMessages[1]);
    }

    [Fact]
    public void EdgeComponentConfigurationProvider_GetCommonApplicationDataDirectoryPath()
    {
        var edgeComponentsConfig = Array.Empty<EdgeComponentConfig>();
        var mockProvider = GetMockConfigProvider(edgeComponentsConfig);
        var mockLogger = new Mock<ILogger>();

        var edgeComponentConfigurationProvider = new ComponentConfigurationProvider(mockProvider.Object, UnitTestComponentId, mockLogger.Object);

        var programDataPath = edgeComponentConfigurationProvider.GetAdapterDataDirectoryPath();

        Assert.Equal(UnitTestComponentId, programDataPath);
    }

    #region Private Methods

    private static Mock<IConfigurationProvider> GetMockConfigProvider(EdgeComponentConfig[] config)
    {
        var mockProvider = new Mock<IConfigurationProvider>();
        var testConfig = new TestConfiguration();
        ICollection<string> errors = new List<string>();

        mockProvider.Setup(cp =>
            cp.TryGetConfiguration(UnitTestComponentId,
                TestConfiguration.ConfigName, out testConfig, out errors)).Returns(true);
        mockProvider
            .Setup(cp =>
                cp.TryGetConfiguration(EdgeSystemConstants.SystemComponentId,
                    EdgeSystemConstants.ComponentsFacetName, out config, out errors)).Returns(true);

        mockProvider.Setup(cp =>
                cp.GetCommonApplicationDataDirectoryPath(It.IsAny<string>()))
            .Returns((string systemId) => systemId);

        return mockProvider;
    }

    #endregion
}

#region Test Data Class

#pragma warning disable SA1402 // File may only contain a single type
internal class TestDataGenerator : IEnumerable<object[]>
#pragma warning restore SA1402 // File may only contain a single type
{
    private static readonly IConfigurationProvider _configurationProvider = new JsonConfigurationProvider("UnitTests");
    private readonly IEnumerable<object[]> _data = new List<object[]>
    {
        new object[] { null, ComponentConfigurationProvider_Tests.UnitTestComponentId },
        new object[] { _configurationProvider, null },
        new object[] { _configurationProvider, string.Empty },
        new object[] { _configurationProvider, "  " },
    };

    public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

#endregion
