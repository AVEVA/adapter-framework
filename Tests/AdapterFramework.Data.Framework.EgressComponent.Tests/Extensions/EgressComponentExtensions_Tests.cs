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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.EgressComponent.Extensions;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests.Extensions;

public class EgressComponentExtensions_Tests
{
    [Fact]
    public void EgressComponentExtensions_AddEgress()
    {
        var services = new ServiceCollection();

        var mockComponentIdService = new Mock<IComponentIdService>();
        var mockConfigProvider = new Mock<IConfigurationProvider>();
        var mockRuntimeConfigurationRegistry = new Mock<IRuntimeConfigurationRegistry>();
        var mockLogManager = new Mock<ILogManager>();
        var mockEdgeComponentOperationService = new Mock<IEdgeComponentsOperationService>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockOmfWriterFactory = new Mock<IOmfWriterFactory>();
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();

        mockComponentIdService
            .Setup(componentIdService => componentIdService.GetEdgeComponentId(It.IsAny<string>()))
            .Returns("UnitTest");

        services.AddSingleton(mockConfigProvider.Object);
        services.AddSingleton(mockRuntimeConfigurationRegistry.Object);
        services.AddSingleton(mockLogManager.Object);
        services.AddSingleton(mockEdgeComponentOperationService.Object);
        services.AddSingleton(mockOmfDataEndpointManager.Object);
        services.AddSingleton(mockOmfWriterFactory.Object);
        services.AddSingleton(mockConfigurationProtector.Object);

        services.AddEgress(mockComponentIdService.Object);

        var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IEgressComponentIdService>());
    }

    [Fact]
    public void EgressComponentExtensions_UseEgress()
    {
        var sinkInitialized = false;

        var mockApplicationBuilder = new Mock<IApplicationBuilder>();
        var mockSinkProvider = new Mock<ISinkProvider>();

        mockSinkProvider.Setup(sink => sink.InitializeAsync()).Callback(() => sinkInitialized = true);

        mockApplicationBuilder.Setup(appBuilder => appBuilder.ApplicationServices.GetService(It.IsAny<Type>()))
            .Returns(mockSinkProvider.Object);

        mockApplicationBuilder.Object.UseEgress();

        Assert.True(sinkInitialized);
    }
}
