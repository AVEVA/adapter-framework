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
using System.Linq;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Host.ComponentServices;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Component_Services;

public class EdgeComponentsRepository_Tests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EdgeComponentsRepository_TryAddAdapter_InvalidInput(string adapterId)
    {
        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.ComponentId).Returns(adapterId);

        var edgeComponentsRepository = new EdgeComponentsRepository();
        Assert.ThrowsAny<Exception>(() => edgeComponentsRepository.TryAddAdapter(mockAdapter.Object));
        Assert.Empty(edgeComponentsRepository.GetAdapters());
    }

    [Fact]
    public void EdgeComponentsRepository_TryGetAdapter_InvalidInput()
    {
        string adapterId = null;
        var edgeComponentsRepository = new EdgeComponentsRepository();

        Assert.Throws<ArgumentNullException>(() => edgeComponentsRepository.TryGetAdapter(adapterId, out _));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void EdgeComponentsRepository_TryAddAdapter_AdaptersAdded(int numberOfAdaptersToAdd)
    {
        var adapterIdBase = "TestAdapter";
        var edgeComponentsRepository = new EdgeComponentsRepository();

        for (int i = 0; i < numberOfAdaptersToAdd; i++)
        {
            var mockAdapter = new Mock<IEdgeAdapter>();
            mockAdapter.Setup(adapter => adapter.ComponentId).Returns(adapterIdBase + i);

            Assert.True(edgeComponentsRepository.TryAddAdapter(mockAdapter.Object));
        }

        Assert.Equal(numberOfAdaptersToAdd, edgeComponentsRepository.GetAdapters().Count());

        Assert.True(edgeComponentsRepository.TryGetAdapter(adapterIdBase + 0, out var storedAdapter));
        Assert.NotNull(storedAdapter);

        Assert.True(edgeComponentsRepository.TryGetAdapter(adapterIdBase + (numberOfAdaptersToAdd - 1), out storedAdapter));
        Assert.NotNull(storedAdapter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("NonExistent")]
    public void EdgeComponentsRepository_TryGetAdapter_AdapterNotFound(string adapterId)
    {
        var existingAdapterId = "TestAdapter";
        var edgeComponentsRepository = new EdgeComponentsRepository();

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapterComponent => adapterComponent.ComponentId).Returns(existingAdapterId);

        edgeComponentsRepository.TryAddAdapter(mockAdapter.Object);

        var adapterFound = edgeComponentsRepository.TryGetAdapter(adapterId, out var adapter);

        Assert.False(adapterFound);
        Assert.Null(adapter);
    }

    [Fact]
    public void EdgeComponentsRepository_TryRemoveAdapter_InvalidInput()
    {
        var edgeComponentsRepository = new EdgeComponentsRepository();

        Assert.Throws<ArgumentNullException>(() =>
        {
            edgeComponentsRepository.TryRemoveAdapter(null, out var adapter);
            adapter?.Dispose();
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("NonExistent")]
    public void EdgeComponentsRepository_TryRemoveAdapter_AdapterNotFound(string adapterId)
    {
        var existingAdapterId = "TestAdapter";
        var edgeComponentsRepository = new EdgeComponentsRepository();

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapterComponent => adapterComponent.ComponentId).Returns(existingAdapterId);

        edgeComponentsRepository.TryAddAdapter(mockAdapter.Object);

        var adapterRemoved = edgeComponentsRepository.TryRemoveAdapter(adapterId, out var adapter);

        Assert.False(adapterRemoved);
        Assert.Null(adapter);
        Assert.Single(edgeComponentsRepository.GetAdapters());
        adapter?.Dispose();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void EdgeComponentsRepository_TryRemoveAdapter_AdaptersRemoved(int numberOfAdaptersToAdd)
    {
        var adapterIdBase = "TestAdapter";
        var edgeComponentsRepository = new EdgeComponentsRepository();

        for (int i = 0; i < numberOfAdaptersToAdd; i++)
        {
            var mockAdapter = new Mock<IEdgeAdapter>();
            mockAdapter.Setup(adapter => adapter.ComponentId).Returns(adapterIdBase + i);

            Assert.True(edgeComponentsRepository.TryAddAdapter(mockAdapter.Object));
        }

        Assert.Equal(numberOfAdaptersToAdd, edgeComponentsRepository.GetAdapters().Count());

        for (int i = 0; i < numberOfAdaptersToAdd; i++)
        {
            Assert.True(edgeComponentsRepository.TryRemoveAdapter(adapterIdBase + i, out var adapter));
            Assert.NotNull(adapter);
            adapter.Dispose();
        }

        Assert.Empty(edgeComponentsRepository.GetAdapters());
    }

    [Fact]
    public void EdgeComponentsRepository_TryRemoveEdgeService_InvalidInput()
    {
        var edgeComponentsRepository = new EdgeComponentsRepository();

        Assert.Throws<ArgumentNullException>(() =>
        {
            edgeComponentsRepository.TryRemoveEdgeService(null, out _);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("  ")]
    public void EdgeComponentsRepository_TryAddEdgeService_InvalidInput(string componentId)
    {
        var edgeComponentsRepository = new EdgeComponentsRepository();
        var mockService = new Mock<IEdgeService>();
        mockService.Setup(edgeService => edgeService.ComponentId).Returns(componentId);

        Assert.Throws<ArgumentNullException>(() =>
        {
            edgeComponentsRepository.TryAddEdgeService(null);
        });

        Assert.ThrowsAny<ArgumentException>(() =>
        {
            edgeComponentsRepository.TryAddEdgeService(mockService.Object);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void EdgeComponentsRepository_TryAddRemoveEdgeService_ServicesRemoved(int numberOfServicesToAdd)
    {
        var componentIdBase = "TestService";
        var edgeComponentsRepository = new EdgeComponentsRepository();

        for (int i = 0; i < numberOfServicesToAdd; i++)
        {
            var mockService = new Mock<IEdgeService>();
            mockService.Setup(edgeService => edgeService.ComponentId).Returns(componentIdBase + i);

            Assert.True(edgeComponentsRepository.TryAddEdgeService(mockService.Object));
        }

        Assert.Equal(numberOfServicesToAdd, edgeComponentsRepository.GetEdgeServices().Count());

        for (int i = 0; i < numberOfServicesToAdd; i++)
        {
            Assert.True(edgeComponentsRepository.TryRemoveEdgeService(componentIdBase + i, out var edgeService));
            Assert.NotNull(edgeService);
        }

        Assert.Empty(edgeComponentsRepository.GetEdgeServices());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EdgeComponentsRepository_TryGetEdgeService(bool found)
    {
        var serviceId = "TestService";
        var edgeComponentsRepository = new EdgeComponentsRepository();
        var mockService = new Mock<IEdgeService>();
        mockService.Setup(edgeService => edgeService.ComponentId).Returns(serviceId);

        edgeComponentsRepository.TryAddEdgeService(mockService.Object);

        Assert.Equal(found, edgeComponentsRepository.TryGetEdgeService(found ? serviceId : "NonExistent", out _));
    }

    [Fact]
    public void EdgeComponentsRepository_SetSinkProvider()
    {
        var mockSinkProvider = new Mock<ISinkProvider>();

        var edgeComponentsRepository = new EdgeComponentsRepository();

        edgeComponentsRepository.SetSinkProvider(mockSinkProvider.Object);

        var retrievedSinkProvider = edgeComponentsRepository.GetSinkProvider();

        Assert.Equal(mockSinkProvider.Object, retrievedSinkProvider);
    }

    [Fact]
    public void EdgeComponentsRepository_GetSinkProvider()
    {
        var edgeComponentsRepository = new EdgeComponentsRepository();

        var retrievedSinkProvider = edgeComponentsRepository.GetSinkProvider();

        Assert.Null(retrievedSinkProvider);
    }

    [Fact]
    public void EdgeComponentsRepository_SetSinkProvider_Sink_Null()
    {
        var edgeComponentsRepository = new EdgeComponentsRepository();

        edgeComponentsRepository.SetSinkProvider(null);

        Assert.Null(edgeComponentsRepository.GetSinkProvider());
    }
}
