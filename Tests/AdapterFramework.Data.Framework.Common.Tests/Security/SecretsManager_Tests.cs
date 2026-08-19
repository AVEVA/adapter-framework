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
using System.Globalization;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Common.Security;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests;

public class SecretsManager_Tests
{
    [Fact]
    public void SecretsManager_InitializeTest()
    {
        var configProvider = TestUtilities.GetMockConfigurationProvider();
        var logger = new Mock<ILogger>();
        var getConfigCalled = false;
        ICollection<string> errors = new List<string>();

        var managedSecretConfigurations = new[]
        {
            new ManagedSecretConfiguration() { Id = "a", Value = "b" },
            new ManagedSecretConfiguration() { Id = "c", Value = "d" },
        };

        configProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(),
                out managedSecretConfigurations, out errors))
            .Callback(() => getConfigCalled = true).Returns(true);

        TestUtilities.CreateSecretsManagerInstance(null, logger.Object, configProvider.Object);

        Assert.True(getConfigCalled);
    }

    [Fact]
    public void SecretsManager_BadConfigInitializeTest()
    {
        var configProvider = TestUtilities.GetMockConfigurationProvider();
        var logger = new TestLogger();
        ICollection<string> errors = new List<string>();

        ManagedSecretConfiguration[] managedSecretConfigurations = null;
        errors.Add(It.IsAny<string>());

        configProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(),
            out managedSecretConfigurations, out errors)).Returns(false);

        TestUtilities.CreateSecretsManagerInstance(null, logger, configProvider.Object);

        Assert.NotEmpty(logger.GetLogMessages());
    }

    [Fact]
    public void SecretsManager_ConfigurationChangedCallbackTest()
    {
        var logger = new TestLogger();
        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, logger, null);
        var newValue = new[]
        {
            new ManagedSecretConfiguration() { Id = "{{Component.Facet.PropertyName}}", Value = "asldkfskjf" },
        };
        var args = new ConfigurationChangedEventArgs(null, newValue);
        secretsManager.ConfigurationChangedAction(args);
        
        // Just ensure nothing throws
    }

    [Theory]
    [InlineData("{{Component.Facet.PropertyName}}", true)]
    [InlineData("{{Component.Facet.ConfigurationId.PropertyName}}", true)]
    [InlineData("{{not.existing.id}}", false)]
    [InlineData("someEncryptedSecret_dkjdfhskljdhfaksjd", true)]
    public void SecretsManager_UnprotectTests_AlreadyFinalized(string id, bool isValid)
    {
        var protectedString = TestUtilities.GenerateRandomString(16);
        var secretString = "pswd";

        var logger = new TestLogger();
        var configProvider = TestUtilities.GetMockConfigurationProvider();
        var dataProtector = new Mock<IInternalDataProtector>();
        ICollection<string> errors = new List<string>();

        var managedSecretConfigurations = new[]
        {
            new ManagedSecretConfiguration() { Id = "Component.Facet.PropertyName", Value = protectedString },
            new ManagedSecretConfiguration()
            {
                Id = "Component.Facet.ConfigurationId.PropertyName", Value = protectedString,
            },
        };
        configProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(),
            out managedSecretConfigurations, out errors)).Returns(true);
        dataProtector.Setup(dp => dp.Unprotect(It.IsAny<string>())).Returns(secretString);

        var secretsManager =
            TestUtilities.CreateSecretsManagerInstance(dataProtector.Object, logger, configProvider.Object);
        var secret = secretsManager.Unprotect(id);

        if (isValid)
        {
            Assert.Equal(secretString, secret);
        }
        else
        {
            Assert.NotEmpty(logger.GetLogMessages());
        }
    }

    [Theory]
    [InlineData("Component.Facet.PropertyName", true)]
    [InlineData("Component.Facet.ConfigurationId.PropertyName", true)]
    [InlineData("not.existing.id", false)]
    public void SecretsManager_UnprotectTests_NotFinalized(string id, bool hasBeenProtected)
    {
        var secretString = "pswd";

        var logger = new TestLogger();

        var dataProtector = new Mock<IInternalDataProtector>();

        dataProtector.Setup(dp => dp.Unprotect(It.IsAny<string>())).Returns(secretString);

        var mockManagementRegistry = new Mock<IRuntimeManagementRegistry>();
        var secretsManager = TestUtilities.CreateSecretsManagerInstance(dataProtector.Object, logger, null, mockManagementRegistry.Object);

        if (hasBeenProtected)
        {
            secretsManager.Protect(secretString, "notManagement", id);
        }

        // Unprotect gets called on the value of a protected property, which will have braces if it is an id
        var secret = secretsManager.Unprotect(string.Format(CultureInfo.InvariantCulture, SecretsManager.AddBrackets, id));

        if (hasBeenProtected)
        {
            Assert.Equal(secretString, secret);
            mockManagementRegistry.Verify(managementRegistry => managementRegistry.AddOrUpdateSecretIdFacetsMapping(It.IsAny<string>(),
                "Component", "Facet", It.IsAny<string>()), Times.Once);
        }
        else
        {
            Assert.Single(logger.GetLogMessages());
            Assert.True(logger.AreErrorsWarningsInLog());
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SecretsManager_UnprotectTests_NullOrEmptyValid(string id)
    {
        var logger = new TestLogger();

        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, logger, null);
        var secret = secretsManager.Unprotect(id);

        Assert.Equal(id, secret);
        Assert.Empty(logger.GetLogMessages());
    }

    [Theory]
    [InlineData(EdgeSystemConstants.ManagementComponentId)]
    [InlineData("management")]
    public void SecretsManager_Protect_ManagementSecretMatchesRegex(string managementComponentId)
    {
        var secretValue = "{{pswd}}";
        var logger = new TestLogger();

        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, logger, null);
        Assert.Equal(secretValue, secretsManager.Protect(secretValue, managementComponentId, "testId"));
    }

    [Theory]
    [InlineData(EdgeSystemConstants.ManagementComponentId)]
    [InlineData("management")]
    public void SecretsManager_Protect_ManagementSecretDoesNotMatchRegex(string managementComponentId)
    {
        var secretValue = "pswd";
        var protectedSecret = Guid.NewGuid();

        var logger = new TestLogger();
        var testProtector = new Mock<IInternalDataProtector>();

        testProtector.Setup(tp => tp.Protect(It.IsAny<string>())).Returns(protectedSecret.ToString());

        var secretsManager = TestUtilities.CreateSecretsManagerInstance(testProtector.Object, logger, null);
        Assert.Equal(protectedSecret.ToString(),
            secretsManager.Protect(secretValue, managementComponentId, "testId"));
    }

    [Fact]
    public void SecretsManager_Protect_ConfigurationSecretValueMatchesRegexAndNotExists()
    {
        var secretValue = "{{matches}}";

        var logger = new TestLogger();
        var testProtector = new Mock<IInternalDataProtector>();

        var secretsManager = TestUtilities.CreateSecretsManagerInstance(testProtector.Object, logger, null);

        Assert.Equal(secretValue, secretsManager.Protect(secretValue, "notmanagement", "testId"));
        Assert.NotEmpty(logger.GetLogMessages());
    }

    [Fact]
    public void SecretsManager_Protect_ConfigurationSecretValueMatchesRegexAndDoesExist()
    {
        var secretValue = "{{matches}}";
        var protectedSecret = Guid.NewGuid();

        var logger = new TestLogger();
        var configProvider = TestUtilities.GetMockConfigurationProvider();
        ICollection<string> errors = new List<string>();

        var managedSecretConfigurations = new[]
        {
            new ManagedSecretConfiguration() { Id = "matches", Value = protectedSecret.ToString() },
        };
        configProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(),
            out managedSecretConfigurations, out errors)).Returns(true);

        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, logger, configProvider.Object);

        var retval = secretsManager.Protect(secretValue, "notmanagement", "testId");
        Assert.Empty(logger.GetLogMessages());
        Assert.Equal(secretValue, retval);
    }

    [Fact]
    public void SecretsManager_Protect_ConfigurationSecretPlaintext()
    {
        var secretId = "testId";
        var secretValue = "pswd";
        var protectedSecret = Guid.NewGuid();

        var logger = new TestLogger();
        var testProtector = new Mock<IInternalDataProtector>();
        var configProvider = TestUtilities.GetMockConfigurationProvider();
        ICollection<string> errors = new List<string>();

        testProtector.Setup(tp => tp.Protect(It.IsAny<string>())).Returns(protectedSecret.ToString());
        configProvider
            .Setup(cp =>
                cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<It.IsAnyType>(),
                    out errors)).Returns(true);

        var secretsManager =
            TestUtilities.CreateSecretsManagerInstance(testProtector.Object, logger, configProvider.Object);
        Assert.Equal($"{{{{{secretId}}}}}", secretsManager.Protect(secretValue, "notmanagement", secretId));

        Assert.Empty(logger.GetLogMessages());
    }

    [Theory]
    [InlineData("please protect me", true)]
    [InlineData("xUedn#$sl!234D@", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SecretsManager_Protect_Plaintext_Tests(string plaintext, bool willBeProtected)
    {
        var logger = new TestLogger();
        var testProtector = new Mock<IInternalDataProtector>();
        var protectedString = Guid.NewGuid().ToString(); // guid represents the cryptographically protected input

        testProtector.Setup(tp => tp.Protect(It.IsAny<string>())).Returns(protectedString);

        var secretsManager = TestUtilities.CreateSecretsManagerInstance(testProtector.Object, logger, null);

        Assert.Equal(willBeProtected ? protectedString : plaintext, secretsManager.Protect(plaintext));
    }

    [Fact]
    public void SecretsManager_ConfigurationChangedAction_MultipleFacets_Test()
    {
        var arrayCallbackExecuted = false;
        var simpleCallbackExecuted = false;
        var componentId = "Test";
        var facetName = "TestFacet";
        var secretId = "MySecret";
        var secretIdWithPattern = "{{MySecret}}";
        var arrayComponentId = "Test1";
        var arrayFacetName = "TestFacet2";
        var arrayConfigurationId = "Index";

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockManagementRegistry = new Mock<IRuntimeManagementRegistry>();
        var mockInternalDataProtector = new Mock<IInternalDataProtector>();
        var logger = new TestLogger();

        var secretsManager = new SecretsManager(mockInternalDataProtector.Object, logger, mockConfigurationProvider.Object, mockManagementRegistry.Object);
        IReadOnlyList<(string, string, string)> facets = new List<(string, string, string)>
        {
            (componentId, facetName, null),
            (arrayComponentId, arrayFacetName, arrayConfigurationId),
        };

        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetFacetsWithSecret(secretId, out facets)).Returns(true);

        var sampleConfiguration = (object)new SampleConfiguration { Password = secretIdWithPattern, };
        var sampleArrayConfiguration = (object)new SampleConfiguration[]
        {
            new()
            {
                Id = arrayConfigurationId,
                Password = secretIdWithPattern,
            },
            new()
            {
                Id = "Hello",
                Password = string.Empty,
            },
        };

        var oldValue = new[] { new ManagedSecretConfiguration { Id = "MySecret", Value = "MyNewSecretValue", } };
        var newConfiguration = new[] { new ManagedSecretConfiguration { Id = "MySecret", Value = "MyNewSecretValue", } };

        ICollection<string> configurationErrors = new List<string>();
        var mockSimpleGetCommand = new Mock<IConfigurationGetCommand>();
        var mockArrayGetCommand = new Mock<IConfigurationGetCommand>();

        mockSimpleGetCommand.Setup(getCommand => getCommand.TryExecute(out sampleConfiguration, out configurationErrors)).Returns(true);
        mockArrayGetCommand.Setup(getCommand => getCommand.TryExecute(out sampleArrayConfiguration, out configurationErrors)).Returns(true);

        var mockCommandGenerator = new Mock<IConfigurationCommandGenerator>();
        mockCommandGenerator.Setup(commandGenerator => commandGenerator.GenerateConfigurationGetCommand(typeof(SampleConfiguration), It.IsAny<string>()))
            .Returns(mockSimpleGetCommand.Object);

        mockCommandGenerator.Setup(commandGenerator => commandGenerator.GenerateConfigurationGetCommand(typeof(SampleConfiguration[]), It.IsAny<string>()))
            .Returns(mockArrayGetCommand.Object);

        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) simpleConfigurationTuple = default;

        simpleConfigurationTuple.CommandGenerator = mockCommandGenerator.Object;
        simpleConfigurationTuple.ConfigurationType = typeof(SampleConfiguration);
        simpleConfigurationTuple.CallbackAction = args => { simpleCallbackExecuted = true; };

        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) arrayConfigurationTuple = default;

        arrayConfigurationTuple.CommandGenerator = mockCommandGenerator.Object;
        arrayConfigurationTuple.ConfigurationType = typeof(SampleConfiguration[]);
        arrayConfigurationTuple.CallbackAction = args => { arrayCallbackExecuted = true; };

        var simpleConfigurationTupleKeys = (componentId, facetName);
        var arrayConfigurationTupleKeys = (arrayComponentId, arrayFacetName);

        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetConfigurationRegistryCommandGeneratorTuple(simpleConfigurationTupleKeys, out simpleConfigurationTuple)).Returns(true);
        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetConfigurationRegistryCommandGeneratorTuple(arrayConfigurationTupleKeys, out arrayConfigurationTuple)).Returns(true);

        secretsManager.ConfigurationChangedAction(new ConfigurationChangedEventArgs(oldValue, newConfiguration));

        var messages = logger.GetLogMessages();
        Assert.Equal(2, messages.Count);
        Assert.Equal(LogLevel.Debug, messages[0].LogLevel);
        Assert.Equal(LogLevel.Debug, messages[1].LogLevel);
        Assert.True(simpleCallbackExecuted);
        Assert.True(arrayCallbackExecuted);
    }

    [Fact]
    public void SecretsManager_ConfigurationChangedAction_InvalidConfig_Logs_Test()
    {
        var simpleCallbackExecuted = false;
        var componentId = "Test";
        var facetName = "TestFacet";
        var secretId = "MySecret";
        var secretIdWithPattern = "{{MySecret}}";

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockManagementRegistry = new Mock<IRuntimeManagementRegistry>();
        var mockInternalDataProtector = new Mock<IInternalDataProtector>();
        var logger = new TestLogger();

        var secretsManager = new SecretsManager(mockInternalDataProtector.Object, logger, mockConfigurationProvider.Object, mockManagementRegistry.Object);
        IReadOnlyList<(string, string, string)> facets = new List<(string, string, string)>
        {
            (componentId, facetName, null),
        };

        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetFacetsWithSecret(secretId, out facets)).Returns(true);

        var sampleConfiguration = (object)new SampleConfiguration { Password = secretIdWithPattern, };

        var oldValue = new[] { new ManagedSecretConfiguration { Id = "MySecret", Value = "MyNewSecretValue", } };
        var newConfiguration = new[] { new ManagedSecretConfiguration { Id = "MySecret", Value = "MyNewSecretValue", } };

        ICollection<string> configurationErrors = new List<string>();
        var mockSimpleGetCommand = new Mock<IConfigurationGetCommand>();
        mockSimpleGetCommand.Setup(getCommand => getCommand.TryExecute(out sampleConfiguration, out configurationErrors)).Returns(false);

        var mockCommandGenerator = new Mock<IConfigurationCommandGenerator>();
        mockCommandGenerator.Setup(commandGenerator => commandGenerator.GenerateConfigurationGetCommand(typeof(SampleConfiguration), It.IsAny<string>()))
            .Returns(mockSimpleGetCommand.Object);

        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) simpleConfigurationTuple = default;

        simpleConfigurationTuple.CommandGenerator = mockCommandGenerator.Object;
        simpleConfigurationTuple.ConfigurationType = typeof(SampleConfiguration);
        simpleConfigurationTuple.CallbackAction = args => { simpleCallbackExecuted = true; };

        var simpleConfigurationTupleKeys = (componentId, facetName);
        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetConfigurationRegistryCommandGeneratorTuple(simpleConfigurationTupleKeys, out simpleConfigurationTuple)).Returns(true);

        secretsManager.ConfigurationChangedAction(new ConfigurationChangedEventArgs(oldValue, newConfiguration));
        var messages = logger.GetLogMessages();
        Assert.Single(messages);
        Assert.Equal(LogLevel.Debug, messages[0].LogLevel);
        Assert.False(simpleCallbackExecuted);
    }

    [Fact]
    public void SecretsManager_ConfigurationChangedAction_NullConfiguration_Test()
    {
        var simpleCallbackExecuted = false;
        var componentId = "Test";
        var facetName = "TestFacet";
        var secretId = "MySecret";

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockManagementRegistry = new Mock<IRuntimeManagementRegistry>();
        var mockInternalDataProtector = new Mock<IInternalDataProtector>();
        var logger = new TestLogger();

        var secretsManager = new SecretsManager(mockInternalDataProtector.Object, logger, mockConfigurationProvider.Object, mockManagementRegistry.Object);
        IReadOnlyList<(string, string, string)> facets = new List<(string, string, string)>
        {
            (componentId, facetName, null),
        };

        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetFacetsWithSecret(secretId, out facets)).Returns(true);

        object sampleConfiguration = null;

        var oldValue = new[] { new ManagedSecretConfiguration { Id = secretId, Value = "MyNewSecretValue", } };
        var newConfiguration = new[] { new ManagedSecretConfiguration { Id = secretId, Value = "MyNewSecretValue", } };

        ICollection<string> configurationErrors = new List<string>();
        var mockSimpleGetCommand = new Mock<IConfigurationGetCommand>();
        mockSimpleGetCommand.Setup(getCommand => getCommand.TryExecute(out sampleConfiguration, out configurationErrors)).Returns(true);

        var mockCommandGenerator = new Mock<IConfigurationCommandGenerator>();
        mockCommandGenerator.Setup(commandGenerator => commandGenerator.GenerateConfigurationGetCommand(typeof(SampleConfiguration), It.IsAny<string>()))
            .Returns(mockSimpleGetCommand.Object);

        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) simpleConfigurationTuple = default;

        simpleConfigurationTuple.CommandGenerator = mockCommandGenerator.Object;
        simpleConfigurationTuple.ConfigurationType = typeof(SampleConfiguration);
        simpleConfigurationTuple.CallbackAction = args => { simpleCallbackExecuted = true; };

        var simpleConfigurationTupleKeys = (componentId, facetName);
        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetConfigurationRegistryCommandGeneratorTuple(simpleConfigurationTupleKeys, out simpleConfigurationTuple)).Returns(true);

        secretsManager.ConfigurationChangedAction(new ConfigurationChangedEventArgs(oldValue, newConfiguration));
        var messages = logger.GetLogMessages();
        Assert.Single(messages);
        Assert.Equal(LogLevel.Debug, messages[0].LogLevel);
        Assert.False(simpleCallbackExecuted);

        mockManagementRegistry.Verify(managementRegistry => managementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, facetName, null), Times.Once);
    }

    [Fact]
    public void SecretsManager_ConfigurationChangedAction_MissingIndex_Test()
    {
        var simpleCallbackExecuted = false;
        var componentId = "Test";
        var facetName = "TestFacet";
        var secretId = "MySecret";
        var configurationEntry = "Index";
        var secretIdWithPattern = "{{MySecret}}";

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockManagementRegistry = new Mock<IRuntimeManagementRegistry>();
        var mockInternalDataProtector = new Mock<IInternalDataProtector>();
        var logger = new TestLogger();

        var secretsManager = new SecretsManager(mockInternalDataProtector.Object, logger, mockConfigurationProvider.Object, mockManagementRegistry.Object);
        IReadOnlyList<(string, string, string)> facets = new List<(string, string, string)>
        {
            (componentId, facetName, configurationEntry),
        };

        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetFacetsWithSecret(secretId, out facets)).Returns(true);

        var sampleArrayConfiguration = (object)new SampleConfiguration[]
        {
            new()
            {
                Id = "SomeIndex",
                Password = secretIdWithPattern,
            },
            new()
            {
                Id = "Hello",
                Password = string.Empty,
            },
        };

        var oldValue = new[] { new ManagedSecretConfiguration { Id = "MySecret", Value = "MyNewSecretValue", } };
        var newConfiguration = new[] { new ManagedSecretConfiguration { Id = "MySecret", Value = "MyNewSecretValue", } };

        ICollection<string> configurationErrors = new List<string>();
        var mockGetCommand = new Mock<IConfigurationGetCommand>();
        mockGetCommand.Setup(getCommand => getCommand.TryExecute(out sampleArrayConfiguration, out configurationErrors)).Returns(true);

        var mockCommandGenerator = new Mock<IConfigurationCommandGenerator>();
        mockCommandGenerator.Setup(commandGenerator => commandGenerator.GenerateConfigurationGetCommand(typeof(SampleConfiguration[]), It.IsAny<string>()))
            .Returns(mockGetCommand.Object);

        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) simpleConfigurationTuple = default;

        simpleConfigurationTuple.CommandGenerator = mockCommandGenerator.Object;
        simpleConfigurationTuple.ConfigurationType = typeof(SampleConfiguration[]);
        simpleConfigurationTuple.CallbackAction = args => { simpleCallbackExecuted = true; };

        var simpleConfigurationTupleKeys = (componentId, facetName);
        mockManagementRegistry.Setup(managementRegistry => managementRegistry.TryGetConfigurationRegistryCommandGeneratorTuple(simpleConfigurationTupleKeys, out simpleConfigurationTuple)).Returns(true);

        secretsManager.ConfigurationChangedAction(new ConfigurationChangedEventArgs(oldValue, newConfiguration));
        var messages = logger.GetLogMessages();
        Assert.Single(messages);
        Assert.Equal(LogLevel.Debug, messages[0].LogLevel);
        Assert.False(simpleCallbackExecuted);

        mockManagementRegistry.Verify(managementRegistry => managementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, facetName, configurationEntry), Times.Once);
    }

    [Theory]
    [InlineData("test.secret.id")]
    [InlineData("secretId")]
    public void SecretsManager_GetEncryptedSecretForId_SecretIdExists_Tests(string secretId)
    {
        var logger = new TestLogger();
        var testProtector = new Mock<IInternalDataProtector>();
        var protectedString = Guid.NewGuid().ToString();

        testProtector.Setup(tp => tp.Protect(It.IsAny<string>())).Returns(protectedString);
        var secretsManager = TestUtilities.CreateSecretsManagerInstance(testProtector.Object, logger, null);

        secretsManager.Protect("somesecret", "notManagement", secretId);
        secretId = string.Format(CultureInfo.InvariantCulture, SecretsManager.AddBrackets, secretId);
        secretsManager.ReconcileSecretValueChange(secretId);

        Assert.Equal(protectedString, secretsManager.GetProtectedString(secretId));
    }

    [Theory]
    [InlineData("test.secret.id")]
    [InlineData("test.secret.id.property")]
    [InlineData("secretId")]
    public void SecretsManager_ReconcileSecretValueChange_Tests(string secretId)
    {
        var logger = new TestLogger();
        var testProtector = new Mock<IInternalDataProtector>();
        var protectedString = Guid.NewGuid().ToString();
        var mockManagementRegistry = new Mock<IRuntimeManagementRegistry>();

        testProtector.Setup(tp => tp.Protect(It.IsAny<string>())).Returns(protectedString);
        var secretsManager = TestUtilities.CreateSecretsManagerInstance(testProtector.Object, logger, null, mockManagementRegistry.Object);

        secretsManager.Protect("somesecret", "notManagement", secretId);
        var secretIdWithPattern = string.Format(CultureInfo.InvariantCulture, SecretsManager.AddBrackets, secretId);
        secretsManager.ReconcileSecretValueChange(secretIdWithPattern, false);

        Assert.Equal(secretIdWithPattern, secretsManager.GetProtectedString(secretIdWithPattern));

        mockManagementRegistry.Verify(managementRegistry => managementRegistry.RemoveSecretIdFacetMapping(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("{{test.secret.id}}")]
    [InlineData("{{secretId}}")]
    public void SecretsManager_GetEncryptedSecretForId_SecretIdNotExists_Tests(string secretId)
    {
        var logger = new TestLogger();

        var secretsManager = TestUtilities.CreateSecretsManagerInstance(null, logger, null);

        Assert.Equal(secretId, secretsManager.GetProtectedString(secretId));
        Assert.Single(logger.GetLogMessages());
        Assert.True(logger.AreErrorsWarningsInLog());
    }

    private class SampleConfiguration
    {
        [Id]
        public string Id { get; set; }

        [Protected]
        public string Password { get; set; }
    }
}
