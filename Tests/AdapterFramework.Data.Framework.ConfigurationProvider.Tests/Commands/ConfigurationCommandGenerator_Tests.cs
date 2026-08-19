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
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.DataProtector;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests.Commands;

public class ConfigurationCommandGenerator_Tests
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = ConfigurationCommandHelper.SerializerOptions;

    private delegate void IsConfigurationValidCallback(object configuration, out ICollection<string> errors);
    private delegate void TryGetConfigurationCallback(string id, string facet, Type configType, out object configs, out ICollection<string> errors);
    private delegate void TrySaveConfigurationCallback(string id, string facet, object configs, out ICollection<string> errors);
    private delegate void TryMoveCorruptedFileCallback(string id, string facet, out string error);

    [Theory]
    [ClassData(typeof(TestDataGenerator))]
    public void ConfigurationCommandGenerator_Constructor_InvalidInput(IConfigurationProvider configurationProvider, string componentId, string facetName)
    {
        Assert.ThrowsAny<Exception>(() => new ConfigurationCommandGenerator(configurationProvider, componentId, facetName));
    }

    [Fact]
    public void GenerateConfigurationGetCommand_SimpleConfig_ConfigurationNotFound()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration)))
            .Returns(null);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var getCommand = configurationCommandGenerator.GenerateConfigurationGetCommand(typeof(TestConfiguration));

        Assert.True(getCommand.TryExecute(out var configuration, out var errors));
        Assert.Null(configuration);
        Assert.Empty(errors);
    }

    [Fact]
    public void GenerateConfigurationGetCommand_SimpleConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var testConfigToReturn = new TestConfiguration { Secret = "SecretString" };

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration)))
            .Returns(testConfigToReturn);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var getCommand = configurationCommandGenerator.GenerateConfigurationGetCommand(typeof(TestConfiguration));

        Assert.True(getCommand.TryExecute(out var configuration, out var errors));
        Assert.Equal(testConfigToReturn, configuration);
        Assert.Empty(errors);
    }

    [Fact]
    public void GenerateConfigurationGetCommand_IndexedConfig_Success()
    {
        var indexToReturn = "Desired";
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var entryToBeReturned = new TestConfiguration { Index = indexToReturn, IntProperty = 42 };
        var existingConfiguration = new[] { new TestConfiguration { Index = "NotInteresting", IntProperty = 2 }, entryToBeReturned };

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration[])))
                .Returns(existingConfiguration);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var getCommand = configurationCommandGenerator.GenerateConfigurationGetCommand(typeof(TestConfiguration[]), indexToReturn);

        Assert.True(getCommand.TryExecute(out var configuration, out var errors));
        Assert.Equal(entryToBeReturned, configuration);
        Assert.Empty(errors);
    }

    [Fact]
    public void GenerateConfigurationGetCommand_IndexedConfig_NotFound()
    {
        var indexToReturn = "Desired";
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var entryToBeReturned = new TestConfiguration { Index = "SomeIndex", IntProperty = 42 };
        var existingConfiguration = new[] { new TestConfiguration { Index = "NotInteresting", IntProperty = 2 }, entryToBeReturned };

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration[])))
            .Returns(existingConfiguration);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var getCommand = configurationCommandGenerator.GenerateConfigurationGetCommand(typeof(TestConfiguration[]), indexToReturn);

        Assert.False(getCommand.TryExecute(out var configuration, out var errors));
        Assert.Null(configuration);
        Assert.Empty(errors);
    }

    [Fact]
    public void GenerateConfigurationSetCommand_SimpleConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { Index = "SomeIndex", IntProperty = 42 };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = null;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryValidate(out errors));
        Assert.Empty(errors);
        Assert.Null(savedConfig);

        Assert.True(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.Null(setCommand.OldValue);
        Assert.NotNull(savedConfig);
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 42 }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 } };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration[]), _jsonSerializerOptions);
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = null;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryValidate(out errors));
        Assert.Empty(errors);
        Assert.Null(savedConfig);

        Assert.True(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.False(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.Null(setCommand.OldValue);
        Assert.NotNull(savedConfig);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public void GenerateConfigurationSetCommand_SimpleConfig_CustomValidation_InvalidConfig()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { Index = "SomeIndex", IntProperty = 52 };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = null;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.SaveConfiguration(componentId, facetName, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                savedConfig = config;
            });

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.False(setCommand.TryValidate(out errors));
        Assert.Equal(2, errors.Count);

        Assert.False(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Equal(2, errors.Count);
        Assert.True(protectSecretsCalled);
        Assert.Null(savedConfig);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public void GenerateConfigurationCreateCommand_SimpleConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { Index = "SomeIndex", IntProperty = 42 };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = null;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationCreateCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration), mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.Null(setCommand.OldValue);
        Assert.NotNull(savedConfig);
    }

    [Fact]
    public void GenerateConfigurationCreateCommand_CollectionConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 41 }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 21 }, new TestConfiguration { Index = "SomeIndex3", IntProperty = 12 } };
        var payloadToPost = new[] { new TestConfiguration { Index = "SomeIndex4", IntProperty = 42 }, new TestConfiguration { Index = "SomeIndex5", IntProperty = 22 }, new TestConfiguration { Index = "SomeIndex6", IntProperty = 14 } };
        var jsonSerialize = JsonSerializer.Serialize(payloadToPost, typeof(TestConfiguration[]), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = configurationToPersist;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationCreateCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.False(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);
        Assert.Equal(3, ((object[])setCommand.OldValue).Length);
        Assert.Equal(6, ((object[])setCommand.NewValue).Length);
        Assert.NotNull(savedConfig);
    }

    [Fact]
    public void GenerateConfigurationCreateCommand_CollectionConfig_CustomValidation_InvalidConfig()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 42 }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 }, new TestConfiguration { Index = "SomeIndex3", IntProperty = 11 } };
        var payloadToPost = new[] { new TestConfiguration { Index = "SomeIndex4", IntProperty = 42 }, new TestConfiguration { Index = "SomeIndex5", IntProperty = 22 }, new TestConfiguration { Index = "SomeIndex6", IntProperty = 14 } };
        var jsonSerialize = JsonSerializer.Serialize(payloadToPost, typeof(TestConfiguration[]), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = configurationToPersist;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.SaveConfiguration(componentId, facetName, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                savedConfig = config;
            });

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationCreateCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.False(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Equal(2, errors.Count);
        Assert.False(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);

        // values should be prepared but not saved
        Assert.Equal(3, ((object[])setCommand.OldValue).Length);
        Assert.Equal(6, ((object[])setCommand.NewValue).Length);
        Assert.Null(savedConfig);
    }

    [Fact]
    public void GenerateConfigurationCreateCommand_CollectionConfigSingleElement_PatternGenerated_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        var generatedIndex = "Hello!";
        var expectedProtectedValue = "{{ComponentId.FacetName.Hello!.Secret}}";
        object savedConfig = null;
        var protectSecretsCalled = false;
        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { IntProperty = 42, Secret = "1234", };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);

        var testLogger = new TestLogger();
        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, testLogger, null);
        var configurationProtector = new ConfigurationProtector(secretsManager);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = null;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors))
            .Callback(new IsConfigurationValidCallback((object configuration, out ICollection<string> errorMessages) =>
            {
                errorMessages = null;
                if (configuration is TestConfiguration[] arrayConfiguration)
                {
                    arrayConfiguration[0].Index = generatedIndex;
                    return;
                }

                ((TestConfiguration)configuration).Index = generatedIndex;
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var createCommand = configurationCommandGenerator.GenerateConfigurationCreateCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), configurationProtector, ValidateTestConfiguration);

        Assert.True(createCommand.TryExecute(testLogger, out errors));
        Assert.Empty(errors);
        Assert.False(protectSecretsCalled);
        Assert.NotNull(createCommand.NewValue);
        Assert.Null(createCommand.OldValue);
        Assert.NotNull(savedConfig);
        Assert.True(((TestConfiguration[])createCommand.NewValue)[0].Secret == expectedProtectedValue);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(1));
    }

    [Fact]
    public void GenerateConfigurationPatchCommand_SimpleConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();
        object savedConfig = null;

        var existingConfiguration = new TestConfiguration { Index = "SomeIndex", IntProperty = 40 };
        var patches = new Patches { IntProperty = 42, Secret = "HelloWorld" };
        var jsonSerialize = JsonSerializer.Serialize(patches, typeof(Patches), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(patches.Secret, It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; }).Returns(patches.Secret);

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration)))
            .Returns(existingConfiguration);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var patchCommand = configurationCommandGenerator.GenerateConfigurationPatchCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => patchCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => patchCommand.OldValue);

        Assert.True(patchCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.True(protectSecretsCalled);

        var typedSavedConfiguration = (TestConfiguration)savedConfig;
        Assert.Equal(existingConfiguration.Index, typedSavedConfiguration.Index);
        Assert.Equal(existingConfiguration.BoolProperty, typedSavedConfiguration.BoolProperty);
        Assert.Equal(patches.Secret, typedSavedConfiguration.Secret);
        Assert.Equal(patches.IntProperty, typedSavedConfiguration.IntProperty);

        // validate that previous configuration is unchanged
        var typedOldConfiguration = (TestConfiguration)patchCommand.OldValue;
        Assert.Equal(existingConfiguration.BoolProperty, typedOldConfiguration.BoolProperty);
        Assert.Equal(existingConfiguration.Secret, typedOldConfiguration.Secret);
        Assert.Equal(existingConfiguration.IntProperty, typedOldConfiguration.IntProperty);

        Assert.NotNull(patchCommand.NewValue);
        Assert.NotNull(patchCommand.OldValue);
    }

    [Fact]
    public void GenerateConfigurationPatchCommand_CollectionConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();
        object savedConfig = null;

        TestConfiguration[] existingConfiguration =
        {
            new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
            new TestConfiguration { Index = "SomeIndex2", IntProperty = 12, BoolProperty = true },
            new TestConfiguration { Index = "SomeIndex3", IntProperty = 14 },
        };

        var patches = new Patches { IntProperty = 42, Secret = "HelloWorld" };
        var jsonSerialize = JsonSerializer.Serialize(patches, typeof(Patches), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(patches.Secret, It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; }).Returns(patches.Secret);

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration[])))
            .Returns(new[]
            {
                new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
                new TestConfiguration { Index = "SomeIndex2", IntProperty = 12, BoolProperty = true },
                new TestConfiguration { Index = "SomeIndex3", IntProperty = 14 },
            });

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var patchCommand = configurationCommandGenerator.GenerateConfigurationPatchCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration[]), existingConfiguration[2].Index, mockConfigurationProtector.Object, ValidateTestConfiguration);
        Assert.True(patchCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.True(protectSecretsCalled);

        // validate saved configuration
        var typedSavedConfiguration = (TestConfiguration[])savedConfig;
        Assert.Equal(3, typedSavedConfiguration.Length);
        Assert.Equal(existingConfiguration[2].Index, typedSavedConfiguration[2].Index);
        Assert.Equal(existingConfiguration[2].BoolProperty, typedSavedConfiguration[2].BoolProperty);
        Assert.Equal(patches.Secret, typedSavedConfiguration[2].Secret);
        Assert.Equal(patches.IntProperty, typedSavedConfiguration[2].IntProperty);

        // validate old configuration
        var typedOldConfiguration = (TestConfiguration[])patchCommand.OldValue;
        Assert.Equal(existingConfiguration[2].Secret, typedOldConfiguration[2].Secret);
        Assert.Equal(existingConfiguration[2].IntProperty, typedOldConfiguration[2].IntProperty);
        Assert.Equal(savedConfig, patchCommand.NewValue);
    }

    [Fact]
    public void GenerateConfigurationPatchCommand_CollectionConfigNoId_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();
        object savedConfig = null;

        var targetIndex1 = "SomeIndex3";
        var targetIndex2 = "SomeIndex2";

        TestConfiguration[] existingConfiguration =
        {
            new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
            new TestConfiguration { Index = targetIndex2, IntProperty = 12, BoolProperty = true },
            new TestConfiguration { Index = targetIndex1, IntProperty = 14 },
        };

        var patches1 = new PatchesWithIndex { IntProperty = 42, Secret = "HelloWorld", Index = targetIndex1 };
        var patches2 = new PatchesWithIndex { IntProperty = 12, Secret = "GoodbyeWorld", Index = targetIndex2 };
        var patchesArray = new[] { patches1, patches2 };
        var jsonSerialize = JsonSerializer.Serialize(patchesArray, typeof(PatchesWithIndex[]), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(patches1.Secret, It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; }).Returns(patches1.Secret);
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(patches2.Secret, It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; }).Returns(patches2.Secret);

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration[])))
            .Returns(new[]
            {
                new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
                new TestConfiguration { Index = targetIndex2, IntProperty = 12, BoolProperty = true },
                new TestConfiguration { Index = targetIndex1, IntProperty = 14 },
            });

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var patchCommand = configurationCommandGenerator.GenerateConfigurationPatchCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration[]), null, mockConfigurationProtector.Object, ValidateTestConfiguration);
        Assert.True(patchCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.True(protectSecretsCalled);

        // Validate saved configuration
        var typedSavedConfiguration = (TestConfiguration[])savedConfig;
        Assert.Equal(3, typedSavedConfiguration.Length);

        Assert.Equal(existingConfiguration[2].Index, typedSavedConfiguration[2].Index);
        Assert.Equal(existingConfiguration[2].BoolProperty, typedSavedConfiguration[2].BoolProperty);
        Assert.Equal(patches1.Secret, typedSavedConfiguration[2].Secret);
        Assert.Equal(patches1.IntProperty, typedSavedConfiguration[2].IntProperty);

        Assert.Equal(existingConfiguration[1].Index, typedSavedConfiguration[1].Index);
        Assert.Equal(existingConfiguration[1].BoolProperty, typedSavedConfiguration[1].BoolProperty);
        Assert.Equal(patches2.Secret, typedSavedConfiguration[1].Secret);
        Assert.Equal(patches2.IntProperty, typedSavedConfiguration[1].IntProperty);

        // Validate other properties untouched
        Assert.Equal(typedSavedConfiguration[0].Index, existingConfiguration[0].Index);
        Assert.Equal(typedSavedConfiguration[0].IntProperty, existingConfiguration[0].IntProperty);
        Assert.Equal(typedSavedConfiguration[0].Secret, existingConfiguration[0].Secret);

        // Validate old configuration
        var typedOldConfiguration = (TestConfiguration[])patchCommand.OldValue;

        Assert.Equal(existingConfiguration[2].Secret, typedOldConfiguration[2].Secret);
        Assert.Equal(existingConfiguration[2].IntProperty, typedOldConfiguration[2].IntProperty);

        Assert.Equal(existingConfiguration[1].Secret, typedOldConfiguration[1].Secret);
        Assert.Equal(existingConfiguration[1].IntProperty, typedOldConfiguration[1].IntProperty);

        Assert.Equal(savedConfig, patchCommand.NewValue);
    }

    [Fact]
    public void GenerateConfigurationPatchCommand_CollectionConfigNoId_NullId_Failure()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();
        object savedConfig = null;

        var targetIndex = "SomeIndex3";

        TestConfiguration[] existingConfiguration =
        {
            new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
            new TestConfiguration { Index = "SomeIndex2", IntProperty = 12, BoolProperty = true },
            new TestConfiguration { Index = targetIndex, IntProperty = 14 },
        };

        var patches = new PatchesWithIndex { IntProperty = 42, Secret = "HelloWorld" };
        var patchesArray = new[] { patches };
        var jsonSerialize = JsonSerializer.Serialize(patchesArray, typeof(PatchesWithIndex[]), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(patches.Secret))
            .Callback(() => { protectSecretsCalled = true; }).Returns(patches.Secret);

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration[])))
            .Returns(new[]
            {
                new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
                new TestConfiguration { Index = "SomeIndex2", IntProperty = 12, BoolProperty = true },
                new TestConfiguration { Index = targetIndex, IntProperty = 14 },
            });

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.SaveConfiguration(componentId, facetName, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                savedConfig = config;
            });

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var patchCommand = configurationCommandGenerator.GenerateConfigurationPatchCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration[]), null, mockConfigurationProtector.Object, ValidateTestConfiguration);
        Assert.False(patchCommand.TryExecute(new TestLogger(), out errors));
        Assert.NotEmpty(errors);
        Assert.False(protectSecretsCalled);

        var typedSavedConfiguration = (TestConfiguration[])savedConfig;
        Assert.Null(typedSavedConfiguration);

        // Validate properties untouched
        var typedOldConfiguration = (TestConfiguration[])patchCommand.OldValue;
        Assert.Equal(typedOldConfiguration[0].Index, existingConfiguration[0].Index);
        Assert.Equal(typedOldConfiguration[1].Index, existingConfiguration[1].Index);
        Assert.Equal(typedOldConfiguration[0].IntProperty, existingConfiguration[0].IntProperty);
        Assert.Equal(typedOldConfiguration[1].IntProperty, existingConfiguration[1].IntProperty);
        Assert.Equal(typedOldConfiguration[0].Secret, existingConfiguration[0].Secret);
        Assert.Equal(typedOldConfiguration[1].Secret, existingConfiguration[1].Secret);
        Assert.Equal(existingConfiguration[2].Secret, typedOldConfiguration[2].Secret);
        Assert.Equal(existingConfiguration[2].IntProperty, typedOldConfiguration[2].IntProperty);
    }

    [Fact]
    public void GenerateConfigurationPatchCommand_CollectionConfigNoId_MultipleError_Failure()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();
        object savedConfig = null;

        var targetIndex = "SomeIndex3";

        TestConfiguration[] existingConfiguration =
        {
            new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
            new TestConfiguration { Index = "SomeIndex2", IntProperty = 12, BoolProperty = true },
            new TestConfiguration { Index = targetIndex, IntProperty = 14 },
        };

        var patches = new PatchesWithIndex { IntProperty = 42, Secret = "HelloWorld" };
        var patchesArray = new[]
        {
            new PatchesWithIndex { IntProperty = 42, Secret = "HelloWorld" },
            new PatchesWithIndex { IntProperty = 12, Secret = "GoodbyeWorld", Index = "SomethingCrazy" },
        };
        var jsonSerialize = JsonSerializer.Serialize(patchesArray, typeof(PatchesWithIndex[]), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(patches.Secret))
            .Callback(() => { protectSecretsCalled = true; }).Returns(patches.Secret);

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration[])))
            .Returns(new[]
            {
                new TestConfiguration { Index = "SomeIndex1", IntProperty = 40 },
                new TestConfiguration { Index = "SomeIndex2", IntProperty = 12, BoolProperty = true },
                new TestConfiguration { Index = targetIndex, IntProperty = 14 },
            });

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.SaveConfiguration(componentId, facetName, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                savedConfig = config;
            });

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var patchCommand = configurationCommandGenerator.GenerateConfigurationPatchCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration[]), null, mockConfigurationProtector.Object, ValidateTestConfiguration);
        Assert.False(patchCommand.TryExecute(new TestLogger(), out errors));
        Assert.Equal(2, errors.Count);
        Assert.False(protectSecretsCalled);

        var typedSavedConfiguration = (TestConfiguration[])savedConfig;
        Assert.Null(typedSavedConfiguration);

        // Validate properties untouched
        var typedOldConfiguration = (TestConfiguration[])patchCommand.OldValue;
        Assert.Equal(typedOldConfiguration[0].Index, existingConfiguration[0].Index);
        Assert.Equal(typedOldConfiguration[1].Index, existingConfiguration[1].Index);
        Assert.Equal(typedOldConfiguration[0].IntProperty, existingConfiguration[0].IntProperty);
        Assert.Equal(typedOldConfiguration[1].IntProperty, existingConfiguration[1].IntProperty);
        Assert.Equal(typedOldConfiguration[0].Secret, existingConfiguration[0].Secret);
        Assert.Equal(typedOldConfiguration[1].Secret, existingConfiguration[1].Secret);
        Assert.Equal(existingConfiguration[2].Secret, typedOldConfiguration[2].Secret);
        Assert.Equal(existingConfiguration[2].IntProperty, typedOldConfiguration[2].IntProperty);
    }

    [Fact]
    public void GenerateConfigurationDeleteCommand_SimpleConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        var existingConfiguration = new TestConfiguration { Index = "SomeIndex", IntProperty = 42 };
        var configurationDeleted = false;

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration)))
            .Returns(existingConfiguration);

        mockConfigProvider.Setup(cp => cp.DeleteConfiguration(componentId, facetName))
            .Callback(() => configurationDeleted = true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var deleteCommand = configurationCommandGenerator.GenerateConfigurationDeleteCommand(typeof(TestConfiguration), null, null);

        Assert.Throws<InvalidOperationException>(() => deleteCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => deleteCommand.OldValue);

        Assert.True(deleteCommand.TryExecute(new TestLogger(), out var errors));
        Assert.Empty(errors);
        Assert.True(configurationDeleted);
        Assert.Null(deleteCommand.NewValue);
        Assert.NotNull(deleteCommand.OldValue);
    }

    [Fact]
    public void GenerateConfigurationDeleteCommand_SimpleConfig_CustomValidation_Fails()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        var existingConfiguration = new TestConfiguration { Index = "SomeIndex", IntProperty = 42 };
        var configurationDeleted = false;

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration)))
            .Returns(existingConfiguration);

        mockConfigProvider.Setup(cp => cp.DeleteConfiguration(componentId, facetName))
            .Callback(() => configurationDeleted = true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var deleteCommand = configurationCommandGenerator.GenerateConfigurationDeleteCommand(typeof(TestConfiguration), null, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => deleteCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => deleteCommand.OldValue);

        Assert.False(deleteCommand.TryExecute(new TestLogger(), out var errors));
        Assert.NotEmpty(errors);
        Assert.False(configurationDeleted);
        Assert.Null(deleteCommand.NewValue);
        Assert.NotNull(deleteCommand.OldValue);
    }

    [Fact]
    public void GenerateConfigurationDeleteCommand_CollectionConfig_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        TestConfiguration[] existingConfiguration =
        {
            new TestConfiguration { Index = "SomeIndex", IntProperty = 42 },
            new TestConfiguration { Index = "SomeIndex2", IntProperty = 12 },
        };

        object savedConfig = null;
        ICollection<string> errors = new List<string>();

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        mockConfigProvider.Setup(cp => cp.GetConfiguration(componentId, facetName, typeof(TestConfiguration[])))
            .Returns(existingConfiguration);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
        .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
        {
            savedConfig = configs;
            errorMessages = new List<string>();
        })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var deleteCommand = configurationCommandGenerator.GenerateConfigurationDeleteCommand(typeof(TestConfiguration[]), existingConfiguration[0].Index, ValidateTestConfiguration);

        Assert.True(deleteCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.NotNull(savedConfig);
        var typedSavedConfiguration = (TestConfiguration[])savedConfig;
        Assert.Single(typedSavedConfiguration);

        var typedOldConfiguration = (TestConfiguration[])deleteCommand.OldValue;
        Assert.Equal(2, typedOldConfiguration.Length);
        Assert.Equal(existingConfiguration[0].Index, typedOldConfiguration[0].Index);
        Assert.Equal(existingConfiguration[1].Index, typedOldConfiguration[1].Index);
    }

    [Fact]
    public void GenerateConfigurationSetCollectionCommand_InvalidConfiguration_ErrorMessages()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        ICollection<string> errors = new List<string>
        {
            EdgeSystemConstants.OriginalConfigurationInvalidMessage,
        };

        TestConfiguration[] newConfiguration =
        {
            new TestConfiguration { Index = "SomeIndex", IntProperty = 42 },
            new TestConfiguration { Index = "SomeIndex2", IntProperty = 12 },
        };

        var jsonSerialize = JsonSerializer.Serialize(newConfiguration, typeof(TestConfiguration[]), _jsonSerializerOptions);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = null;
                errs = errors;
            })).Returns(false);

        var singleError = EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage;
        mockConfigProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out singleError))
            .Callback(new TryMoveCorruptedFileCallback((string id, string facet, out string error) =>
            {
                error = singleError;
            })).Returns(false);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var mockConfigProtector = new Mock<IConfigurationProtector>();
        mockConfigProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { });

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration[]), componentId, mockConfigProtector.Object, ValidateTestConfiguration);

        var logger = new TestLogger();
        Assert.False(setCommand.TryExecute(logger, out _));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.OriginalConfigurationInvalidMessage));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage));
        mockConfigProvider.Verify(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string>.IsAny), Times.Once);
    }

    [Fact]
    public void GenerateConfigurationSetSingleCommand_InvalidConfiguration_ErrorMessages()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        ICollection<string> errors = new List<string>
        {
            EdgeSystemConstants.OriginalConfigurationInvalidMessage,
        };

        var newConfiguration = new TestConfiguration { Index = "SomeIndex", IntProperty = 42 };

        var jsonSerialize = JsonSerializer.Serialize(newConfiguration, typeof(TestConfiguration), _jsonSerializerOptions);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = null;
                errs = errors;
            })).Returns(false);

        var singleError = EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage;
        mockConfigProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out singleError))
            .Callback(new TryMoveCorruptedFileCallback((string id, string facet, out string error) =>
            {
                error = singleError;
            })).Returns(false);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var mockConfigProtector = new Mock<IConfigurationProtector>();
        mockConfigProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { });

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration), componentId, mockConfigProtector.Object, ValidateTestConfiguration);

        var logger = new TestLogger();
        Assert.False(setCommand.TryExecute(logger, out _));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.OriginalConfigurationInvalidMessage));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage));
        mockConfigProvider.Verify(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string>.IsAny), Times.Once);
    }

    [Fact]
    public void GenerateConfigurationCreateCommand_Single_InvalidConfiguration_ErrorMessage()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        ICollection<string> errors = new List<string>
        {
            EdgeSystemConstants.OriginalConfigurationInvalidMessage,
        };

        var newConfiguration = new TestConfiguration { Index = "SomeIndex", IntProperty = 42 };

        var jsonSerialize = JsonSerializer.Serialize(newConfiguration, typeof(TestConfiguration), _jsonSerializerOptions);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = null;
                errs = errors;
            })).Returns(false);

        var singleError = EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage;
        mockConfigProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out singleError))
            .Callback(new TryMoveCorruptedFileCallback((string id, string facet, out string error) =>
            {
                error = singleError;
            })).Returns(false);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var mockConfigProtector = new Mock<IConfigurationProtector>();
        mockConfigProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { });

        var createCommand = configurationCommandGenerator.GenerateConfigurationCreateCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration), mockConfigProtector.Object, ValidateTestConfiguration);

        var logger = new TestLogger();
        Assert.False(createCommand.TryExecute(logger, out _));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.OriginalConfigurationInvalidMessage));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage));
        mockConfigProvider.Verify(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string>.IsAny), Times.Once);
    }

    [Fact]
    public void GenerateConfigurationCreateCommand_Collection_InvalidConfiguration_ErrorMessage()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        ICollection<string> errors = new List<string>
        {
            EdgeSystemConstants.OriginalConfigurationInvalidMessage,
        };

        TestConfiguration[] newConfiguration =
        {
            new TestConfiguration { Index = "SomeIndex", IntProperty = 42 },
            new TestConfiguration { Index = "SomeIndex2", IntProperty = 12 },
        };

        var jsonSerialize = JsonSerializer.Serialize(newConfiguration, typeof(TestConfiguration[]), _jsonSerializerOptions);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = null;
                errs = errors;
            })).Returns(false);

        var singleError = EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage;
        mockConfigProvider.Setup(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out singleError))
            .Callback(new TryMoveCorruptedFileCallback((string id, string facet, out string error) =>
            {
                error = singleError;
            })).Returns(false);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var mockConfigProtector = new Mock<IConfigurationProtector>();
        mockConfigProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { });

        var createCommand = configurationCommandGenerator.GenerateConfigurationCreateCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration[]), mockConfigProtector.Object, ValidateTestConfiguration);

        var logger = new TestLogger();
        Assert.False(createCommand.TryExecute(logger, out _));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.OriginalConfigurationInvalidMessage));
        Assert.True(logger.ContainsMessage(EdgeSystemConstants.FailedToMoveInvalidConfigurationMessage));
        mockConfigProvider.Verify(cp => cp.TryMoveCorruptedConfiguration(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<string>.IsAny), Times.Once);
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionConfigRetainSecretNoConfig_Fail()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 42, Secret = ConfigurationProtector.MaskedValue }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 } };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration[]), _jsonSerializerOptions);
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = null;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryValidate(out errors));
        Assert.Empty(errors);
        Assert.Null(savedConfig);

        Assert.False(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Single(errors);
        Assert.False(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.Null(setCommand.OldValue);
        Assert.Null(savedConfig);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionConfigRetainSecretOtherConfigExists_Fail()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 42, Secret = ConfigurationProtector.MaskedValue }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 } };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration[]), _jsonSerializerOptions);
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = new[] { new TestConfiguration { Index = "SomeIndex3", IntProperty = 42 }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 } };
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryValidate(out errors));
        Assert.Empty(errors);
        Assert.Null(savedConfig);

        Assert.False(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Single(errors);
        Assert.False(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);
        Assert.Null(savedConfig);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionConfigSingleElementCollectionPutRetainSecretConfigExists_Fail()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { Index = "SomeIndex1", IntProperty = 42, Secret = ConfigurationProtector.MaskedValue };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = new[] { new TestConfiguration { Index = "SomeIndex3", IntProperty = 42 }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 } };
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), "SomeIndex1", mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.False(setCommand.TryValidate(out errors));
        Assert.Single(errors);
        Assert.Null(savedConfig);

        Assert.False(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Single(errors);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);
        Assert.Null(savedConfig);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionValidationFail_ProtectSecrets_Fail()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";

        var newConfiguration = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 42, Secret = ConfigurationProtector.MaskedValue }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 } };

        var jsonSerialize = JsonSerializer.Serialize(newConfiguration, typeof(TestConfiguration[]), _jsonSerializerOptions);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out It.Ref<ICollection<string>>.IsAny)).Returns(false);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var mockConfigProtector = new Mock<IConfigurationProtector>();
        mockConfigProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { });
        mockConfigProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { });
        var createCommand = configurationCommandGenerator.GenerateConfigurationCreateCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions), typeof(TestConfiguration[]), mockConfigProtector.Object, ValidateTestConfiguration);

        var logger = new TestLogger();
        mockConfigProtector.Verify(cp => cp.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        mockConfigProtector.Verify(cp => cp.ProtectSecrets(ref It.Ref<object[]>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionConfigRetainSecretOldConfigExists_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        var password = "Hello";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 42, Secret = ConfigurationProtector.MaskedValue }, new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 } };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration[]), _jsonSerializerOptions);
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration[]), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = new[] { new TestConfiguration { Index = "SomeIndex1", IntProperty = 42, Secret = password } };
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryValidate(out errors));
        Assert.Empty(errors);
        Assert.Null(savedConfig);

        Assert.True(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.False(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);
        Assert.NotNull(savedConfig);
        Assert.True(((TestConfiguration[])setCommand.NewValue)[0].Secret == password);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionConfigSingleElement_PatternGenerated_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        var password = "Hello";
        var generatedIndex = "Hello!";
        var expectedProtectedValue = "{{ComponentId.FacetName.Hello!.Secret}}";
        object savedConfig = null;
        ICollection<string> errors = new List<string>();

        var configurationToPersist = new[]
        {
                new TestConfiguration { IntProperty = 42, Secret = "1234" },
                new TestConfiguration { Index = "SomeIndex2", IntProperty = 22 },
        };

        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration[]), _jsonSerializerOptions);

        var testLogger = new TestLogger();
        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, testLogger, null);
        var configurationProtector = new ConfigurationProtector(secretsManager);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = new[] { new TestConfiguration { IntProperty = 42, Secret = password } };
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors))
            .Callback(new IsConfigurationValidCallback((object configuration, out ICollection<string> errorMessages) =>
            {
                errorMessages = null;
                ((TestConfiguration[])configuration)[0].Index = generatedIndex;
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), null, configurationProtector, ValidateTestConfiguration);

        Assert.True(setCommand.TryExecute(testLogger, out errors));
        Assert.Empty(errors);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);
        Assert.NotNull(savedConfig);
        Assert.True(((TestConfiguration[])setCommand.NewValue)[0].Secret == expectedProtectedValue);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(1));
    }

    [Fact]
    public void GenerateConfigurationSetCommand_CollectionConfigSingleElement_IdSupplied_PatternGenerated_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        var suppliedIndex = "Bam!";
        var expectedProtectedValue = "{{ComponentId.FacetName.Bam!.Secret}}";
        object savedConfig = null;
        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { IntProperty = 42, Secret = "1234" };

        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);

        var testLogger = new TestLogger();
        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, testLogger, null);
        var configurationProtector = new ConfigurationProtector(secretsManager);

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = new[] { new TestConfiguration { Index = "Existing!", IntProperty = 4, Secret = suppliedIndex } };
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors))
            .Callback(new IsConfigurationValidCallback((object configuration, out ICollection<string> errorMessages) =>
            {
                errorMessages = null;
                if (string.IsNullOrEmpty(((TestConfiguration[])configuration)[0].Index))
                {
                    ((TestConfiguration[])configuration)[0].Index = "SomethingWrong!";
                }
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration[]), suppliedIndex, configurationProtector, ValidateTestConfiguration);

        Assert.True(setCommand.TryExecute(testLogger, out errors));
        Assert.Empty(errors);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);
        Assert.NotNull(savedConfig);
        Assert.True(((TestConfiguration[])setCommand.NewValue)[1].Secret == expectedProtectedValue);
        Assert.True(((TestConfiguration[])setCommand.NewValue)[1].Index == suppliedIndex);
        mockConfigProvider.Verify(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out It.Ref<object>.IsAny, out It.Ref<ICollection<string>>.IsAny), Times.Exactly(1));
    }

    [Fact]
    public void GenerateConfigurationSetCommand_SimpleConfigRetainSecret_Success()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;
        var password = "hello";

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { Index = "SomeIndex", IntProperty = 42, Secret = ConfigurationProtector.MaskedValue };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = new TestConfiguration { Index = "SomeIndex", Secret = password };
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryValidate(out errors));
        Assert.Empty(errors);
        Assert.Null(savedConfig);

        Assert.True(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Empty(errors);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.NotNull(setCommand.OldValue);
        Assert.NotNull(savedConfig);
        Assert.True(((TestConfiguration)setCommand.NewValue).Secret == password);
    }

    [Fact]
    public void GenerateConfigurationSetCommand_SimpleConfigRetainSecret_Fail()
    {
        var componentId = "ComponentId";
        var facetName = "FacetName";
        object savedConfig = null;
        var protectSecretsCalled = false;

        ICollection<string> errors = new List<string>();

        var configurationToPersist = new TestConfiguration { Index = "SomeIndex", IntProperty = 42, Secret = ConfigurationProtector.MaskedValue };
        var jsonSerialize = JsonSerializer.Serialize(configurationToPersist, typeof(TestConfiguration), _jsonSerializerOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.ProtectSecrets(ref It.Ref<object>.IsAny, typeof(TestConfiguration), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => { protectSecretsCalled = true; });

        var mockConfigProvider = new Mock<IConfigurationProvider>();

        object configOutputStructure = null;
        mockConfigProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type>(), out configOutputStructure, out errors))
            .Callback(new TryGetConfigurationCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errorMessages) =>
            {
                configs = null;
                errorMessages = new List<string>();
            })).Returns(true);

        mockConfigProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        mockConfigProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), out errors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object configs, out ICollection<string> errorMessages) =>
            {
                savedConfig = configs;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationCommandGenerator = new ConfigurationCommandGenerator(mockConfigProvider.Object, componentId, facetName);

        var setCommand = configurationCommandGenerator.GenerateConfigurationSetCommand(JsonSerializer.Deserialize<JsonElement>(jsonSerialize, _jsonSerializerOptions),
            typeof(TestConfiguration), null, mockConfigurationProtector.Object, ValidateTestConfiguration);

        Assert.Throws<InvalidOperationException>(() => setCommand.NewValue);
        Assert.Throws<InvalidOperationException>(() => setCommand.OldValue);

        Assert.True(setCommand.TryValidate(out errors));
        Assert.Empty(errors);
        Assert.Null(savedConfig);

        Assert.False(setCommand.TryExecute(new TestLogger(), out errors));
        Assert.Single(errors);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(setCommand.NewValue);
        Assert.Null(setCommand.OldValue);
        Assert.Null(savedConfig);
    }

    private ICollection<string> ValidateTestConfiguration(ConfigurationChangedEventArgs configurations)
    {
        var errors = new List<string>();

        if (configurations.NewValue != null)
        {
            if (configurations.NewValue is TestConfiguration typedNewConfiguration)
            {
                if (typedNewConfiguration.IntProperty > 50 && typedNewConfiguration.BoolProperty == false)
                {
                    errors.Add("Invalid arguments combination.");
                    errors.Add("Please try again.");
                }
            }
            else if (configurations.NewValue is TestConfiguration[] typedNewConfigurations)
            {
                var existingNumbers = new HashSet<int>();
                foreach (var newConfigurationEntry in typedNewConfigurations)
                {
                    if (!existingNumbers.Contains(newConfigurationEntry.IntProperty))
                    {
                        existingNumbers.Add(newConfigurationEntry.IntProperty);
                    }
                    else
                    {
                        errors.Add($"{nameof(TestConfiguration.IntProperty)} must be unique! Duplicate value: {newConfigurationEntry.IntProperty}");
                    }
                }
            }
        }
        else
        {
            errors.Add("Entire configuration cannot be deleted.");
        }

        return errors;
    }
}

