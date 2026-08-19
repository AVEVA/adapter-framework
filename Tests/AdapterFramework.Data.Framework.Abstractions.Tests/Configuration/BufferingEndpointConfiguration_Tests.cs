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
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using Xunit;

namespace AdapterFramework.Data.Framework.Abstractions.Tests.Configuration;

public class BufferingEndpointConfiguration_Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Buffering_OnDisMaxBufferSizeMb_InvalidInput_Test(int bufferSize)
    {
        var bufferConfig = new BufferingConfiguration
        {
            MaxBufferSizeMB = bufferSize,
            BufferLocation = Path.GetTempPath(),
        };

        var validationResults = bufferConfig.Validate();
        Assert.NotEmpty(validationResults);
    }

    [Theory]
    [InlineData(@"my/fake/location/folder")]
    [InlineData(@"^<!@#$%!(*&|&/")]
    [InlineData("./")]
    public void Buffering_OnDiskBufferLocation_InvalidInput_Test(string bufferLocation)
    {
        var bufferConfig = new BufferingConfiguration
        {
            BufferLocation = bufferLocation,
        };

        var validationResults = bufferConfig.Validate();
        Assert.NotEmpty(validationResults);
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(600000, true)]
    [InlineData(600001, false)]
    public void Buffering_MaxDataBulkTime_Input_Test(int maxDataBulkTime, bool isValid)
    {
        var bufferConfig = new BufferingConfiguration()
        {
            BufferLocation = Directory.GetCurrentDirectory(),
            MaxBufferSizeMB = 1000,
            MaxDataBulkTime = TimeSpan.FromMilliseconds(maxDataBulkTime),
        };

        var errors = EdgeConfigurationBase.ValidateConfiguration(bufferConfig);
        Assert.Equal(isValid, errors.Count == 0);
    }

    [Fact]
    public void Buffering_GetOrCreateBufferingConfiguration_TryGetSucceeded_Test()
    {
        var mockLogger = new Mock<ILogger>();
        List<string> logMessages = new();
        mockLogger.Setup(logger => logger.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        ICollection<string> getErrors = null;
        var testBufferLocation = "TestLocation";
        var testMaxBufferSizeMB = 40;
        var configuration = new BufferingConfiguration()
        {
            BufferLocation = testBufferLocation,
            MaxBufferSizeMB = testMaxBufferSizeMB,
        };

        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out configuration, out getErrors)).Returns(true);

        var configurationGot = BufferingConfiguration.GetOrCreateBufferingConfiguration(mockConfigurationProvider.Object, mockLogger.Object);

        Assert.Empty(logMessages);
        Assert.NotNull(configurationGot);
        Assert.Equal(testBufferLocation, configurationGot.BufferLocation);
        Assert.Equal(testMaxBufferSizeMB, configurationGot.MaxBufferSizeMB);
    }

    [Fact]
    public void Buffering_GetOrCreateBufferingConfiguration_NoConfigurationToGet_TrySaveFailed_Test()
    {
        var mockLogger = new Mock<ILogger>();
        List<string> logMessages = new();
        mockLogger.Setup(logger => logger.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        ICollection<string> getErrors = null;
        BufferingConfiguration configuration = null;
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out configuration, out getErrors)).Returns(false);

        ICollection<string> saveErrors = new List<string>() { "Test" };
        var trySaveCalled = false;
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BufferingConfiguration>(), out saveErrors)).Callback(() => { trySaveCalled = true; }).Returns(false);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns("TestCommonApplicationDataDirectoryPath");

        var configurationGot = BufferingConfiguration.GetOrCreateBufferingConfiguration(mockConfigurationProvider.Object, mockLogger.Object);

        Assert.NotEmpty(logMessages);
        Assert.True(trySaveCalled);
        Assert.NotNull(configurationGot);
    }

    [Fact]
    public void Buffering_GetOrCreateBufferingConfiguration_NoConfigurationToGet_TrySaveSucceeded_Test()
    {
        var mockLogger = new Mock<ILogger>();
        List<string> logMessages = new();
        mockLogger.Setup(logger => logger.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        ICollection<string> getErrors = null;
        BufferingConfiguration configuration = null;
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out configuration, out getErrors)).Returns(false);

        ICollection<string> saveErrors = new List<string>() { "Test" };
        var trySaveCalled = false;
        mockConfigurationProvider.Setup(x => x.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BufferingConfiguration>(), out saveErrors)).Callback(() => { trySaveCalled = true; }).Returns(true);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns("TestCommonApplicationDataDirectoryPath");

        var configurationGot = BufferingConfiguration.GetOrCreateBufferingConfiguration(mockConfigurationProvider.Object, mockLogger.Object);

        Assert.Empty(logMessages);
        Assert.True(trySaveCalled);
        Assert.NotNull(configurationGot);
    }

    [Fact]
    public void Buffering_GetOrCreateBufferingConfiguration_TryGetFailedWithError_Test()
    {
        var mockLogger = new Mock<ILogger>();
        List<string> logMessages = new();
        mockLogger.Setup(logger => logger.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
            .Callback((LogLevel logLevel, EventId eventId, dynamic message, object exception, object func) =>
            {
                logMessages.Add(message.ToString());
            });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        ICollection<string> getErrors = new List<string>() { "TestError" };
        BufferingConfiguration configuration = null;
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out configuration, out getErrors)).Returns(false);
        mockConfigurationProvider.Setup(x => x.GetCommonApplicationDataDirectoryPath(It.IsAny<string>())).Returns("TestCommonApplicationDataDirectoryPath");

        var configurationGot = BufferingConfiguration.GetOrCreateBufferingConfiguration(mockConfigurationProvider.Object, mockLogger.Object);

        Assert.NotEmpty(logMessages);
        Assert.NotNull(configurationGot);
    }
}
