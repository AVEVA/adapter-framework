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
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.Web;

namespace AdapterFramework.Data.Framework.Host.Configuration;

// The route below translates to "api/v1/help"
[Route(RouteConstants.BaseHelpRoute)]
public class SystemConfigurationHelpController : Controller
{
    private const string ComponentNotFoundString = "Component with ID: '{0}' was not found.";
    private const string FacetNotFoundString = "Facet with name: '{0}' was not found.";

    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;
    private readonly IRuntimeManagementRegistry _runtimeManagementRegistry;

    public SystemConfigurationHelpController(IRuntimeConfigurationRegistry runtimeConfigurationRegistry, IRuntimeManagementRegistry runtimeManagementRegistry)
    {
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
        _runtimeManagementRegistry = runtimeManagementRegistry;
    }

    [HttpGet("{componentId}")]
    public IActionResult GetComponentHelp([FromRoute] string componentId)
    {
        if (_runtimeConfigurationRegistry.TryGetAvailableFacets(componentId, out var availableFacets))
        {
            var componentHelpBuilder = new StringBuilder();
            foreach (var facet in availableFacets)
            {
                if (_runtimeConfigurationRegistry.TryGetCommandLineHelpFunction(componentId, facet, out var cmdHelpFunc))
                {
                    componentHelpBuilder.AppendLine(cmdHelpFunc?.Invoke());
                }
            }

            return Ok(componentHelpBuilder.ToString());
        }

        if (_runtimeManagementRegistry.TryGetAvailableFacets(componentId, out availableFacets))
        {
            var componentHelpBuilder = new StringBuilder();
            foreach (var facet in availableFacets)
            {
                if (_runtimeManagementRegistry.TryGetCommandLineHelpFunction(componentId, facet, out var cmdHelpFunc))
                {
                    componentHelpBuilder.AppendLine(cmdHelpFunc?.Invoke());
                }
            }

            return Ok(componentHelpBuilder.ToString());
        }

        return NotFound(string.Format(CultureInfo.InvariantCulture, ComponentNotFoundString, componentId));
    }

    [HttpGet("{componentId}/{facet}")]
    public IActionResult GetComponentFacetHelp([FromRoute] string componentId, [FromRoute] string facet)
    {
        if (_runtimeConfigurationRegistry.TryGetAvailableFacets(componentId, out _))
        {
            if (_runtimeConfigurationRegistry.TryGetCommandLineHelpFunction(componentId, facet, out var cmdHelpFunc))
            {
                return Ok(cmdHelpFunc?.Invoke());
            }

            return NotFound(string.Format(CultureInfo.InvariantCulture, FacetNotFoundString, facet));
        }

        if (_runtimeManagementRegistry.TryGetAvailableFacets(componentId, out _))
        {
            if (_runtimeManagementRegistry.TryGetCommandLineHelpFunction(componentId, facet, out var cmdHelpFunc))
            {
                return Ok(cmdHelpFunc?.Invoke());
            }

            return NotFound(string.Format(CultureInfo.InvariantCulture, FacetNotFoundString, facet));
        }

        return NotFound(string.Format(CultureInfo.InvariantCulture, ComponentNotFoundString, componentId));
    }
}
