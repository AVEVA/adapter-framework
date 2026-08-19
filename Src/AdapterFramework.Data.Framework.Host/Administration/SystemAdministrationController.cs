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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Host.Administration;

// The route below translates to "api/v1/Administration"
[Route(RouteConstants.BaseAdministrationRoute)]
public class SystemAdministrationController : Controller
{
    #region Private Constants

    private const string ComponentNotFoundString = "Component with ID: '{0}' doesn't have any functions registered.";
    private const string FunctionNameNotFoundString = "Component with ID: '{0}' doesn't support '{1}' function.";

    #endregion

    #region Private Fields

    private readonly IRuntimeAdministrationRegistry _runtimeAdministrationRegistry;     

    #endregion

    #region Public Constructor

    public SystemAdministrationController(IRuntimeAdministrationRegistry runtimeAdministrationRegistry)
    {
        _runtimeAdministrationRegistry = runtimeAdministrationRegistry;
    }

    #endregion

    #region HTTP POST

    [HttpPost("{componentId}/{function}")]
    public async Task<IActionResult> ExecuteCallbackFunctionAsync([FromRoute] string componentId, [FromRoute] string function)
    {
        if (_runtimeAdministrationRegistry.TryGetRegisteredFunctionNames(componentId, out _))
        {
            if (_runtimeAdministrationRegistry.TryGetCallbackFunction((componentId, function), out var action))
            {
                await action.Invoke();

                return NoContent();
            }

            return SetHttpResponse(StatusCodes.Status404NotFound, string.Format(CultureInfo.InvariantCulture, FunctionNameNotFoundString, componentId, function));
        }

        return SetHttpResponse(StatusCodes.Status404NotFound, string.Format(CultureInfo.InvariantCulture, ComponentNotFoundString, componentId));
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// This is a helper function to set the HTTP return code in a response and copy an error
    /// message into the response's body.
    /// </summary>
    /// <param name="httpCode">HTTP code (e.g. 200 => OK)</param>
    /// <param name="message">Message to be placed as a text string in the HTTP response</param>
    /// <param name="reason">Reason for a response.</param>
    /// <param name="resolution">Potential resolution (suggestion) of a problem.</param>
    /// <returns>A Task object so that this return can be called asynchronously</returns>
    private IActionResult SetHttpResponse(int httpCode, string message, string reason = "", string resolution = "")
    {
        if (!message.IsNullOrEmpty())
        {
            var json = new RestApiErrorResponse(message, reason, resolution);
            return StatusCode(httpCode, json);
        }

        return StatusCode(httpCode);
    }

    #endregion
}
