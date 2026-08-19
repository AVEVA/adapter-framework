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
using System.Globalization;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.Extensions;
using Xunit;

namespace AdapterFramework.Data.Framework.Registry.Tests;

public class RuntimeConfigurationRegistry_Tests
{
    private const string TestComponentId1 = "TestComponent1";
    private const string TestComponentId2 = "TestComponent2";
    private const string TestFacetName1 = "TestFacet1";
    private const string TestFacetName2 = "TestFacet2";

    private static IDataSourceDiscoveryManager CreateMockDataSourceDiscoveryManager => new Mock<IDataSourceDiscoveryManager>().Object;

    [Theory]
    [ClassData(typeof(TestConfigurationDataGenerator))]
    public void RegisterComponentConfiguration_InvalidInput(string componentId, string facetName, IConfigurationCommandGenerator commandGenerator)
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        Assert.ThrowsAny<Exception>(() =>
            runtimeConfigurationRegistry.RegisterComponentConfiguration<TestConfigurationDataGenerator>(
                componentId,
                facetName,
                commandGenerator,
                null,
                null));
    }

    [Fact]
    public void RegisterComponentConfiguration_AddSameFacetTwice_Throws()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, commandGenerator, null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId1, out var facets));
        Assert.Equal(TestFacetName1, facets[0]);

        Assert.Throws<InvalidOperationException>(() => runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, commandGenerator, null, null));
    }

    [Fact]
    public void RegisterComponentConfiguration_ArrayConfigNoId()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        Assert.Throws<ArgumentException>(() => runtimeConfigurationRegistry.RegisterComponentConfiguration<BadConfiguration_MissingIndex[]>(TestComponentId1, TestFacetName1, commandGenerator, null, null));
    }

    [Fact]
    public void RegisterComponentConfiguration_ProtectedAttribute_WrongType()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        Assert.Throws<ArgumentException>(() => runtimeConfigurationRegistry.RegisterComponentConfiguration<BadConfiguration_ProtectedFieldType[]>(TestComponentId1, TestFacetName1, commandGenerator, null, null));
        Assert.Throws<ArgumentException>(() => runtimeConfigurationRegistry.RegisterComponentConfiguration<BadConfiguration_ProtectedFieldType>(TestComponentId1, TestFacetName1 + 1, commandGenerator, null, null));
    }

    [Fact]
    public void RegisterComponentConfiguration_TryGetFacetProtectedPropertyInfos_Test()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<GoodConfiguration>(TestComponentId1, TestFacetName1, commandGenerator, null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetFacetProtectedPropertyInfos(TestComponentId1, TestFacetName1, out var protectedProperties));
        Assert.Equal(2, protectedProperties.Count);
        Assert.Equal(nameof(GoodConfiguration.Password), protectedProperties[0].Name);
        Assert.Equal(nameof(GoodConfiguration.AnotherProtectedField), protectedProperties[1].Name);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<GoodConfiguration[]>(TestComponentId2, TestFacetName2, commandGenerator, null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetFacetProtectedPropertyInfos(TestComponentId2, TestFacetName2, out protectedProperties));
        Assert.Equal(2, protectedProperties.Count);
        Assert.Equal(nameof(GoodConfiguration.Password), protectedProperties[0].Name);
        Assert.Equal(nameof(GoodConfiguration.AnotherProtectedField), protectedProperties[1].Name);
    }

    [Theory]
    [InlineData("Component", "Facet")]
    [InlineData("component", "facet")]
    [InlineData("ComPOnenT", "FAceT")]
    public void RegisterComponentConfiguration_TryGetFacetProtectedPropertyInfos_CaseInsensitivity_Test(string componentId, string facetName)
    {
        var testComponent = "component";
        var testFacet = "facet";
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = CreateConfigurationCommandGenerator(testComponent, testFacet);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<GoodConfiguration>(testComponent, testFacet, commandGenerator, null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetFacetProtectedPropertyInfos(componentId, facetName, out var protectedProperties));
        Assert.Equal(2, protectedProperties.Count);
    }

    [Theory]
    [InlineData("Component", "")]
    [InlineData("Component", "  ")]
    [InlineData("Component", null)]
    [InlineData("", "facet")]
    [InlineData("  ", "facet")]
    [InlineData(null, "facet")]
    public void RegisterComponentConfiguration_TryGetFacetProtectedPropertyInfos_InvalidInput_Test(string componentId, string facetName)
    {
        var testComponent = "component";
        var testFacet = "facet";
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = CreateConfigurationCommandGenerator(testComponent, testFacet);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<GoodConfiguration>(testComponent, testFacet, commandGenerator, null, null);

        Assert.ThrowsAny<Exception>(() => runtimeConfigurationRegistry.TryGetFacetProtectedPropertyInfos(componentId, facetName, out _));
    }

    [Fact]
    public void RegisterComponentConfiguration_ArrayConfigWithId()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<DataSelectionConfigurationBase[]>(TestComponentId1, TestFacetName1, commandGenerator, null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId1, out var facets));
        Assert.Equal(TestFacetName1, facets[0]);
    }

    [Fact]
    public void RegisterComponentConfiguration_FacetRegistered()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1), null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId1, out var facets));
        Assert.Equal(TestFacetName1, facets[0]);
    }

    [Theory]
    [InlineData(null, "facet")]
    [InlineData("", "facet")]
    [InlineData(" ", "facet")]
    [InlineData("ComponentId", null)]
    [InlineData("ComponentId", "")]
    [InlineData("ComponentId", " ")]
    public void UnregisterComponentConfiguration_InvalidInput(string componentId, string facetName)
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        Assert.ThrowsAny<Exception>(() => runtimeConfigurationRegistry.UnregisterComponentConfiguration(componentId, facetName));
    }

    [Fact]
    public void UnregisterComponentConfiguration_NothingRemains()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1), null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId1, out var facets));
        Assert.Equal(TestFacetName1, facets[0]);

        runtimeConfigurationRegistry.UnregisterComponentConfiguration(TestComponentId1, TestFacetName1);

        Assert.False(runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId1, out _));
        Assert.False(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId1, TestFacetName1), out _));
    }

    [Fact]
    public void UnregisterComponentConfiguration_OneFacetRemains()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName2, commandGenerator, null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId1, out var facets));
        Assert.Equal(2, facets.Count);

        runtimeConfigurationRegistry.UnregisterComponentConfiguration(TestComponentId1, TestFacetName1);

        Assert.True(runtimeConfigurationRegistry.TryGetAvailableFacets(TestComponentId1, out facets));
        Assert.Single(facets);
        Assert.True(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId1, TestFacetName2), out _));
        Assert.False(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId1, TestFacetName1), out _));
    }

    [Fact]
    public void GetComponentIds_Success()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName2, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId2, TestFacetName1, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId2, TestFacetName2, commandGenerator, null, null);

        var registeredComponentIds = runtimeConfigurationRegistry.GetRegisteredComponentIds().ToReadOnlyList();

        Assert.Equal(2, registeredComponentIds.Count);
        Assert.Contains(TestComponentId1, registeredComponentIds);
        Assert.Contains(TestComponentId2, registeredComponentIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void UnregisterComponent_InvalidInput(string componentId)
    {
        Assert.ThrowsAny<Exception>(() => new RuntimeConfigurationRegistry().UnregisterComponent(componentId));
    }

    [Fact]
    public void UnregisterComponent_Success()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<GoodConfiguration>(TestComponentId1, TestFacetName2, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId2, TestFacetName1, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId2, TestFacetName2, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(TestComponentId1, CreateMockDataSourceDiscoveryManager);
        runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(TestComponentId2, CreateMockDataSourceDiscoveryManager);

        Assert.True(runtimeConfigurationRegistry.TryGetFacetProtectedPropertyInfos(TestComponentId1, TestFacetName2, out _));

        runtimeConfigurationRegistry.UnregisterComponent(TestComponentId1);

        Assert.Single(runtimeConfigurationRegistry.GetRegisteredComponentIds());
        Assert.False(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId1, TestFacetName2), out _));
        Assert.False(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId1, TestFacetName1), out _));
        Assert.True(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId2, TestFacetName2), out _));
        Assert.True(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId2, TestFacetName1), out _));
        Assert.True(runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId2, out _));
        Assert.False(runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId1, out _));
        Assert.False(runtimeConfigurationRegistry.TryGetFacetProtectedPropertyInfos(TestComponentId1, TestFacetName2, out _));
    }

    [Fact]
    public void TryGetCommandGeneratorTuple_CaseInsensitive()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        var commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName1, commandGenerator, null, null);
        runtimeConfigurationRegistry.RegisterComponentConfiguration<int>(TestComponentId1, TestFacetName2, commandGenerator, null, null);

        Assert.True(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId1.ToUpper(CultureInfo.InvariantCulture), TestFacetName1.ToUpper(CultureInfo.InvariantCulture)), out _));
        Assert.True(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((TestComponentId1.ToUpper(CultureInfo.InvariantCulture), TestFacetName2.ToUpper(CultureInfo.InvariantCulture)), out _));
        Assert.True(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple(("TESTcomponenT1", "TESTfaceT1"), out _));
        Assert.True(runtimeConfigurationRegistry.TryGetCommandGeneratorTuple(("tESTComPONENT1", "TeSTFacEt2"), out _));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData("testComponent", null)]
    public void RegisterDataSourceDiscoveryManager_InvalidInput(string componentId, IDataSourceDiscoveryManager dataSourceDiscoveryManager)
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        Assert.ThrowsAny<Exception>(() => runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(componentId, dataSourceDiscoveryManager));
    }

    [Fact]
    public void RegisterDataSourceDiscoveryManager_Test()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(TestComponentId1, CreateMockDataSourceDiscoveryManager);

        Assert.True(runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId1, out _));
    }

    [Fact]
    public void RegisterDataSourceDiscoveryManager_AddTwiceThrows()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(TestComponentId1, CreateMockDataSourceDiscoveryManager);

        Assert.Throws<InvalidOperationException>(() => runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(TestComponentId1, CreateMockDataSourceDiscoveryManager));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryGetDataSourceDiscoveryManager_InvalidInput(string componentId)
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();
        Assert.ThrowsAny<Exception>(() => runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out _));
    }

    [Theory]
    [InlineData(TestComponentId1)]
    [InlineData("tEsTComponent1")]
    [InlineData("testcomponent1")]
    public void TryGetDataSourceDiscoveryManager_Test(string componentId)
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(TestComponentId1, CreateMockDataSourceDiscoveryManager);

        Assert.True(runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var dataSourceDiscoveryManager));
        Assert.NotNull(dataSourceDiscoveryManager);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UnregisterDataSourceDiscoveryManager_InvalidInput(string componentId)
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        Assert.ThrowsAny<Exception>(() => runtimeConfigurationRegistry.UnregisterDataSourceDiscoveryManager(componentId));
    }

    [Fact]
    public void UnregisterDataSourceDiscoveryManager_Test()
    {
        var runtimeConfigurationRegistry = new RuntimeConfigurationRegistry();

        runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(TestComponentId1, CreateMockDataSourceDiscoveryManager);

        Assert.True(runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId1, out _));

        runtimeConfigurationRegistry.UnregisterDataSourceDiscoveryManager(TestComponentId1);

        Assert.False(runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(TestComponentId1, out _));
    }

    private static IConfigurationCommandGenerator CreateConfigurationCommandGenerator(string componentId, string facetName)
        => new ConfigurationCommandGenerator(new Mock<IConfigurationProvider>().Object, componentId, facetName);

    #region Test Data Class

    private class TestConfigurationDataGenerator : IEnumerable<object[]>
    {
        private static readonly IConfigurationCommandGenerator _commandGenerator = CreateConfigurationCommandGenerator(TestComponentId1, TestFacetName1);

        private readonly IEnumerable<object[]> _data = new List<object[]>
        {
            new object[] { null, TestFacetName1, _commandGenerator },
            new object[] { string.Empty, TestFacetName1, _commandGenerator },
            new object[] { " ", TestFacetName1, _commandGenerator },
            new object[] { TestComponentId1, null, _commandGenerator },
            new object[] { TestComponentId1, string.Empty, _commandGenerator },
            new object[] { TestComponentId1, " ", _commandGenerator },
            new object[] { TestComponentId1, TestFacetName1, null },
        };

        public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private class BadConfiguration_MissingIndex
    {
        [Protected]
        public string Password { get; set; }
    }

    private class BadConfiguration_ProtectedFieldType
    {
        [Id]
        public string Id { get; set; }

        [Protected]
        public int Password { get; set; }
    }

    private class GoodConfiguration
    {
        [Id]
        public string Id { get; set; }

        [Protected]
        public string Password { get; set; }

        [Protected]
        public string AnotherProtectedField { get; set; }
    }

    #endregion
}