#region TestConfiguration Class

#pragma warning disable SA1402 // File may only contain a single type
internal class TestConfiguration : EdgeConfigurationBase
#pragma warning restore SA1402 // File may only contain a single type
{
    [Id]
    public string Index { get; set; }
    public int IntProperty { get; set; }
    [Protected]
    public string Secret { get; set; }
    public bool BoolProperty { get; set; }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (string.IsNullOrEmpty(Index))
        {
            Index = Guid.NewGuid().ToString();
        }

        if (IntProperty > 50)
        {
            yield return new ValidationResult($"{nameof(IntProperty)} cannot be bigger than 50.");
        }
    }
}

#pragma warning disable SA1402 // File may only contain a single type
internal class Patches
#pragma warning restore SA1402 // File may only contain a single type
{
    public int IntProperty { get; set; }
    public string Secret { get; set; }
}

#pragma warning disable SA1402 // File may only contain a single type
internal class PatchesWithIndex
#pragma warning restore SA1402 // File may only contain a single type
{
    public int IntProperty { get; set; }
    public string Secret { get; set; }
    public string Index { get; set; }
}

#endregion

#region Test Data Class

#pragma warning disable SA1402 // File may only contain a single type
internal class TestDataGenerator : IEnumerable<object[]>
#pragma warning restore SA1402 // File may only contain a single type
{
    private static readonly Mock<IConfigurationProvider> _mockConfigurationProvider = new Mock<IConfigurationProvider>();

    private readonly IEnumerable<object[]> _data = new List<object[]>
    {
        new object[] { null, "TestId", "TestConfigName" },
        new object[] { _mockConfigurationProvider.Object, null, "TestConfigName" },
        new object[] { _mockConfigurationProvider.Object, string.Empty, "TestConfigName" },
        new object[] { _mockConfigurationProvider.Object, " ", "TestConfigName" },
        new object[] { _mockConfigurationProvider.Object, "TestId", null },
        new object[] { _mockConfigurationProvider.Object, "TestId", string.Empty },
        new object[] { _mockConfigurationProvider.Object, "TestId", " " },
    };

    public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

#endregion
