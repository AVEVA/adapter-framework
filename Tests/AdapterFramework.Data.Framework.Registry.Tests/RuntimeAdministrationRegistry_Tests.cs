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
using System.Threading.Tasks;
using Xunit;

namespace AdapterFramework.Data.Framework.Registry.Tests;

public class RuntimeAdministrationRegistry_Tests
{
    [Theory]
    [ClassData(typeof(TestAdminDataGenerator))]
    public void RegisterComponentCallbackFunction_InvalidInput(string componentId, string facetName, Func<Task> function)
    {
        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();
        var exceptionThrown = false;
        try
        {
            runtimeAdministrationRegistry.RegisterComponentCallbackFunction(componentId, facetName, function);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Fact]
    public void RegisterComponentCallbackFunction_AddSameFunctionNameTwice_Throws()
    {
        var testComponentId = "TestComponent";
        var testConfigName = "TestFunction";

        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId, testConfigName, SampleCallbackFunction);

        Assert.True(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId, out var functionNames));
        Assert.Equal(testConfigName, functionNames[0]);

        Assert.Throws<InvalidOperationException>(() => runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId, testConfigName, SampleCallbackFunction));
    }

    [Fact]
    public void RegisterComponentCallbackFunction_Success()
    {
        var testComponentId = "TestComponent";
        var testConfigName = "TestConfiguration";

        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId, testConfigName, SampleCallbackFunction);

        Assert.True(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId, out var functionNames));
        Assert.Equal(testConfigName, functionNames[0]);

        Assert.True(runtimeAdministrationRegistry.TryGetCallbackFunction((testComponentId, testConfigName), out var callbackFunction));
        Assert.NotNull(callbackFunction);
    }

    [Theory]
    [InlineData(null, "function")]
    [InlineData("", "function")]
    [InlineData(" ", "function")]
    [InlineData("ComponentId", null)]
    [InlineData("ComponentId", "")]
    [InlineData("ComponentId", " ")]
    public void UnregisterComponentCallbackFunction_InvalidInput(string componentId, string facetName)
    {
        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        var exceptionThrown = false;
        try
        {
            runtimeAdministrationRegistry.UnregisterComponentCallbackFunction(componentId, facetName);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Fact]
    public void UnregisterComponentCallbackFunction_NothingRemains()
    {
        var testComponentId = "TestComponent";
        var testConfigName = "TestConfiguration";

        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId, testConfigName, SampleCallbackFunction);

        Assert.True(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId, out var facets));
        Assert.Equal(testConfigName, facets[0]);

        runtimeAdministrationRegistry.UnregisterComponentCallbackFunction(testComponentId, testConfigName);

        Assert.False(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId, out facets));
        Assert.False(runtimeAdministrationRegistry.TryGetCallbackFunction((testComponentId, testConfigName), out _));
    }

    [Fact]
    public void UnregisterComponentCallbackFunction_OneFacetRemains()
    {
        var testComponentId1 = "TestComponent";
        var testConfigName1 = "TestConfiguration1";
        var testConfigName2 = "TestConfiguration2";

        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId1, testConfigName1, SampleCallbackFunction);
        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId1, testConfigName2, SampleCallbackFunction);

        Assert.True(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId1, out var facets));
        Assert.Equal(2, facets.Count);

        runtimeAdministrationRegistry.UnregisterComponentCallbackFunction(testComponentId1, testConfigName1);

        Assert.True(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId1, out facets));
        Assert.Single(facets);
        Assert.True(runtimeAdministrationRegistry.TryGetCallbackFunction((testComponentId1, testConfigName2), out _));
        Assert.False(runtimeAdministrationRegistry.TryGetCallbackFunction((testComponentId1, testConfigName1), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UnregisterComponent_InvalidInput(string componentId)
    {
        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        var exceptionThrown = false;

        try
        {
            runtimeAdministrationRegistry.UnregisterComponent(componentId);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Fact]
    public void UnregisterComponent_Success()
    {
        var testComponentId1 = "TestComponent";
        var testConfigName1 = "TestConfiguration1";
        var testConfigName2 = "TestConfiguration2";

        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId1, testConfigName1, SampleCallbackFunction);
        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(testComponentId1, testConfigName2, SampleCallbackFunction);

        Assert.True(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId1, out var facets));
        Assert.Equal(2, facets.Count);

        runtimeAdministrationRegistry.UnregisterComponent(testComponentId1);

        Assert.False(runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(testComponentId1, out facets));
        Assert.False(runtimeAdministrationRegistry.TryGetCallbackFunction((testComponentId1, testConfigName2), out _));
        Assert.False(runtimeAdministrationRegistry.TryGetCallbackFunction((testComponentId1, testConfigName1), out _));
    }

    private async Task SampleCallbackFunction()
    {
        await Task.Delay(1);
    }

    #region Test Data Class

    internal class TestAdminDataGenerator : IEnumerable<object[]>
    {
        private readonly IEnumerable<object[]> _data = new List<object[]>
    {
        new object[] { null, "TestFunctionName", null },
        new object[] { " ", "TestFunctionName", null },
        new object[] { "TestId", null, null },
        new object[] { "TestId", string.Empty, null },
        new object[] { "TestId", " ", null },
        new object[] { "TestId", "TestFunctionName", null },
    };

        public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    #endregion
}
