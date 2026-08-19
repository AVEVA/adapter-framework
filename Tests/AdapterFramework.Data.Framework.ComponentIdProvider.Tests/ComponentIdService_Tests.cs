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
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;
using Xunit;

namespace AdapterFramework.Data.Framework.ComponentIdProvider.Tests;

public class ComponentIdService_Tests
{
    [Fact]
    public void ComponentIdService_GetEdgeComponentId_ComponentFound()
    {
        var componentId1 = "UnitTestComponent1";
        var componentId2 = "UnitTestComponent2";
        var componentType = "UnitTest";

        var edgeComponentsConfig = new EdgeComponentConfig[]
        {
            new EdgeComponentConfig
            {
                ComponentId = componentId1,
                ComponentType = componentType,
            },
            new EdgeComponentConfig
            {
                ComponentId = componentId2,
                ComponentType = componentType,
            },
        };

        var mockConfigurationProvider = GetMockConfigProvider(edgeComponentsConfig);
        var mockApplicationManifest = GetMockApplicationManifest();
        var componentIdProvider = new ComponentIdService(mockConfigurationProvider.Object, mockApplicationManifest.Object);

        var receivedId = componentIdProvider.GetEdgeComponentId(componentType);

        Assert.Equal(componentId1, receivedId);

        receivedId = componentIdProvider.GetEdgeComponentId(componentType);

        Assert.Equal(componentId2, receivedId);

        // make sure that assigned component IDs were removed
        receivedId = componentIdProvider.GetEdgeComponentId(componentType);

        Assert.Null(receivedId);
    }

    [Fact]
    public void ComponentIdService_GetEdgeComponentId_ComponentNotFound()
    {
        var componentId1 = "UnitTestComponent1";
        var componentType = "UnitTest";
        var requiredComponentType = "NotFound";

        var edgeComponentsConfig = new EdgeComponentConfig[]
        {
            new EdgeComponentConfig
            {
                ComponentId = componentId1,
                ComponentType = componentType,
            },
        };

        var mockConfigurationProvider = GetMockConfigProvider(edgeComponentsConfig);
        var mockApplicationManifest = GetMockApplicationManifest();
        var componentIdProvider = new ComponentIdService(mockConfigurationProvider.Object, mockApplicationManifest.Object);

        var receivedId = componentIdProvider.GetEdgeComponentId(requiredComponentType);

        Assert.Null(receivedId);

        receivedId = componentIdProvider.GetEdgeComponentId(componentType);

        Assert.Equal(componentId1, receivedId);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("UnitTestComponent1")]
    public void ComponentIdService_GetEdgeComponentId_Duplicate_Reserved_Id(string componentId)
    {
        var componentId1 = "UnitTestComponent1";
        var componentType = "UnitTest";

        var edgeComponentsConfig = new EdgeComponentConfig[]
        {
            new EdgeComponentConfig
            {
                ComponentId = componentId1,
                ComponentType = componentType,
            },
            new EdgeComponentConfig
            {
                ComponentId = componentId,
                ComponentType = componentType,
            },
        };

        var mockConfigurationProvider = GetMockConfigProvider(edgeComponentsConfig);
        var mockApplicationManifest = GetMockApplicationManifest();
        Assert.Throws<InvalidOperationException>(() => new ComponentIdService(mockConfigurationProvider.Object, mockApplicationManifest.Object));
    }

    private static IMock<IApplicationManifest> GetMockApplicationManifest()
    {
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        mockApplicationManifest.Setup(x => x.HasComponent(It.IsAny<string>())).Returns(true);
        return mockApplicationManifest;
    }

    private static IMock<IConfigurationProvider> GetMockConfigProvider(EdgeComponentConfig[] configToReturn)
    {
        ICollection<string> errors = new List<string>();

        var mockConfigProvider = new Mock<IConfigurationProvider>();
        mockConfigProvider
            .Setup(cp =>
                cp.TryGetConfiguration(EdgeSystemConstants.SystemComponentId,
                    EdgeSystemConstants.ComponentsFacetName, out configToReturn, out errors)).Returns(configToReturn != null);

        return mockConfigProvider;
    }
}
