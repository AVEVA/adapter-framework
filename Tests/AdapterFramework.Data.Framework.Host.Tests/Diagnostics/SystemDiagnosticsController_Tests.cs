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
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Host.Diagnostics;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Diagnostics;

public class SystemDiagnosticsController_Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEdgeSystemVersionInformation_VersionInfo_Found_ExpectedResults(bool isEdgeDataStore)
    {
        const string AdapterType = "Aurora";
        var frameworkVersion = new Version(1, 4, 0, 10);
        var productVersion = new Version(1, 1, 0);
        var adapterTypes = new List<string> { AdapterType };

        var mockApplicationManifest = new Mock<IApplicationManifest>();
        mockApplicationManifest.Setup(manifest => manifest.AdapterFrameworkVersion).Returns(frameworkVersion);
        mockApplicationManifest.Setup(manifest => manifest.ProductVersion).Returns(productVersion);
        mockApplicationManifest.Setup(manifest => manifest.IsEdgeDataStore).Returns(isEdgeDataStore);
        mockApplicationManifest.Setup(manifest => manifest.GetAdapterTypes()).Returns(adapterTypes);

        using var systemDiagnosticsController = new SystemDiagnosticsController();
        var result = systemDiagnosticsController.GetEdgeSystemVersionInformation(mockApplicationManifest.Object);

        var expectedProductInformation = new Dictionary<string, string>
        {
            [SystemDiagnosticsController.ProductNameString] = isEdgeDataStore ? EdgeSystemConstants.ApplicationName : SystemDiagnosticsController.AdaptersProductNamePrefix + AdapterType,
            [SystemDiagnosticsController.ProductVersionString] = productVersion.ToString(),
            [SystemDiagnosticsController.AdapterFrameworkVersionString] = frameworkVersion.ToString(),
            [SystemDiagnosticsController.RuntimeVersionString] = RuntimeInformation.FrameworkDescription,
            [SystemDiagnosticsController.OperatingSystemString] = RuntimeInformation.OSDescription,
        };

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(expectedProductInformation, objectResult.Value);
    }
}
