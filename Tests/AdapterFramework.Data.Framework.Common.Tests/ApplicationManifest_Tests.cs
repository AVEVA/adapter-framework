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
using AdapterFramework.Data.DataModel;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests;

public class ApplicationManifest_Tests
{
    [Fact]
    public void ApplicationManifest_NoPortOrUrlThrowsException()
    {
        var applicationManifest = new ApplicationManifest();
        Assert.Throws<InvalidOperationException>(() => applicationManifest.ApplicationPort);
        Assert.Throws<InvalidOperationException>(() => applicationManifest.BaseApplicationAddress);
    }

    [Theory]
    [InlineData(0, "hello", "prefix", "machine", "service", OmfVersion.Omf12)]
    [InlineData(0, "hello", "prefix", "machine", "service", OmfVersion.Omf13)]
    [InlineData(-1, "http://example.com", "zzza", "fax", "massage", OmfVersion.Omf12)]
    [InlineData(-1, "http://example.com", "zzza", "fax", "massage", OmfVersion.Omf13)]
    [InlineData(5590, "Test", "hp", "herp", "derp", OmfVersion.Omf12)]
    [InlineData(5590, "Test", "hp", "herp", "derp", OmfVersion.Omf13)]
    public void ApplicationManifest_Primitives(int port, string url, string healthPrefix, string machineName, string serviceName, OmfVersion omfVersion)
    {
        var applicationManifest = new ApplicationManifest(port, url, healthPrefix, machineName, serviceName, omfVersion);
        Assert.Equal(port, applicationManifest.ApplicationPort);
        Assert.Equal(url, applicationManifest.BaseApplicationAddress);
    }

    [Theory]
    [InlineData(OmfVersion.Omf12)]
    [InlineData(OmfVersion.Omf13)]
    public void ApplicationManifest_HasComponent_ComponentFound(OmfVersion omfVersion)
    {
        var requiredComponent = "AdapterFramework.Data.Framework.Abstractions";

        var applicationManifest = new ApplicationManifest();
        Assert.True(applicationManifest.HasComponent(requiredComponent));

        applicationManifest = new ApplicationManifest(1000, "Test", "a", "b", "c", omfVersion);
        Assert.True(applicationManifest.HasComponent(requiredComponent));
    }

    [Theory]
    [InlineData(OmfVersion.Omf12)]
    [InlineData(OmfVersion.Omf13)]
    public void ApplicationManifest_HasComponent_ComponentNotFound(OmfVersion omfVersion)
    {
        var requiredComponent = "AdapterFramework.Data.Framework.NonExistent";

        var applicationManifest = new ApplicationManifest();
        Assert.False(applicationManifest.HasComponent(requiredComponent));

        applicationManifest = new ApplicationManifest(1000, "Test", "a", "b", "c", omfVersion);
        Assert.False(applicationManifest.HasComponent(requiredComponent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ApplicationManifest_HasComponent_DoesNotThrow(string componentName)
    {
        var applicationManifest = new ApplicationManifest();
        Assert.False(applicationManifest.HasComponent(componentName));
    }

    [Fact]
    public void ApplicationManifest_IsEdgeDataStore_Test()
    {
        var applicationManifest = new ApplicationManifest();

        Assert.False(applicationManifest.IsEdgeDataStore);
    }

    [Fact]
    public void ApplicationManifest_ProductVersion_Test()
    {
        var applicationManifest = new ApplicationManifest();
        var productVersion = applicationManifest.ProductVersion;

        Assert.NotNull(productVersion);
    }

    [Fact]
    public void ApplicationManifest_FrameworkVersion_Test()
    {
        var applicationManifest = new ApplicationManifest();
        var frameworkVersion = applicationManifest.AdapterFrameworkVersion;

        Assert.NotNull(frameworkVersion);
    }
}
