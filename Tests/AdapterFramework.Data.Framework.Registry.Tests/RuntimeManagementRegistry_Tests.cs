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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using Xunit;

namespace AdapterFramework.Data.Framework.Registry.Tests;

public class RuntimeManagementRegistry_Tests
{
    [Theory]
    [InlineData("MySecret", "Component1", "facetName", null)]
    [InlineData("MySecret", "Component1", "facetName2", "ConfigurationId")]
    public void RuntimeManagementRegistry_AddOrUpdateSecretIdFacetsMapping_Test(string secretId, string componentId, string facet, string configurationEntryId)
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, componentId, facet);
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        runtimeConfigurationRegistry.RegisterComponentConfiguration<OmfHealthEndpointConfiguration>(componentId, facet, commandGenerator, (x) => { },
            null);

        var secretsManagementRegistry = new RuntimeManagementRegistry(runtimeConfigurationRegistry);

        secretsManagementRegistry.AddOrUpdateSecretIdFacetsMapping(secretId, componentId, facet, configurationEntryId);

        Assert.True(secretsManagementRegistry.TryGetFacetsWithSecret(secretId, out var facets));
        Assert.Single(facets);
        Assert.Equal(componentId.ToUpperInvariant(), facets[0].ComponentId);
        Assert.Equal(facet.ToUpperInvariant(), facets[0].Facet);
        Assert.Equal(configurationEntryId, facets[0].EntryId);
    }

    [Theory]
    [InlineData(null, "Component1", "facetName")]
    [InlineData("", "Component1", "facetName2")]
    [InlineData("  ", "Component1", "facetName2")]
    [InlineData("SecretId", null, "facetName2")]
    [InlineData("SecretId", "", "facetName2")]
    [InlineData("SecretId", "  ", "facetName2")]
    [InlineData("SecretId", "Component1", null)]
    [InlineData("SecretId", "Component1", "")]
    [InlineData("SecretId", "Component1", "  ")]
    public void RuntimeManagementRegistry_AddOrUpdateSecretIdFacetsMapping_InvalidInput_Test(string secretId, string componentId, string facet)
    {
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();

        var secretsManagementRegistry = new RuntimeManagementRegistry(mockRuntimeConfigurationRegistry.Object);

        Assert.ThrowsAny<Exception>(() => secretsManagementRegistry.AddOrUpdateSecretIdFacetsMapping(secretId, componentId, facet, null));
    }

    [Fact]
    public void RuntimeManagementRegistry_RemoveSecretIdFacetMapping_Test()
    {
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var componentId = "Component";
        var facet = "Facet";
        var anotherFacet = "AnotherFacet";
        var secretId = "MySecret";
        var configurationEntryId = "Index";

        var secretsManagementRegistry = new RuntimeManagementRegistry(mockRuntimeConfigurationRegistry.Object);

        secretsManagementRegistry.AddOrUpdateSecretIdFacetsMapping(secretId, componentId, facet, null);

        Assert.True(secretsManagementRegistry.TryGetFacetsWithSecret(secretId, out var facets));
        Assert.Single(facets);

        secretsManagementRegistry.AddOrUpdateSecretIdFacetsMapping(secretId, componentId, anotherFacet, configurationEntryId);

        Assert.True(secretsManagementRegistry.TryGetFacetsWithSecret(secretId, out facets));
        Assert.Equal(2, facets.Count);

        secretsManagementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, anotherFacet, configurationEntryId);

        Assert.True(secretsManagementRegistry.TryGetFacetsWithSecret(secretId, out facets));
        Assert.Single(facets);
        Assert.Equal(facet.ToUpperInvariant(), facets[0].Facet);
        Assert.Null(facets[0].EntryId);

        secretsManagementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, facet, null);

        Assert.False(secretsManagementRegistry.TryGetFacetsWithSecret(secretId, out _));
    }

    [Fact]
    public void RuntimeManagementRegistry_RemoveSecretIdFacetMapping_NoSecret_Test()
    {
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();

        var secretsManagementRegistry = new RuntimeManagementRegistry(mockRuntimeConfigurationRegistry.Object);

        secretsManagementRegistry.RemoveSecretIdFacetMapping("SomeSecret", "SomeComponent", "SomeFacet", null);
        secretsManagementRegistry.RemoveSecretIdFacetMapping("SomeSecret", "SomeComponent", "SomeFacet", "EntryId");
    }

    [Theory]
    [InlineData(null, "Component1", "facetName")]
    [InlineData("", "Component1", "facetName2")]
    [InlineData("  ", "Component1", "facetName2")]
    [InlineData("SecretId", null, "facetName2")]
    [InlineData("SecretId", "", "facetName2")]
    [InlineData("SecretId", "  ", "facetName2")]
    [InlineData("SecretId", "Component1", null)]
    [InlineData("SecretId", "Component1", "")]
    [InlineData("SecretId", "Component1", "  ")]
    public void RuntimeManagementRegistry_RemoveSecretIdFacetMapping_InvalidInput_Test(string secretId, string componentId, string facet)
    {
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();

        var secretsManagementRegistry = new RuntimeManagementRegistry(mockRuntimeConfigurationRegistry.Object);

        Assert.ThrowsAny<Exception>(() => secretsManagementRegistry.RemoveSecretIdFacetMapping(secretId, componentId, facet, null));
    }

    [Fact]
    public void RuntimeManagementRegistry_TryGetConfigurationRegistryCommandGeneratorTuple_Test()
    {
        var requestedTuple = ("Component", "Facet");

        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();

        var secretsManagementRegistry = new RuntimeManagementRegistry(mockRuntimeConfigurationRegistry.Object);

        secretsManagementRegistry.TryGetConfigurationRegistryCommandGeneratorTuple(requestedTuple, out var something);

        mockRuntimeConfigurationRegistry.Verify(mcr => mcr.TryGetCommandGeneratorTuple(requestedTuple, out something), Times.Once);
    }
}
