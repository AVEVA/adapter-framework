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
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.Host.Helpers;

namespace AdapterFramework.Data.Framework.Host.Management;

// The route below translates to "api/v1/Management"
[Route(RouteConstants.BaseManagementRoute)]
public class SystemManagementController : ControllerHelper
{
    #region Public Constructor

    public SystemManagementController(IConfigurationProtector configurationProtector, ILogger logger, IRuntimeManagementRegistry runtimeManagementRegistry)
        : base(configurationProtector, logger, runtimeManagementRegistry)
    {
    }

    #endregion

    #region HTTP GET

    [HttpGet("{facet}")]
    public IActionResult GetConfiguration([FromRoute] string facet)
    {
        return GetFacetConfiguration(EdgeSystemConstants.ManagementComponentId, facet);
    }

    [HttpGet("{facet}/{id}")]
    public IActionResult GetConfigurationById([FromRoute] string facet, [FromRoute] string id)
    {
        return GetFacetConfiguration(EdgeSystemConstants.ManagementComponentId, facet, id);
    }

    #endregion

    #region HTTP PUT

    [HttpPut("{facet}")]
    public IActionResult PutConfiguration([FromRoute] string facet, [FromBody] JsonElement configObject)
    {
        return PutFacetConfiguration(EdgeSystemConstants.ManagementComponentId, facet, configObject);
    }

    [HttpPut("{facet}/{id}")]
    public IActionResult PutConfigurationById([FromRoute] string facet, [FromRoute] string id, [FromBody] JsonElement configObject)
    {
        return PutFacetConfiguration(EdgeSystemConstants.ManagementComponentId, facet, configObject, id);
    }

    #endregion

    #region HTTP DELETE

    [HttpDelete("{facet}")]
    public IActionResult DeleteConfiguration([FromRoute] string facet)
    {
        return DeleteFacetConfiguration(EdgeSystemConstants.ManagementComponentId, facet);
    }

    [HttpDelete("{facet}/{id}")]
    public IActionResult DeleteConfigurationById([FromRoute] string facet, [FromRoute] string id)
    {
        return DeleteFacetConfiguration(EdgeSystemConstants.ManagementComponentId, facet, id);
    }

    #endregion
}
