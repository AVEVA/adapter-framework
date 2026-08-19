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
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using Xunit;

namespace AdapterFramework.Data.Framework.Logger.Tests;

public class LogManager_Tests
{
    private readonly string _logSourceName = "sourceName";

    [Fact]
    public void LogManager_GetLoggerNoFileNoParameter()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider
            .Setup(configurationProvider => configurationProvider.GetCommonApplicationDataDirectoryPath(null))
            .Returns(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        var logManager = new LogManager(mockConfigurationProvider.Object);
        var logOne = logManager.GetOrCreateLogger(_logSourceName, null);

        Assert.NotNull(logOne);

        mockConfigurationProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<LoggerConfiguration>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.AtLeastOnce());
        mockConfigurationProvider.Verify(x => x.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LoggerConfiguration>(), out It.Ref<ICollection<string>>.IsAny), Times.AtLeastOnce());
    }

    [Fact]
    public void LogManager_GetLoggerNoFileConfigParameterExist()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider
            .Setup(configurationProvider => configurationProvider.GetCommonApplicationDataDirectoryPath(null))
            .Returns(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        var logManager = new LogManager(mockConfigurationProvider.Object);
        var logOne = logManager.GetOrCreateLogger(_logSourceName, new LoggerConfiguration());

        Assert.NotNull(logOne);

        mockConfigurationProvider.Verify(x => x.TryGetConfiguration<LoggerConfiguration>(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<LoggerConfiguration>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Never());
        mockConfigurationProvider.Verify(x => x.TrySaveConfiguration<LoggerConfiguration>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LoggerConfiguration>(), out It.Ref<ICollection<string>>.IsAny), Times.Never());
    }

    [Fact]
    public void LogManager_GetLoggerFileExistConfigParameterNull()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var newLoggerConfiguration = new LoggerConfiguration();
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration<LoggerConfiguration>(It.IsAny<string>(), It.IsAny<string>(), out newLoggerConfiguration, out It.Ref<ICollection<string>>.IsAny)).Returns(true);
        mockConfigurationProvider
            .Setup(configurationProvider => configurationProvider.GetCommonApplicationDataDirectoryPath(null))
            .Returns(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        var logManager = new LogManager(mockConfigurationProvider.Object);
        var logOne = logManager.GetOrCreateLogger(_logSourceName, null);

        Assert.NotNull(logOne);

        mockConfigurationProvider.Verify(x => x.GetCommonApplicationDataDirectoryPath(null), Times.AtLeastOnce());
        mockConfigurationProvider.Verify(x => x.TryGetConfiguration<LoggerConfiguration>(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<LoggerConfiguration>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.AtLeastOnce());
        mockConfigurationProvider.Verify(x => x.TrySaveConfiguration<LoggerConfiguration>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LoggerConfiguration>(), out It.Ref<ICollection<string>>.IsAny), Times.Never());
    }

    [Fact]
    public void LogManager_GetExistingLogger()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider
            .Setup(configurationProvider => configurationProvider.GetCommonApplicationDataDirectoryPath(null))
            .Returns(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        var logManager = new LogManager(mockConfigurationProvider.Object);
        var logOne = logManager.GetOrCreateLogger(_logSourceName, null);

        mockConfigurationProvider.Invocations.Clear();

        Assert.NotNull(logOne);

        var logTwo = logManager.GetOrCreateLogger(_logSourceName, null);

        mockConfigurationProvider.Verify(x => x.TryGetConfiguration<LoggerConfiguration>(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<LoggerConfiguration>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Never());
        mockConfigurationProvider.Verify(x => x.TrySaveConfiguration<LoggerConfiguration>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LoggerConfiguration>(), out It.Ref<ICollection<string>>.IsAny), Times.Never());

        Assert.Equal(logOne, logTwo);
    }

    [Fact]
    public void LogManager_LogsFolderPathNotEmpty()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider
            .Setup(configurationProvider => configurationProvider.GetCommonApplicationDataDirectoryPath(null))
            .Returns(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        var logManager = new LogManager(mockConfigurationProvider.Object);
        Assert.NotEmpty(logManager.LogsFolderPath);
        Assert.Contains(EdgeSystemConstants.LoggingDirectoryName, logManager.LogsFolderPath, StringComparison.InvariantCultureIgnoreCase);
    }
}
