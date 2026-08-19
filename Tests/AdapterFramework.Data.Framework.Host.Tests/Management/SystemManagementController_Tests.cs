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
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.Host.Management;
using AdapterFramework.Data.Framework.Registry;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Configuration;

public class SystemManagementController_Tests
{
    private const string ComponentId = EdgeSystemConstants.ManagementComponentId;
    private const string FacetName1 = "SomeFacet1";
    private const string ConfigurationId = "Index0";

    private static JsonSerializerOptions _jsonSerializerOptions;
    private bool _callbackActionCalled;
    private ConfigurationChangedEventArgs _configurationChangedArgs;

    public SystemManagementController_Tests()
    {
        _configurationChangedArgs = null;
        _callbackActionCalled = false;

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
    }

    private delegate void TrySaveConfigurationCallback(string id, string facet, object config, out ICollection<string> errors);
    private delegate void MockTrySaveCallback(string componentId, string configurationName, object configuration, out ICollection<string> err);
    private delegate void MockTryGetCallback(string componentId, string configurationName, Type configType, out object configuration, out ICollection<string> err);

    private interface ISampleConfiguration
    {
        string Index { get; set; }

        int IntProperty { get; set; }

        string Secret { get; set; }
    }

    [Fact]
    public void GetConfiguration_Configuration_NotFound()
    {
        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(null, testLogger, managementRegistry);
        var result = managementController.GetConfiguration(FacetName1);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(JsonSerializer.Deserialize<JsonElement>("[]").GetRawText(), objectResult.Value.ToString());
    }

