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
using Moq;
using Newtonsoft.Json.Linq;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using Xunit;

namespace AdapterFramework.Data.Framework.DataProtector.Tests;

public class ConfigurationProtector_Tests
{
    private const string MaskedValue = "***************";
    private const string TestComponentId = "TestComponentId";
    private const string TestFacetId = "TestFacetId";
    private const string ExpectedEncryptedSecret = "ExtremelyProtectedValue";

    [Fact]
    public void ConfigurationProtector_MaskSecrets_SingleObject()
    {
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.MaskSecrets(ref testConfiguration);

        Assert.Equal(MaskedValue, testConfiguration.ConfidentialValue1);
        Assert.NotEqual(originalConfiguration.ConfidentialValue1, testConfiguration.ConfidentialValue1);
        Assert.NotEqual(MaskedValue, testConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue2, testConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, testConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, testConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, testConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, testConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, testConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, testConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_MaskSecrets_SingleObject_TypePassed()
    {
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = (object)GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.MaskSecrets(ref testConfiguration, typeof(TestConfiguration));

        var typedTestConfiguration = (TestConfiguration)testConfiguration;

        Assert.Equal(MaskedValue, typedTestConfiguration.ConfidentialValue1);
        Assert.NotEqual(originalConfiguration.ConfidentialValue1, typedTestConfiguration.ConfidentialValue1);
        Assert.NotEqual(MaskedValue, typedTestConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue2, typedTestConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, typedTestConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, typedTestConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, typedTestConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, typedTestConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, typedTestConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, typedTestConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_MaskSecrets_Collection()
    {
        var testConfigurationCollection = GetSampleConfigurations(5);
        var originalConfigurationCollection = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.MaskSecrets(ref testConfigurationCollection);

        for (var i = 0; i < testConfigurationCollection.Length; i++)
        {
            Assert.Equal(MaskedValue, testConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(originalConfigurationCollection[i].ConfidentialValue1, testConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(MaskedValue, testConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, testConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, testConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, testConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, testConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, testConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, testConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_MaskSecrets_Collection_TypePassed()
    {
        var testConfiguration = (object)GetSampleConfigurations(5);
        var originalConfigurationCollection = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        var testConfigurationCollection = (object[])testConfiguration;
        configurationProtector.MaskSecrets(ref testConfigurationCollection, typeof(TestConfiguration[]));

        var typedTestConfigurationCollection = (TestConfiguration[])JArray.FromObject(testConfigurationCollection).ToObject(typeof(TestConfiguration[]));

        for (var i = 0; i < typedTestConfigurationCollection.Length; i++)
        {
            Assert.Equal(MaskedValue, typedTestConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(originalConfigurationCollection[i].ConfidentialValue1, typedTestConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(MaskedValue, typedTestConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, typedTestConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, typedTestConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, typedTestConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, typedTestConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, typedTestConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, typedTestConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_ProtectSecrets_SingleObject()
    {
        var expectedProtectedValue = "ExtremelyProtectedValue";
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.Protect(It.IsAny<string>(), TestComponentId, It.IsAny<string>())).Returns(expectedProtectedValue);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ProtectSecrets(ref testConfiguration, TestComponentId, TestFacetId);

        Assert.Equal(expectedProtectedValue, testConfiguration.ConfidentialValue1);
        Assert.NotEqual(originalConfiguration.ConfidentialValue1, testConfiguration.ConfidentialValue1);
        Assert.Equal(originalConfiguration.ConfidentialValue2, testConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, testConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, testConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, testConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, testConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, testConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, testConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_ProtectSecrets_SingleObject_TypePassed()
    {
        var expectedProtectedValue = "ExtremelyProtectedValue";
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = (object)GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.Protect(It.IsAny<string>(), TestComponentId, It.IsAny<string>())).Returns(expectedProtectedValue);
        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ProtectSecrets(ref testConfiguration, typeof(TestConfiguration), TestComponentId, TestFacetId);

        var typedTestConfiguration = (TestConfiguration)testConfiguration;

        Assert.Equal(expectedProtectedValue, typedTestConfiguration.ConfidentialValue1);
        Assert.NotEqual(originalConfiguration.ConfidentialValue1, typedTestConfiguration.ConfidentialValue1);
        Assert.Equal(originalConfiguration.ConfidentialValue2, typedTestConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, typedTestConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, typedTestConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, typedTestConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, typedTestConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, typedTestConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, typedTestConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_ProtectSecrets_Collection()
    {
        var expectedProtectedValue = "ExtremelyProtectedValue";
        var originalConfigurationCollection = GetSampleConfigurations(5);
        var testConfigurationCollection = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.Protect(It.IsAny<string>(), TestComponentId, It.IsAny<string>())).Returns(expectedProtectedValue);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ProtectSecrets(ref testConfigurationCollection, TestComponentId, TestFacetId);

        for (var i = 0; i < testConfigurationCollection.Length; i++)
        {
            Assert.Equal(expectedProtectedValue, testConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(originalConfigurationCollection[i].ConfidentialValue1, testConfigurationCollection[i].ConfidentialValue1);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, testConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, testConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, testConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, testConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, testConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, testConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_ProtectSecrets_Collection_TypePassed()
    {
        var expectedProtectedValue = "ExtremelyProtectedValue";
        var testConfiguration = (object)GetSampleConfigurations(5);
        var originalConfigurationCollection = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.Protect(It.IsAny<string>(), TestComponentId, It.IsAny<string>())).Returns(expectedProtectedValue);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        var testConfigurationCollection = (object[])testConfiguration;
        configurationProtector.ProtectSecrets(ref testConfigurationCollection, typeof(TestConfiguration[]), TestComponentId, TestFacetId);

        var typedTestConfigurationCollection = (TestConfiguration[])JArray.FromObject(testConfigurationCollection).ToObject(typeof(TestConfiguration[]));

        for (var i = 0; i < typedTestConfigurationCollection.Length; i++)
        {
            Assert.Equal(expectedProtectedValue, typedTestConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(originalConfigurationCollection[i].ConfidentialValue1, typedTestConfigurationCollection[i].ConfidentialValue1);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, typedTestConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, typedTestConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, typedTestConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, typedTestConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, typedTestConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, typedTestConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_UnProtectSecrets_SingleObject()
    {
        var expectedSecretValue = "SuperSecretValue";
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.Unprotect(It.IsAny<string>())).Returns(expectedSecretValue);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.UnProtectSecrets(ref testConfiguration);

        Assert.Equal(expectedSecretValue, testConfiguration.ConfidentialValue1);
        Assert.NotEqual(testConfiguration.ConfidentialValue1, originalConfiguration.ConfidentialValue1);
        Assert.Equal(originalConfiguration.ConfidentialValue2, testConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, testConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, testConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, testConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, testConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, testConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, testConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_UnProtectSecrets_Collection()
    {
        var expectedSecretValue = "SuperSecretValue";
        var originalConfigurationCollection = GetSampleConfigurations(5);
        var testConfigurationCollection = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.Unprotect(It.IsAny<string>())).Returns(expectedSecretValue);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.UnProtectSecrets(ref testConfigurationCollection);

        for (var i = 0; i < testConfigurationCollection.Length; i++)
        {
            Assert.Equal(expectedSecretValue, testConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(originalConfigurationCollection[i].ConfidentialValue1, testConfigurationCollection[i].ConfidentialValue1);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, testConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, testConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, testConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, testConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, testConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, testConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_ReplaceSecretIdsWithEncryptedSecrets_Collection()
    {
        var originalConfigurationCollection = GetSampleConfigurations(5);
        var testConfigurationCollection = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref testConfigurationCollection);

        for (var i = 0; i < testConfigurationCollection.Length; i++)
        {
            Assert.Equal(ExpectedEncryptedSecret, testConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(originalConfigurationCollection[i].ConfidentialValue1, testConfigurationCollection[i].ConfidentialValue1);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, testConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, testConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, testConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, testConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, testConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, testConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_ReplaceSecretIdsWithEncryptedSecrets_SingleObject()
    {
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref testConfiguration);

        Assert.Equal(ExpectedEncryptedSecret, testConfiguration.ConfidentialValue1);
        Assert.NotEqual(testConfiguration.ConfidentialValue1, originalConfiguration.ConfidentialValue1);
        Assert.Equal(originalConfiguration.ConfidentialValue2, testConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, testConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, testConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, testConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, testConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, testConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, testConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_ReplaceSecretIdsWithEncryptedSecrets_Collection_TypePassed()
    {
        var originalConfigurationCollection = GetSampleConfigurations(5);
        var testConfiguration = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        var testConfigurationCollection = (object[])testConfiguration;

        configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref testConfigurationCollection, typeof(TestConfiguration[]));

        var typedTestConfigurationCollection = (TestConfiguration[])JArray.FromObject(testConfigurationCollection).ToObject(typeof(TestConfiguration[]));

        for (var i = 0; i < typedTestConfigurationCollection.Length; i++)
        {
            Assert.Equal(ExpectedEncryptedSecret, typedTestConfigurationCollection[i].ConfidentialValue1);
            Assert.NotEqual(originalConfigurationCollection[i].ConfidentialValue1, typedTestConfigurationCollection[i].ConfidentialValue1);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, typedTestConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, typedTestConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, typedTestConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, typedTestConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, typedTestConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, typedTestConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_ReplaceSecretIdsWithEncryptedSecrets_SingleObject_TypePassed()
    {
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = (object)GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref testConfiguration, typeof(TestConfiguration));

        var typedTestConfiguration = (TestConfiguration)testConfiguration;

        Assert.Equal(ExpectedEncryptedSecret, typedTestConfiguration.ConfidentialValue1);
        Assert.NotEqual(originalConfiguration.ConfidentialValue1, typedTestConfiguration.ConfidentialValue1);
        Assert.Equal(originalConfiguration.ConfidentialValue2, typedTestConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, typedTestConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, typedTestConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, typedTestConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, typedTestConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, typedTestConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, typedTestConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_FinalizeSecrets_Collection()
    {
        var originalConfigurationCollection = GetSampleConfigurations(5);
        var testConfigurationCollection = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ReconcileSecretsChange(ref testConfigurationCollection, true);

        for (var i = 0; i < testConfigurationCollection.Length; i++)
        {
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue1, testConfigurationCollection[i].ConfidentialValue1);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, testConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, testConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, testConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, testConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, testConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, testConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_FinalizeSecrets_SingleObject()
    {
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ReconcileSecretsChange(ref testConfiguration, true);

        Assert.Equal(testConfiguration.ConfidentialValue1, originalConfiguration.ConfidentialValue1);
        Assert.Equal(originalConfiguration.ConfidentialValue2, testConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, testConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, testConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, testConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, testConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, testConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, testConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_FinalizeSecrets_Collection_TypePassed()
    {
        var originalConfigurationCollection = GetSampleConfigurations(5);
        var testConfiguration = GetSampleConfigurations(5);

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        var testConfigurationCollection = (object[])testConfiguration;

        configurationProtector.ReconcileSecretsChange(ref testConfigurationCollection, typeof(TestConfiguration[]), true);

        var typedTestConfigurationCollection = (TestConfiguration[])JArray.FromObject(testConfigurationCollection).ToObject(typeof(TestConfiguration[]));

        for (var i = 0; i < typedTestConfigurationCollection.Length; i++)
        {
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue1, typedTestConfigurationCollection[i].ConfidentialValue1);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue2, typedTestConfigurationCollection[i].ConfidentialValue2);
            Assert.Equal(originalConfigurationCollection[i].ConfidentialValue3, typedTestConfigurationCollection[i].ConfidentialValue3);
            Assert.Equal(originalConfigurationCollection[i].Name, typedTestConfigurationCollection[i].Name);
            Assert.Equal(originalConfigurationCollection[i].TestInt, typedTestConfigurationCollection[i].TestInt);
            Assert.Equal(originalConfigurationCollection[i].TestBool, typedTestConfigurationCollection[i].TestBool);
            Assert.Equal(originalConfigurationCollection[i].UnprotectedStuff, typedTestConfigurationCollection[i].UnprotectedStuff);
        }
    }

    [Fact]
    public void ConfigurationProtector_FinalizeSecrets_SingleObject_TypePassed()
    {
        var originalConfiguration = GetSampleTestConfiguration();
        var testConfiguration = (object)GetSampleTestConfiguration();

        var mockSecretManager = new Mock<ISecretsManager>();
        mockSecretManager.Setup(protector => protector.GetProtectedString(It.IsAny<string>())).Returns(ExpectedEncryptedSecret);

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        configurationProtector.ReconcileSecretsChange(ref testConfiguration, typeof(TestConfiguration), true);

        var typedTestConfiguration = (TestConfiguration)testConfiguration;

        Assert.Equal(originalConfiguration.ConfidentialValue1, typedTestConfiguration.ConfidentialValue1);
        Assert.Equal(originalConfiguration.ConfidentialValue2, typedTestConfiguration.ConfidentialValue2);
        Assert.Equal(originalConfiguration.ConfidentialValue3, typedTestConfiguration.ConfidentialValue3);
        Assert.Equal(originalConfiguration.Name, typedTestConfiguration.Name);
        Assert.Equal(originalConfiguration.TestId, typedTestConfiguration.TestId);
        Assert.Equal(originalConfiguration.TestInt, typedTestConfiguration.TestInt);
        Assert.Equal(originalConfiguration.TestBool, typedTestConfiguration.TestBool);
        Assert.Equal(originalConfiguration.UnprotectedStuff, typedTestConfiguration.UnprotectedStuff);
    }

    [Fact]
    public void ConfigurationProtector_ProtectSecrets_InvalidInput()
    {
        var invalidConfiguration = GetInvalidSampleTestConfiguration();
        var invalidConfigurationCollection = GetInvalidSampleConfigurations(4);

        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        invalidConfiguration = null;
        invalidConfigurationCollection = null;

        Assert.Throws<ArgumentNullException>(() => configurationProtector.ProtectSecrets(ref invalidConfiguration, TestComponentId, TestFacetId));
        Assert.Throws<ArgumentNullException>(() => configurationProtector.ProtectSecrets(ref invalidConfigurationCollection, TestComponentId, TestFacetId));
    }

    [Fact]
    public void ConfigurationProtector_UnprotectSecrets_InvalidInput()
    {
        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        InvalidTestConfiguration invalidConfiguration = null;
        InvalidTestConfiguration[] invalidConfigurationCollection = null;

        Assert.Throws<ArgumentNullException>(() => configurationProtector.UnProtectSecrets(ref invalidConfiguration));
        Assert.Throws<ArgumentNullException>(() => configurationProtector.UnProtectSecrets(ref invalidConfigurationCollection));
    }

    [Fact]
    public void ConfigurationProtector_MaskSecrets_InvalidInput()
    {
        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        InvalidTestConfiguration invalidConfiguration = null;
        InvalidTestConfiguration[] invalidConfigurationCollection = null;

        Assert.Throws<ArgumentNullException>(() => configurationProtector.MaskSecrets(ref invalidConfiguration));
        Assert.Throws<ArgumentNullException>(() => configurationProtector.MaskSecrets(ref invalidConfigurationCollection));
    }

    [Fact]
    public void ConfigurationProtector_ReplaceSecretIdsWithEncryptedSecrets_InvalidInput()
    {
        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        InvalidTestConfiguration invalidConfiguration = null;
        InvalidTestConfiguration[] invalidConfigurationCollection = null;

        Assert.Throws<ArgumentNullException>(() => configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref invalidConfiguration));
        Assert.Throws<ArgumentNullException>(() => configurationProtector.ReplaceSecretIdsWithEncryptedSecrets(ref invalidConfigurationCollection));
    }

    [Fact]
    public void ConfigurationProtector_FinalizeSecrets_InvalidInput()
    {
        var mockSecretManager = new Mock<ISecretsManager>();

        var configurationProtector = new ConfigurationProtector(mockSecretManager.Object);

        InvalidTestConfiguration invalidConfiguration = null;
        InvalidTestConfiguration[] invalidConfigurationCollection = null;

        Assert.Throws<ArgumentNullException>(() => configurationProtector.ReconcileSecretsChange(ref invalidConfiguration, true));
        Assert.Throws<ArgumentNullException>(() => configurationProtector.ReconcileSecretsChange(ref invalidConfigurationCollection, true));
    }

    private static TestConfiguration GetSampleTestConfiguration()
    {
        return new TestConfiguration
        {
            TestId = "TestId",
            Name = "TestConfig",
            ConfidentialValue1 = "Secret1",
            ConfidentialValue2 = string.Empty,
            ConfidentialValue3 = string.Empty,
            UnprotectedStuff = "Hello World...",
            TestInt = 42,
        };
    }

    private static InvalidTestConfiguration GetInvalidSampleTestConfiguration()
    {
        return new InvalidTestConfiguration()
        {
            Name = "TestConfig",
            ConfidentialValue1 = "Secret1",
            ConfidentialValue2 = string.Empty,
            UnprotectedStuff = "Hello World...",
        };
    }

    private static TestConfiguration[] GetSampleConfigurations(int configurationEntriesCount)
    {
        var configurations = new TestConfiguration[configurationEntriesCount];
        for (var i = 0; i < configurationEntriesCount; i++)
        {
            configurations[i] = GetSampleTestConfiguration();
        }

        return configurations;
    }

    private static InvalidTestConfiguration[] GetInvalidSampleConfigurations(int configurationEntriesCount)
    {
        var configurations = new InvalidTestConfiguration[configurationEntriesCount];
        for (var i = 0; i < configurationEntriesCount; i++)
        {
            configurations[i] = GetInvalidSampleTestConfiguration();
        }

        return configurations;
    }

    private class InvalidTestConfiguration
    {
        public string Name { get; set; }

        public bool TestBool { get; set; }

        [Protected]
        public string ConfidentialValue1 { get; set; }

        public string UnprotectedStuff { get; set; }

        [Protected]
        public string ConfidentialValue2 { get; set; }
    }

    private class TestConfiguration
    {
        [Id]
        public string TestId { get; set; }

        public string Name { get; set; }

        public int TestInt { get; set; }

        public bool TestBool { get; set; }

        [Protected]
        public string ConfidentialValue1 { get; set; }

        public string UnprotectedStuff { get; set; }

        [Protected]
        public string ConfidentialValue2 { get; set; }

        [Protected]
        public string ConfidentialValue3 { get; set; }
    }
}
