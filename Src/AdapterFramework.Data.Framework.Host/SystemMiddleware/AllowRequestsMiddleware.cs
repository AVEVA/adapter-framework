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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Host.SystemMiddleware;

/// <summary>
/// This middleware adds an Accept-Verbosity: verbose header to the request if that header key doesn't already exist.
/// This changes the behavior of calls to SDS to return full JSON objects rather than objects with missing key-value pairs
/// if the value is the default value for the type.
/// In order to revert to the original behvior of returning compact values, add an Accept-Verbosity header with any value
/// other than "verbose". A logical choice would be "compact", although it can be anything.
/// </summary>
public class AllowRequestsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAllowRequestsManager _allowRequestsManager;

    public AllowRequestsMiddleware(RequestDelegate next, IAllowRequestsManager allowRequestsManager)
    {
        _next = next;
        _allowRequestsManager = allowRequestsManager;
    }

    public async Task Invoke(HttpContext context)
    {
        ThrowHelper.ThrowIfArgumentNull(context, nameof(context));

        if (!_allowRequestsManager.RequestsAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        await _next(context);
    }
}