    [Fact]
    public void GetConfiguration_Configuration_Array_Found()
    {
        var expectedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1);
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>()))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(expectedConfiguration);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.GetConfiguration(FacetName1);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(expectedConfiguration, objectResult.Value);
        Assert.True(maskSecretsCalled);
    }

    [Fact]
    public void GetConfiguration_Configuration_Simple_Found()
    {
        var expectedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>()))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration))).Returns(expectedConfiguration);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.GetConfiguration(FacetName1);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(expectedConfiguration, objectResult.Value);
        Assert.True(maskSecretsCalled);
    }

    [Fact]
    public void GetConfigurationById_Configuration_IdNotFound()
    {
        const string NonExistentConfigurationId = "nonExistent";
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(3);
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, typeof(SampleIdConfiguration)))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(existingConfiguration);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1), null, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.GetConfigurationById(FacetName1, NonExistentConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.False(maskSecretsCalled);
    }

    [Fact]
    public void GetConfigurationById_Configuration_IncompatibleType()
    {
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(3);
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>())).Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleConfiguration[])))
            .Returns(existingConfiguration);

        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1);
        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleConfiguration[]>(ComponentId, FacetName1, commandGenerator, null, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.GetConfigurationById(FacetName1, ConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.False(maskSecretsCalled);
    }

    [Fact]
    public void GetConfigurationById_Configuration_Found()
    {
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(3);
        var expectedConfigurationEntry = existingConfiguration[0];
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>()))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(existingConfiguration);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.GetConfigurationById(FacetName1, ConfigurationId);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(expectedConfigurationEntry, objectResult.Value);
        Assert.True(maskSecretsCalled);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void GetConfiguration_InvalidConfiguration()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Throws(new Exception());

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(null, testLogger, managementRegistry);
        var result = managementController.GetConfiguration(FacetName1);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public void PutConfiguration_Simple_Success()
    {
        object savedConfig = null;
        ICollection<string> errors = new List<string>();
        var postedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var protectSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration)))
            .Returns(postedConfiguration);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.PutConfiguration(FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(postedConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.True(_callbackActionCalled);
        Assert.NotNull(savedConfig);
    }

    [Fact]
    public void PutConfiguration_Collection_Success()
    {
        ICollection<string> errors = new List<string>();
        var postedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>())).Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = postedConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.PutConfiguration(FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(postedConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PutConfiguration_WithId_InsertIfNotPresent()
    {
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var replacementConfiguration = new SampleIdConfiguration { Index = ConfigurationId, Secret = "SuperSecret", BoolProperty = true, IntProperty = 42 };
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>())).Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.PutConfigurationById(FacetName1, replacementConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(replacementConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Equal(2, convertedSavedConfig.Length);

        savedConfig = null;
        protectSecretsCalled = false;
        _callbackActionCalled = false;

        var newConfiguration = new SampleIdConfiguration { Index = "SomethingNew", Secret = "NoSecret", BoolProperty = true, IntProperty = 33 };
        result = managementController.PutConfigurationById(FacetName1, newConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(newConfiguration), _jsonSerializerOptions));

        noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PutConfiguration_WithId_InsertIfNotPresent_EmptyFacet()
    {
        ICollection<string> errors = new List<string>();
        SampleIdConfiguration[] existingConfiguration = null;
        var replacementConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>())).Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.PutConfigurationById(FacetName1, replacementConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(replacementConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Single(convertedSavedConfig);
    }

    [Fact]
    public void PutConfiguration_WithId_FailIfIdsDoNotMatch()
    {
        ICollection<string> errors = new List<string>();
        const int ConfigurationEntryCount = 3;
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(ConfigurationEntryCount);
        var replacementConfiguration = new SampleIdConfiguration { Index = ConfigurationId, Secret = "SuperSecret", BoolProperty = true, IntProperty = 42 };
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.PutConfigurationById(FacetName1, replacementConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(replacementConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Equal(ConfigurationEntryCount, convertedSavedConfig.Length);

        savedConfig = null;
        protectSecretsCalled = false;
        _callbackActionCalled = false;

        var newConfiguration = new SampleIdConfiguration { Index = "SomethingNew", Secret = "NoSecret", BoolProperty = true, IntProperty = 33 };
        result = managementController.PutConfigurationById(FacetName1, "NonMatchingId", JsonSerializer.Deserialize<JsonElement>(Serialize(newConfiguration), _jsonSerializerOptions));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.Null(savedConfig);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void DeleteConfiguration_EntriesFrom_Collection_Success()
    {
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(existingConfiguration);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.DeleteConfigurationById(FacetName1, existingConfiguration[1].Index);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);

        var savedConfigCollection = (SampleIdConfiguration[])savedConfig;

        Assert.Single(savedConfigCollection);
        Assert.Equal(existingConfiguration[0].Index, savedConfigCollection[0].Index);
        Assert.Equal(existingConfiguration[0].BoolProperty, savedConfigCollection[0].BoolProperty);
        Assert.Equal(existingConfiguration[0].IntProperty, savedConfigCollection[0].IntProperty);
        Assert.Equal(existingConfiguration[0].Secret, savedConfigCollection[0].Secret);
        Assert.True(_callbackActionCalled);

        var notifyConfiguration = (SampleIdConfiguration[])_configurationChangedArgs.NewValue;
        Assert.Equal(savedConfig, notifyConfiguration);

        var oldConfig = (SampleIdConfiguration[])_configurationChangedArgs.OldValue;
        Assert.Equal(existingConfiguration, oldConfig);

        _configurationChangedArgs = null;

        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(savedConfigCollection);

        result = managementController.DeleteConfigurationById(FacetName1, savedConfigCollection[0].Index);

        noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);
        Assert.NotNull(_configurationChangedArgs);

        var newEmptyConfiguration = (SampleIdConfiguration[])_configurationChangedArgs.NewValue;
        Assert.Empty(newEmptyConfiguration);
    }

    [Fact]
    public void DeleteConfiguration_Success()
    {
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(4);
        var configurationDeleted = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.DeleteConfiguration(ComponentId, FacetName1)).Callback((string id, string facet) => { configurationDeleted = true; });
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(existingConfiguration);

        var mockConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var managementRegistry = new RuntimeManagementRegistry(mockConfigurationRegistry.Object);
        managementRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var managementController = new SystemManagementController(mockConfigurationProtector.Object, testLogger, managementRegistry);
        var result = managementController.DeleteConfiguration(FacetName1);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(_configurationChangedArgs);

        var oldConfiguration = (SampleIdConfiguration[])_configurationChangedArgs.OldValue;

        Assert.Equal(existingConfiguration, oldConfiguration);
        Assert.Null(_configurationChangedArgs.NewValue);
        Assert.True(_callbackActionCalled);
        Assert.True(configurationDeleted);
    }

    private static T[] CreateConfigurationCollection<T>(int count, int skip = 0) where T : ISampleConfiguration, new()
    {
        if (count - skip < 1)
        {
            throw new InvalidOperationException();
        }

        var collection = new T[count - skip];
        for (int i = 0 + skip; i < count; i++)
        {
            collection[i - skip] = new T { Index = $"Index{i}", Secret = "Secret{i}", IntProperty = i };
        }

        return collection;
    }

    private static string Serialize<T>(T instance) => JsonSerializer.Serialize(instance, typeof(T), _jsonSerializerOptions);

    private void SampleCallbackAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        _callbackActionCalled = true;
        _configurationChangedArgs = configurationChangeEvent;
    }

    internal class SampleConfiguration : EdgeConfigurationBase, ISampleConfiguration
    {
        private const int MaxValue = 42;

        public string Index { get; set; }

        public int IntProperty { get; set; }

        public bool BoolProperty { get; set; }

        [Protected]
        public string Secret { get; set; }

        public override IEnumerable<ValidationResult> Validate()
        {
            if (IntProperty > MaxValue)
            {
                yield return new ValidationResult($"{nameof(IntProperty)} value cannot be more than {MaxValue}!");
            }
        }
    }

    internal class SampleIdConfiguration : EdgeConfigurationBase, ISampleConfiguration
    {
        private const int MaxValue = 42;

        [Id]
        public string Index { get; set; }

        public int IntProperty { get; set; }

        public bool BoolProperty { get; set; }

        [Protected]
        public string Secret { get; set; }

        public override IEnumerable<ValidationResult> Validate()
        {
            if (IntProperty > MaxValue)
            {
                yield return new ValidationResult($"{nameof(IntProperty)} value cannot be more than {MaxValue}!");
            }
        }
    }
}
