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
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Mvc;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Host.Diagnostics;

// The route below translates to "api/v1/Diagnostics/"
[Route(RouteConstants.BaseDiagnosticsRoute)]
public class SystemDiagnosticsController : Controller
{
    internal const string ProductNameString = "Product Name";
    internal const string AdaptersProductNamePrefix = "AVEVA Adapter for ";
    internal const string AdapterFrameworkVersionString = "Adapter Framework Version";
    internal const string ProductVersionString = "Product Version";
    internal const string RuntimeVersionString = "Runtime Version";
    internal const string OperatingSystemString = "Operating System";

    [HttpGet(RouteConstants.SystemTopic)]
    public IActionResult GetHostApplicationDiagnostics([FromServices] IEdgeDiagnosticsService diagnosticsService)
    {
        ThrowHelper.ThrowIfArgumentNull(diagnosticsService, nameof(diagnosticsService));

        return Ok(diagnosticsService.Collect());
    }

    [HttpGet(RouteConstants.ProductInformationTopic)]
    public IActionResult GetEdgeSystemVersionInformation([FromServices] IApplicationManifest applicationManifest)
    {
        ThrowHelper.ThrowIfArgumentNull(applicationManifest, nameof(applicationManifest));

        var productInformation = new Dictionary<string, string>
        {
            [ProductNameString] = GetProductName(applicationManifest),
            [ProductVersionString] = applicationManifest.ProductVersion?.ToString(),
            [AdapterFrameworkVersionString] = applicationManifest.AdapterFrameworkVersion?.ToString(),
            [RuntimeVersionString] = RuntimeInformation.FrameworkDescription,
            [OperatingSystemString] = RuntimeInformation.OSDescription,
        };

        return Ok(productInformation);
    }

    [HttpGet(RouteConstants.FailoverStateTopic)]
    public IActionResult GetCurrentFailoverState([FromServices] IFailoverManager failoverManager)
    {
        if (failoverManager is null)
        {
            return NotFound();
        }

        var currentFailoverState = failoverManager.GetCurrentFailoverState();
        if (currentFailoverState is null)
        {
            return NotFound();
        }

        return Ok(currentFailoverState);
    }

    private static string GetProductName(IApplicationManifest applicationManifest)
    {
        if (applicationManifest.IsEdgeDataStore)
        {
            return EdgeSystemConstants.ApplicationName;
        }

        return AdaptersProductNamePrefix + applicationManifest.GetAdapterTypes().FirstOrDefault();
    }
}
