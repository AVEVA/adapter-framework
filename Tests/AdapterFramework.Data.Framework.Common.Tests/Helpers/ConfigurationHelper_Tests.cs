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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Common.Helpers;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Helpers;

public class ConfigurationHelper_Tests
{
    private const string TestComponentId = "TestComponent";
    private const string TestFacetId = "TestFacet";

    [Fact]
    public void GetIdProperty_Success()
    {
        var idProperty = ConfigurationHelper.GetIdProperty(typeof(IdAndProtected));

        Assert.NotNull(idProperty);
        Assert.Equal("Id", idProperty.Name);
    }

    [Fact]
    public void GetIdProperty_NotDefined()
    {
        var idProperty = ConfigurationHelper.GetIdProperty(typeof(NoIdOrProtected));

        Assert.Null(idProperty);
    }

    [Fact]
    public void GetIdProperty_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationHelper.GetIdProperty(null));
    }

    [Fact]
    public void GenerateProtectedPropertyId_Management()
    {
        var managedSecretConfigurations = new ManagedSecretConfiguration
        {
            Id = "TestId",
            Value = "TestValue",
        };

        var result = ConfigurationHelper.GenerateProtectedPropertyId(managedSecretConfigurations,
            typeof(ManagedSecretConfiguration).IsArray,
            EdgeSystemConstants.ManagementComponentId,
            EdgeSystemConstants.SecretsFacetName,
            "Value");
        Assert.Equal(managedSecretConfigurations.Id, result);
    }

    [Fact]
    public void GenerateProtectedPropertyId_Array()
    {
        var testId = "TestId";
        var expectedResult = $"{TestComponentId}.{TestFacetId}.{testId}.{nameof(IdAndProtected.Secret)}";
        
        var config = new[]
        {
            new IdAndProtected() { Id = testId, Secret = "TestSecret" },
        };

        var result = ConfigurationHelper.GenerateProtectedPropertyId(config[0],
            true,
            TestComponentId,
            TestFacetId,
            nameof(IdAndProtected.Secret));
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void GenerateProtectedPropertyId_NonArray()
    {
        var expectedResult = $"{TestComponentId}.{TestFacetId}.{nameof(IdAndProtected.Secret)}";

        var config = new IdAndProtected()
        { 
            Id = "TestId",
            Secret = "TestSecret",
        };

        var result = ConfigurationHelper.GenerateProtectedPropertyId(config,
            typeof(IdAndProtected).IsArray,
            TestComponentId,
            TestFacetId,
            nameof(IdAndProtected.Secret));
        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData(true, "test", "testFacet", "password")]
    [InlineData(false, null, "testFacet", "password")]
    [InlineData(false, "", "testFacet", "password")]
    [InlineData(false, "   ", "testFacet", "password")]
    [InlineData(false, "test", null, "password")]
    [InlineData(false, "test", "", "password")]
    [InlineData(false, "test", "    ", "password")]
    [InlineData(false, "test", "testFacet", null)]
    [InlineData(false, "test", "testFacet", "")]
    [InlineData(false, "test", "testFacet", "    ")]
    public void GenerateProtectedPropertyId_ThrowsNullException(bool nullConfigObject, string componentId, string facet, string propertyName)
    {
        var testObject = nullConfigObject ? null : new IdAndProtected()
        {
            Id = "1234",
            Secret = "1234",
        };

        Assert.ThrowsAny<Exception>(() => ConfigurationHelper.GenerateProtectedPropertyId(
            testObject, false, componentId, facet, propertyName));
    }

    [Fact]
    public void CreateObjectCopy_Test()
    {
        var testObject = new IdAndProtected { Id = "TestId", Secret = "TestSecret", };

        var objectCopy = ConfigurationHelper.CreateObjectCopy(testObject, typeof(IdAndProtected));
        var typedObjectCopy = (IdAndProtected)objectCopy;

        Assert.Equal(testObject.Id, typedObjectCopy.Id);
        Assert.Equal(testObject.Secret, typedObjectCopy.Secret);

        typedObjectCopy.Id = "SomethingElse";
        typedObjectCopy.Secret = "changedValue";

        Assert.NotEqual(testObject.Id, typedObjectCopy.Id);
        Assert.NotEqual(testObject.Secret, typedObjectCopy.Secret);
    }

    [Fact]
    public void CreateObjectCopy_InvalidInput_Test()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationHelper.CreateObjectCopy(null, null));
    }

    [Fact]
    public void GetProtectedPropertyInfos_InvalidInput_Test()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationHelper.GetProtectedPropertyInfos(null));
    }

    [Fact]
    public void GetProtectedPropertyInfos_Test()
    {
        var configuration = new IdAndProtected { Id = "Index", Secret = "HelloWorld", };

        var protectedPropertyInfos = ConfigurationHelper.GetProtectedPropertyInfos(typeof(IdAndProtected));
        var propertyInfo = Assert.Single(protectedPropertyInfos);
        var protectedPropertyValue = (string)propertyInfo.GetValue(configuration);

        Assert.Equal(configuration.Secret, protectedPropertyValue);
    }

    private class NoIdOrProtected
    {
        public string Id { get; set; }
        public string Secret { get; set; }
    }

    private class IdAndProtected
    {
        [Id]
        public string Id { get; set; }
        [Protected]
        public string Secret { get; set; }
    }
}
