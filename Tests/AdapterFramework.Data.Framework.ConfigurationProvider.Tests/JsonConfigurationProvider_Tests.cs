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
using AdapterFramework.Data.Framework.Abstractions.Constants;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests;

public class JsonConfigurationProvider_Tests : IDisposable
{
    private const string ComponentId = "Unit_Test";
    private const string InvalidJsonConfig = "{}dasdasd";
    private readonly JsonConfigurationProvider _provider;

    private readonly TestConfiguration _testConfiguration = new TestConfiguration
    {
        StringProp = "UnitTest Config",
        EnumProp = TestEnum.Option2,
        IntProp = 42,
        ListProp = new List<string> { "Hello", "World" },
    };

    private readonly TestConfigurationNoValidate _testConfigurationNoValidateValid = new TestConfigurationNoValidate
    {
        StringProp = "UnitTest Config",
        EnumProp = TestEnum.Option2,
        IntProp = 42,
        ListProp = new List<string> { "Hello", "World" },
    };

    private readonly List<TestDiscoveryResult> _testDiscoveryResults = new List<TestDiscoveryResult>
    {
        new TestDiscoveryResult
        {
            Selected = true,
            Name = "Test Discovery Result Name",
            StreamId = "Test Discovery Stream Id",
        },
    };

    private readonly List<TestDiscoveryResultNoValidate> _testDiscoveryResultsNoValidateValid = new List<TestDiscoveryResultNoValidate>
    {
        new TestDiscoveryResultNoValidate
        {
            Selected = true,
            Name = "Test Discovery Result Name",
            StreamId = "Test Discovery Stream Id",
        },
    };

    private bool _disposed;

