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
using Microsoft.AspNetCore.Mvc;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Host.Configuration;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Configuration;

public class SystemConfigurationHelpController_Tests
{
    private int _helpFunctionCalledCounter;

    [Fact]
    public void SystemConfigurationHelpController_GetComponentHelp_Test()
    {
        var testComponentId = "TestComponent";
        IList<string> facets = new List<string> { "logging", "datasource" };
        Func<string> helpFunction = SampleCommandlineHelpFunction;

        var mockRuntimeConfigRegistry = new Mock<IRuntimeConfigurationRegistry>();
        mockRuntimeConfigRegistry
            .Setup(runtimeRegistry => runtimeRegistry.TryGetAvailableFacets(testComponentId, out facets))
            .Returns(true);
        mockRuntimeConfigRegistry
            .Setup(runtimeRegistry => runtimeRegistry.TryGetCommandLineHelpFunction(testComponentId, It.IsAny<string>(), out helpFunction))
            .Returns(true);
        var mockRuntimeManagementRegistry = new Mock<IRuntimeManagementRegistry>();

        using var helpController = new SystemConfigurationHelpController(mockRuntimeConfigRegistry.Object, mockRuntimeManagementRegistry.Object);
        var result = helpController.GetComponentHelp(testComponentId);

        Assert.True(result is OkObjectResult);
        Assert.Equal(facets.Count, _helpFunctionCalledCounter);
    }

    [Fact]
    public void SystemConfigurationHelpController_GetComponentHelp_ComponentNotRegistered_Test()
    {
        var testComponentId = "TestComponent";
        IList<string> facets = new List<string> { "logging", "datasource" };

        var mockRuntimeConfigRegistry = new Mock<IRuntimeConfigurationRegistry>();
        mockRuntimeConfigRegistry
            .Setup(runtimeRegistry => runtimeRegistry.TryGetAvailableFacets(testComponentId, out facets))
            .Returns(false);
        var mockRuntimeManagementRegistry = new Mock<IRuntimeManagementRegistry>();

        using var helpController = new SystemConfigurationHelpController(mockRuntimeConfigRegistry.Object, mockRuntimeManagementRegistry.Object);
        var result = helpController.GetComponentHelp(testComponentId);

        Assert.True(result is NotFoundObjectResult);
    }

    [Fact]
    public void SystemConfigurationHelpController_GetComponentFacetHelp_Test()
    {
        var testComponentId = "TestComponent";
        var requestedFacet = "Logging";

        IList<string> facets = new List<string> { "logging", "datasource" };
        Func<string> helpFunction = SampleCommandlineHelpFunction;

        var mockRuntimeConfigRegistry = new Mock<IRuntimeConfigurationRegistry>();
        mockRuntimeConfigRegistry
            .Setup(runtimeRegistry => runtimeRegistry.TryGetAvailableFacets(testComponentId, out facets))
            .Returns(true);
        mockRuntimeConfigRegistry
            .Setup(runtimeRegistry => runtimeRegistry.TryGetCommandLineHelpFunction(testComponentId, requestedFacet, out helpFunction))
            .Returns(true);
        var mockRuntimeManagementRegistry = new Mock<IRuntimeManagementRegistry>();

        using var helpController = new SystemConfigurationHelpController(mockRuntimeConfigRegistry.Object, mockRuntimeManagementRegistry.Object);
        var result = helpController.GetComponentFacetHelp(testComponentId, requestedFacet);

        Assert.True(result is OkObjectResult);
        Assert.Equal(1, _helpFunctionCalledCounter);
    }

    [Fact]
    public void SystemConfigurationHelpController_GetComponentFacetHelp_FacetNotFund_Test()
    {
        var testComponentId = "TestComponent";
        var requestedFacet = "Logging";

        IList<string> facets = new List<string> { "logging", "datasource" };
        Func<string> helpFunction = null;

        var mockRuntimeConfigRegistry = new Mock<IRuntimeConfigurationRegistry>();
        mockRuntimeConfigRegistry
            .Setup(runtimeRegistry => runtimeRegistry.TryGetAvailableFacets(testComponentId, out facets))
            .Returns(true);
        mockRuntimeConfigRegistry
            .Setup(runtimeRegistry => runtimeRegistry.TryGetCommandLineHelpFunction(testComponentId, requestedFacet, out helpFunction))
            .Returns(false);
        var mockRuntimeManagementRegistry = new Mock<IRuntimeManagementRegistry>();

        using var helpController = new SystemConfigurationHelpController(mockRuntimeConfigRegistry.Object, mockRuntimeManagementRegistry.Object);
        var result = helpController.GetComponentFacetHelp(testComponentId, requestedFacet);

        Assert.True(result is NotFoundObjectResult);
    }

    private string SampleCommandlineHelpFunction()
    {
        _helpFunctionCalledCounter++;
        return "helpful string";
    }
}
