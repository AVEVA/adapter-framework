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
using System.Collections.Generic;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Host.ComponentServices;
using AdapterFramework.Data.Framework.Host.Interfaces;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.ComponentServices;

public class EdgeComponentsOperationService_Tests
{
    [Fact]
    public void EdgeComponentsOperationService_ResendHealthMetadata_No_Components_Present()
    {
        var mockEdgeComponentsRepository = new Mock<IEdgeComponentsRepository>();

        IEnumerable<IEdgeAdapter> adapters = new List<IEdgeAdapter>();
        ISinkProvider sinkProvider = null;

        mockEdgeComponentsRepository.Setup(componentsRepo => componentsRepo.GetAdapters())
            .Returns(adapters);

        mockEdgeComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(sinkProvider);

        var edgeComponentsOperationService = new EdgeComponentsOperationService(mockEdgeComponentsRepository.Object);

        edgeComponentsOperationService.ResendHealthMetadata();
    }

    [Fact]
    public void EdgeComponentsOperationService_ResendHealthMetadata_Success()
    {
        var mockEdgeComponentsRepository = new Mock<IEdgeComponentsRepository>();
        var mockAdapter1 = new Mock<IEdgeAdapter>();
        var mockAdapter2 = new Mock<IEdgeAdapter>();
        var mockEdgeService1 = new Mock<IEdgeService>();
        var mockEdgeService2 = new Mock<IEdgeService>();

        var adapter1HealthMetadataResend = false;
        var adapter2HealthMetadataResend = false;
        var edgeService1HealthMetadataResend = false;
        var edgeService2HealthMetadataResend = false;

        mockAdapter1.Setup(adapter1 => adapter1.ResendHealthMetadata()).Callback(() => adapter1HealthMetadataResend = true);
        mockAdapter2.Setup(adapter2 => adapter2.ResendHealthMetadata()).Callback(() => adapter2HealthMetadataResend = true);
        mockEdgeService1.Setup(edgeService1 => edgeService1.ResendHealthMetadata()).Callback(() => edgeService1HealthMetadataResend = true);
        mockEdgeService2.Setup(edgeService1 => edgeService1.ResendHealthMetadata()).Callback(() => edgeService2HealthMetadataResend = true);

        IEnumerable<IEdgeAdapter> adapters = new List<IEdgeAdapter>() { mockAdapter1.Object, mockAdapter2.Object };
        IEnumerable<IEdgeService> services = new List<IEdgeService>() { mockEdgeService1.Object, mockEdgeService2.Object };

        mockEdgeComponentsRepository.Setup(componentsRepo => componentsRepo.GetAdapters()).Returns(adapters);
        mockEdgeComponentsRepository.Setup(componentsRepo => componentsRepo.GetEdgeServices()).Returns(services);

        var edgeComponentsOperationService = new EdgeComponentsOperationService(mockEdgeComponentsRepository.Object);

        edgeComponentsOperationService.ResendHealthMetadata();

        Assert.True(adapter1HealthMetadataResend);
        Assert.True(adapter2HealthMetadataResend);
        Assert.True(edgeService1HealthMetadataResend);
        Assert.True(edgeService2HealthMetadataResend);
    }

    [Fact]
    public void EdgeComponentsOperationService_ResendDynamicMetadata_Success()
    {
        var mockEdgeComponentsRepository = new Mock<IEdgeComponentsRepository>();
        var mockAdapter1 = new Mock<IEdgeAdapter>();
        var mockAdapter2 = new Mock<IEdgeAdapter>();

        var adapter1DynamicMetadataResend = false;
        var adapter2DynamicMetadataResend = false;

        mockAdapter1.Setup(adapter1 => adapter1.ResendDynamicMetadata()).Callback(() => adapter1DynamicMetadataResend = true);
        mockAdapter2.Setup(adapter2 => adapter2.ResendDynamicMetadata()).Callback(() => adapter2DynamicMetadataResend = true);

        IEnumerable<IEdgeAdapter> adapters = new List<IEdgeAdapter>() { mockAdapter1.Object, mockAdapter2.Object };

        mockEdgeComponentsRepository.Setup(componentsRepo => componentsRepo.GetAdapters())
            .Returns(adapters);

        var edgeComponentsOperationService = new EdgeComponentsOperationService(mockEdgeComponentsRepository.Object);

        edgeComponentsOperationService.ResendDynamicMetadata();

        Assert.True(adapter1DynamicMetadataResend);
        Assert.True(adapter2DynamicMetadataResend);
    }
}