    public JsonConfigurationProvider_Tests()
    {
        _provider = new JsonConfigurationProvider("UnitTests");
        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, _testConfiguration, out _);
        _provider.TrySaveDiscoveryResult(ComponentId, TestDiscoveryResult.DiscoveryId, _testDiscoveryResults, out _);
    }

    [Fact]
    public void JsonConfigurationProvider_GetConfiguration_NotFound_NoThrow()
    {
        _provider.TryGetConfiguration<TestConfiguration>("TestId", "S", out var config, out _);

        Assert.Null(config);
    }

    [Fact]
    public void JsonConfigurationProvider_GetConfigurationWithType_NotFound_NoThrow()
    {
        _provider.TryGetConfiguration("TestId", "S",  typeof(TestConfiguration), out var config, out _);

        Assert.Null(config);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_NotFound_NoThrow()
    {
        var result = _provider.TryGetConfiguration<TestConfiguration>("TestId", "S", out var configuration, out var errors);

        Assert.False(result);
        Assert.Null(configuration);
        Assert.Empty(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_NotFound_NoThrow()
    {
        var result = _provider.TryGetConfiguration("TestId", "S", typeof(TestConfiguration), out var configuration, out var errors);

        Assert.False(result);
        Assert.Null(configuration);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, "")]
    [InlineData(null, "Valid")]
    [InlineData("Valid", null)]
    public void JsonConfigurationProvider_TryGetConfiguration_InvalidInput(string componentId, string configName)
    {
        var result = _provider.TryGetConfiguration<TestConfiguration>(componentId, configName, out var configuration, out var errors);

        Assert.Null(configuration);
        Assert.False(result);
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, "")]
    [InlineData(null, "Valid")]
    [InlineData("Valid", null)]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_InvalidInput(string componentId, string configName)
    {
        var result = _provider.TryGetConfiguration(componentId, configName, typeof(TestConfiguration), out var configuration, out var errors);

        Assert.Null(configuration);
        Assert.False(result);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_Found()
    {
        var result = _provider.TryGetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName, out var configuration, out var errors);

        Assert.True(result);
        Assert.NotNull(configuration);
        Assert.Empty(errors);
        Assert.True(TestConfiguration.AreEqual(_testConfiguration, configuration));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_Found()
    {
        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(TestConfiguration), out var configuration, out var errors);

        Assert.True(result);
        Assert.NotNull(configuration);
        Assert.Empty(errors);
        Assert.True(TestConfiguration.AreEqual(_testConfiguration, (TestConfiguration)configuration));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_Found_Invalid()
    {
        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, new TestConfigurationNoValidate(), out _);
        var result = _provider.TryGetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName, out var configuration, out var errors);

        Assert.False(result);
        Assert.NotNull(configuration);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_Found_Invalid()
    {
        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, new TestConfigurationNoValidate(), out _);
        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(TestConfiguration), out var configuration, out var errors);

        Assert.False(result);
        Assert.NotNull(configuration);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_Found_InvalidProperty()
    {
        var testConfig = new TestConfigurationNoValidate { IntProp = 9000, StringProp = "10" };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, testConfig);
        var result = _provider.TryGetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName, out var configuration, out var errors);

        Assert.False(result);
        Assert.NotNull(configuration);
        Assert.NotEmpty(errors);
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.IntProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_Found_InvalidProperty()
    {
        var testConfig = new TestConfigurationNoValidate { IntProp = 9000, StringProp = "10" };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, testConfig);
        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(TestConfiguration), out var configuration, out var errors);

        Assert.False(result);
        Assert.NotNull(configuration);
        Assert.NotEmpty(errors);
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.IntProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetEnumerableConfiguration_Found()
    {
        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfiguration>() { _testConfiguration, _testConfiguration }, out _);
        var result = _provider.TryGetConfiguration<List<TestConfiguration>>(ComponentId, TestConfiguration.ConfigName, out var configurations, out var errors);

        Assert.True(result);
        Assert.NotNull(configurations);
        Assert.Empty(errors);
        Assert.All(configurations, item => Assert.Equal(_testConfiguration, item));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetEnumerableConfigurationWithType_Found()
    {
        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfiguration>() { _testConfiguration, _testConfiguration }, out _);
        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(List<TestConfiguration>), out var configurations, out var errors);

        Assert.True(result);
        Assert.NotNull(configurations);
        Assert.Empty(errors);
        Assert.All((List<TestConfiguration>)configurations, item => Assert.Equal(_testConfiguration, item));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_FoundEnumerable_Invalid()
    {
        var testConfig = new TestConfigurationNoValidate { StringProp = null };

        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfigurationNoValidate>() { _testConfigurationNoValidateValid, testConfig }, out _);
        var result = _provider.TryGetConfiguration<List<TestConfiguration>>(ComponentId, TestConfiguration.ConfigName, out var configurations, out var errors);

        Assert.False(result);
        Assert.NotNull(configurations);
        Assert.NotEmpty(errors);
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.StringProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_FoundEnumerable_Invalid()
    {
        var testConfig = new TestConfigurationNoValidate { StringProp = null };

        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfigurationNoValidate>() { _testConfigurationNoValidateValid, testConfig }, out _);
        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(List<TestConfiguration>), out var configurations, out var errors);

        Assert.False(result);
        Assert.NotNull(configurations);
        Assert.NotEmpty(errors);
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.StringProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_FoundEnumerable_InvalidProperty()
    {
        var testConfig = new TestConfigurationNoValidate { IntProp = 9000, StringProp = "10" };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfigurationNoValidate>() { _testConfigurationNoValidateValid, testConfig });
        var result = _provider.TryGetConfiguration<List<TestConfiguration>>(ComponentId, TestConfiguration.ConfigName, out var configurations, out var errors);

        Assert.False(result);
        Assert.NotNull(configurations);
        Assert.NotEmpty(errors);
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.IntProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_FoundEnumerable_InvalidProperty()
    {
        var testConfig = new TestConfigurationNoValidate { IntProp = 9000, StringProp = "10" };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfigurationNoValidate>() { _testConfigurationNoValidateValid, testConfig });
        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(List<TestConfiguration>), out var configurations, out var errors);

        Assert.False(result);
        Assert.NotNull(configurations);
        Assert.NotEmpty(errors);
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.IntProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_FoundEnumerable_MultipleInvalid()
    {
        var testConfig = new TestConfigurationNoValidate { IntProp = 900, StringProp = "10" };
        var testConfig2 = new TestConfigurationNoValidate { StringProp = null };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfigurationNoValidate>() { testConfig, testConfig2 });
        var result = _provider.TryGetConfiguration<List<TestConfiguration>>(ComponentId, TestConfiguration.ConfigName, out var configurations, out var errors);

        Assert.False(result);
        Assert.NotNull(configurations);
        Assert.NotEmpty(errors);

        // unfortunately order of contains matters. 
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.IntProp), item, StringComparison.InvariantCulture),
            item => Assert.Contains(nameof(testConfig.StringProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_FoundEnumerable_MultipleInvalid()
    {
        var testConfig = new TestConfigurationNoValidate { IntProp = 900, StringProp = "10" };
        var testConfig2 = new TestConfigurationNoValidate { StringProp = null };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, new List<TestConfigurationNoValidate>() { testConfig, testConfig2 });
        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(List<TestConfiguration>), out var configurations, out var errors);

        Assert.False(result);
        Assert.NotNull(configurations);
        Assert.NotEmpty(errors);

        // unfortunately order of contains matters. 
        Assert.Collection(errors, item => Assert.Contains(nameof(testConfig.IntProp), item, StringComparison.InvariantCulture),
            item => Assert.Contains(nameof(testConfig.StringProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveConfiguration_ConfigUpdated()
    {
        var valid = _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, _testConfiguration, out _);
        Assert.True(valid);

        _provider.TryGetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName, out var persistedConfig, out _);

        Assert.NotNull(persistedConfig);
        Assert.True(TestConfiguration.AreEqual(_testConfiguration, persistedConfig));
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveConfiguration_ConfigUpdated_Invalid()
    {
        var updatedConfig = new TestConfiguration()
        {
            StringProp = null,
            EnumProp = TestEnum.Option3,
            IntProp = 42,
            ListProp = new List<string> { "Updated" },
        };

        var valid = _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, updatedConfig, out var errors);
        Assert.False(valid);
        Assert.Collection(errors, item => Assert.Contains(nameof(updatedConfig.StringProp), item, StringComparison.InvariantCulture));
        _provider.TryGetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName, out var config, out _);
        Assert.Equal(config, _testConfiguration);
        Assert.Equal(config, _testConfiguration);
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveConfiguration_ConfigUpdated_InvalidProperty()
    {
        var updatedConfig = new TestConfiguration()
        {
            StringProp = "Updated Config",
            EnumProp = TestEnum.Option3,
            IntProp = 9000,
            ListProp = new List<string> { "Updated" },
        };

        var valid = _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, updatedConfig, out var errors);
        Assert.False(valid);
        Assert.Collection(errors, item => Assert.Contains(nameof(updatedConfig.IntProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveConfiguration_ConfigUpdatedEnumerable()
    {
        var valid = _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName,
            new List<TestConfiguration>() { _testConfiguration, _testConfiguration }, out var _);
        Assert.True(valid);

        _provider.TryGetConfiguration<List<TestConfiguration>>(ComponentId, TestConfiguration.ConfigName, out var persistedConfigs, out _);

        Assert.NotNull(persistedConfigs);
        Assert.All(persistedConfigs, item => Assert.Equal(_testConfiguration, item));
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveConfiguration_ConfigUpdatedEnumerable_Invalid()
    {
        var updatedConfig = new TestConfiguration()
        {
            StringProp = null,
            EnumProp = TestEnum.Option3,
            IntProp = 42,
            ListProp = new List<string> { "Updated" },
        };

        var valid = _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName,
             new List<TestConfiguration>() { _testConfiguration, updatedConfig }, out var errors);
        Assert.False(valid);
        Assert.Collection(errors, item => Assert.Contains(nameof(updatedConfig.StringProp), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveConfiguration_ConfigUpdatedEnumerable_InvalidProperty()
    {
        var updatedConfig = new TestConfiguration()
        {
            StringProp = "Updated Config",
            EnumProp = TestEnum.Option3,
            IntProp = 9000,
            ListProp = new List<string> { "Updated" },
        };

        var valid = _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName,
             new List<TestConfiguration>() { _testConfiguration, updatedConfig }, out var errors);
        Assert.False(valid);
        Assert.Collection(errors, item => Assert.Contains(nameof(updatedConfig.IntProp), item, StringComparison.InvariantCulture));
        _provider.TryGetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName, out var config, out _);
        Assert.Equal(config, _testConfiguration);
    }

    [Fact]
    public void JsonConfigurationProvider_IsConfigurationValid_NullConfiguration()
    {
        object test = null;
        Assert.False(_provider.IsConfigurationValid(test, out var errors));
        Assert.Single(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_IsConfigurationValid_Success()
    {
        var validConfiguration = new List<TestConfiguration> { _testConfiguration, _testConfiguration };
        Assert.True(_provider.IsConfigurationValid(validConfiguration, out var errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_IsConfigurationValid_InvalidConfig()
    {
        var invalidConfiguration = new TestConfiguration()
        {
            StringProp = "Updated Config",
            EnumProp = TestEnum.Option3,
            IntProp = 9000,
            ListProp = new List<string> { "Updated" },
        };

        Assert.False(_provider.IsConfigurationValid(invalidConfiguration, out var errors));
        Assert.Single(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_ConvertsTimeSpanInConfig()
    {
        var timeSpan = TimeSpan.FromSeconds(15);

        var timeSpanConfiguration = new TestConfiguration()
        {
            TimeSpanProp = timeSpan,
        };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, timeSpanConfiguration);
        var config = _provider.GetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName);

        Assert.Equal(timeSpan, config.TimeSpanProp);
    }

    [Fact]
    public void JsonConfigurationProvider_ConvertsNullTimeSpanInConfig()
    {
        TimeSpan? timeSpan = null;

        var timeSpanConfiguration = new TestConfiguration()
        {
            TimeSpanProp = timeSpan,
        };

        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, timeSpanConfiguration);
        var config = _provider.GetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName);

        Assert.Equal(timeSpan, config.TimeSpanProp);
    }
    
    [Fact]
    public void JsonConfigurationProvider_TryGetDiscoveryResult_NotFound_NoThrow()
    {
        var result = _provider.TryGetDiscoveryResult<List<TestDiscoveryResult>>("TestId", "S", out var discoveryResults, out var errors);

        Assert.False(result);
        Assert.Null(discoveryResults);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, "")]
    [InlineData(null, "Valid")]
    [InlineData("Valid", null)]
    public void JsonConfigurationProvider_TryGetDiscoveryResult_InvalidInput(string componentId, string discoveryId)
    {
        var result = _provider.TryGetDiscoveryResult<List<TestDiscoveryResult>>(componentId, discoveryId, out var discoveryResults, out var errors);

        Assert.Null(discoveryResults);
        Assert.False(result);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetDiscoveryResult_Found()
    {
        var result = _provider.TryGetDiscoveryResult<List<TestDiscoveryResult>>(ComponentId, TestDiscoveryResult.DiscoveryId, out var discoveryResults, out var errors);

        Assert.True(result);
        Assert.NotNull(discoveryResults);
        Assert.Empty(errors);
        Assert.Equal(discoveryResults, _testDiscoveryResults);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetDiscoveryResult_Found_OneInvalid()
    {
        var testDiscoveryResult = new TestDiscoveryResultNoValidate { Name = "Test", StreamId = string.Empty };

        var newDiscoveryResultList = new List<TestDiscoveryResultNoValidate>(_testDiscoveryResultsNoValidateValid)
        {
            testDiscoveryResult,
        };

        _provider.TrySaveDiscoveryResult(ComponentId, TestDiscoveryResult.DiscoveryId, newDiscoveryResultList, out _);
        var result = _provider.TryGetDiscoveryResult<List<TestDiscoveryResult>>(ComponentId, TestDiscoveryResult.DiscoveryId, out var discoveryResults, out var errors);

        Assert.False(result);
        Assert.NotNull(discoveryResults);
        Assert.NotEmpty(errors);
        Assert.Collection(errors, item => Assert.Contains(nameof(testDiscoveryResult.StreamId), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetDiscoveryResult_MultipleInvalid()
    {
        var testResult1 = new TestDiscoveryResultNoValidate { Name = "Test", StreamId = string.Empty };
        var testResult2 = new TestDiscoveryResultNoValidate { Name = string.Empty, StreamId = "Test" };

        _provider.TrySaveDiscoveryResult(ComponentId, TestDiscoveryResult.DiscoveryId, new List<TestDiscoveryResultNoValidate>() { testResult1, testResult2 }, out _);
        var result = _provider.TryGetDiscoveryResult<List<TestDiscoveryResult>>(ComponentId, TestDiscoveryResult.DiscoveryId, out var discoveryResults, out var errors);

        Assert.False(result);
        Assert.NotNull(discoveryResults);
        Assert.NotEmpty(errors);

        // unfortunately order of contains matters. 
        Assert.Collection(errors, item => Assert.Contains(nameof(testResult1.StreamId), item, StringComparison.InvariantCulture),
            item => Assert.Contains(nameof(testResult2.Name), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveDiscoveryResult_Updated()
    {
        var valid = _provider.TrySaveDiscoveryResult(ComponentId, TestDiscoveryResult.DiscoveryId,
            new List<TestDiscoveryResult>() { _testDiscoveryResults[0], _testDiscoveryResults[0] }, out var _);
        Assert.True(valid);

        _provider.TryGetDiscoveryResult<List<TestDiscoveryResult>>(ComponentId, TestDiscoveryResult.DiscoveryId, out var persistedResults, out _);

        Assert.NotNull(persistedResults);
        Assert.All(persistedResults, item => Assert.Equal(_testDiscoveryResults[0], item));
    }

    [Fact]
    public void JsonConfigurationProvider_TrySaveDiscoveryResult_Invalid()
    {
        var invalidResult = new TestDiscoveryResult()
        {
            Name = string.Empty,
            StreamId = "Test",
        };

        var newResultList = new List<TestDiscoveryResult>(_testDiscoveryResults)
        {
            invalidResult,
        };

        var valid = _provider.TrySaveDiscoveryResult(ComponentId, TestDiscoveryResult.DiscoveryId, newResultList, out var errors);
        Assert.False(valid);
        Assert.Collection(errors, item => Assert.Contains(nameof(invalidResult.Name), item, StringComparison.InvariantCulture));
    }

    [Fact]
    public void JsonConfigurationProvider_DeleteDiscoveryResult_InvalidDiscoveryId()
    {
        Assert.Throws<ArgumentNullException>(() => _provider.DeleteDiscoveryResult(ComponentId, null));
    }

    [Fact]
    public void JsonConfigurationProvider_DeleteDiscoveryResult_validDiscoveryId()
    {
        _provider.DeleteDiscoveryResult(ComponentId, TestDiscoveryResult.DiscoveryId);
        var result = _provider.TryGetDiscoveryResult<List<TestDiscoveryResult>>(ComponentId, TestDiscoveryResult.DiscoveryId, out var discoveryResults, out var errors);

        Assert.False(result);
        Assert.Null(discoveryResults);
        Assert.Empty(errors);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfiguration_InvalidJson()
    {
        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, InvalidJsonConfig);

        var result = _provider.TryGetConfiguration<TestConfiguration>(ComponentId, TestConfiguration.ConfigName, out var _, out var errors);
        Assert.False(result);
        Assert.NotEmpty(errors);

        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, _testConfiguration, out _);
    }

    [Fact]
    public void JsonConfigurationProvider_TryGetConfigurationWithType_InvalidJson()
    {
        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, InvalidJsonConfig);

        var result = _provider.TryGetConfiguration(ComponentId, TestConfiguration.ConfigName, typeof(TestConfiguration), out var _, out var errors);
        Assert.False(result);
        Assert.NotEmpty(errors);

        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, _testConfiguration, out _);
    }

    [Fact]
    public void JsonConfigurationProvider_MoveCorruptedConfiguration()
    {
        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, InvalidJsonConfig);

        var removedFolderPath = Path.Combine(_provider.ConfigDirPath, EdgeSystemConstants.RemovedDirectoryName);
        
        if (Directory.Exists(removedFolderPath))
        {
            Directory.Delete(removedFolderPath, true);
        }
        
        _provider.MoveCorruptedConfiguration(ComponentId, TestConfiguration.ConfigName);

        Assert.True(Directory.Exists(removedFolderPath));
        Assert.NotEmpty(Directory.GetFiles(removedFolderPath));

        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, _testConfiguration, out _);
    }

    [Fact]
    public void JsonConfigurationProvider_TryMoveCorruptedConfiguration()
    {
        _provider.SaveConfiguration(ComponentId, TestConfiguration.ConfigName, InvalidJsonConfig);

        var removedFolderPath = Path.Combine(_provider.ConfigDirPath, EdgeSystemConstants.RemovedDirectoryName);

        if (Directory.Exists(removedFolderPath))
        {
            Directory.Delete(removedFolderPath, true);
        }

        _provider.TryMoveCorruptedConfiguration(ComponentId, TestConfiguration.ConfigName, out _);

        Assert.True(Directory.Exists(removedFolderPath));
        Assert.NotEmpty(Directory.GetFiles(removedFolderPath));

        _provider.TrySaveConfiguration(ComponentId, TestConfiguration.ConfigName, _testConfiguration, out _);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _provider.DeleteConfiguration(ComponentId, TestConfiguration.ConfigName);
        }

        _disposed = true;
    }
}
