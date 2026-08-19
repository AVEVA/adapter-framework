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
using System.IO;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Extensions;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests;

public class ConfigurationProviderBase_Tests
{
    private const string ApplicationDataDirectory = "UnitTest";
    private const string CommonApplicationDataDirPlaceholder = "$CommonApplicationData";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ConfigurationProviderBase_Constructor_InvalidInput(string applicationDataDirectory)
    {
        var exceptionThrown = false;

        try
        {
            var configurationProvider = new JsonConfigurationProvider(applicationDataDirectory);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Theory]
    [InlineData(ApplicationDataDirectory)]
    [InlineData("Adapters/" + ApplicationDataDirectory)]
    [InlineData(CommonApplicationDataDirPlaceholder)]
    public void ConfigurationProviderBase_Constructor_ValidInput(string applicationDataDirectory)
    {
        string dataDirectory;
        string expectedDataDirectory;

        if (applicationDataDirectory.Equals(CommonApplicationDataDirPlaceholder,
            StringComparison.InvariantCultureIgnoreCase))
        {
            dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), EdgeSystemConstants.AdapterFrameworkDirectoryName, ApplicationDataDirectory, " ").TrimEnd();
            expectedDataDirectory = dataDirectory;
        }
        else
        {
            dataDirectory = applicationDataDirectory;
            expectedDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    EdgeSystemConstants.AdapterFrameworkDirectoryName, applicationDataDirectory, " ").TrimEnd();
        }

        var configurationProvider = new JsonConfigurationProvider(dataDirectory);

        Assert.Equal(expectedDataDirectory, configurationProvider.GetCommonApplicationDataDirectoryPath());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConfigurationProviderBase_GetCommonApplicationDataDirectoryPath_InvalidInput(string componentId)
    {
        var exceptionThrown = false;
        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        try
        {
            configurationProvider.GetCommonApplicationDataDirectoryPath(componentId);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ComponentId")]
    public void ConfigurationProviderBase_GetCommonApplicationDataDirectoryPath_Test(string componentId)
    {
        var commonApplicationDataPath = "UnitTest/UnitTest_1";

        var expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), EdgeSystemConstants.AdapterFrameworkDirectoryName, commonApplicationDataPath, " ").TrimEnd();

        if (componentId != null)
        {
            expectedPath = Path.Combine(expectedPath, componentId);
        }

        var configurationProvider = new JsonConfigurationProvider(commonApplicationDataPath);

        var path = configurationProvider.GetCommonApplicationDataDirectoryPath(componentId);

        Assert.NotNull(path);
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void ConfigurationProviderBase_GetBaseDirectoryPath_Test()
    {
        var expectedPath = Path.GetDirectoryName(AppContext.BaseDirectory);

        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        var path = configurationProvider.GetBaseDirectoryPath();

        Assert.Equal(expectedPath, path);
    }

    [Theory]
    [InlineData("UnitTest")]
    [InlineData("UnitTest/Test1")]
    [InlineData("UnitTest/Test1/Test/")]
    public void ConfigurationProviderBase_GetCommonApplicationDirectoryName_Test(string commonDirectoryPath)
    {
        ThrowHelper.ThrowIfArgumentNull(commonDirectoryPath, nameof(commonDirectoryPath));

        var configurationProvider = new JsonConfigurationProvider(commonDirectoryPath);

        commonDirectoryPath = commonDirectoryPath.TrimEnd(EdgeSystemConstants.SeparatorSlashCharacter);
        var expectedDirectoryName = commonDirectoryPath.Substring(commonDirectoryPath.LastIndexOf(EdgeSystemConstants.SeparatorSlashCharacter) + 1);

        var directoryName = configurationProvider.GetCommonApplicationDataDirectoryName();

        Assert.Equal(expectedDirectoryName, directoryName);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("UnitTest", null)]
    [InlineData("UnitTest", "")]
    [InlineData("UnitTest", " ")]
    public void ConfigurationProviderBase_GetConfigFilePath_InvalidInput(string componentId, string configName)
    {
        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);
        var exceptionThrown = false;

        try
        {
            configurationProvider.GetConfigFilePath(componentId, configName);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Theory]
    [InlineData("UnitTest1", "Global")]
    [InlineData("Unit.Test_1", "Global")]
    [InlineData("Unit.Test.1", "Global")]
    public void ConfigurationProviderBase_GetConfigFilePath_ValidPath(string componentId, string configName)
    {
        var expectedConfigName = componentId + EdgeSystemConstants.SeparatorUnderscore + configName + ".json";
        var expectedPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var expectedFilePath = Path.Combine(expectedPath, EdgeSystemConstants.AdapterFrameworkDirectoryName, ApplicationDataDirectory, EdgeSystemConstants.ConfigurationDirectoryName, expectedConfigName);

        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        var configFilePath = configurationProvider.GetConfigFilePath(componentId, configName);

        Assert.Equal(expectedFilePath, configFilePath);
    }

    [Fact]
    public void ConfigurationProviderBase_DeleteConfiguration_FileDeleted()
    {
        var componentId = "UnitTest1";
        var configName = "Global";

        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);

        configurationProvider.TrySaveConfiguration(componentId, configName, new TestConfiguration() { StringProp = "hello" }, out _);

        var filePath = configurationProvider.GetConfigFilePath(componentId, configName);

        Assert.True(File.Exists(filePath));

        configurationProvider.DeleteConfiguration(componentId, configName);

        Assert.False(File.Exists(filePath));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("UnitTest", null)]
    [InlineData("UnitTest", "")]
    [InlineData("UnitTest", " ")]
    public void ConfigurationProviderBase_DeleteConfiguration_InvalidInput(string componentId, string configName)
    {
        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);
        var exceptionThrown = false;

        try
        {
            configurationProvider.DeleteConfiguration(componentId, configName);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }
}
