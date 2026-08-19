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

namespace AdapterFramework.Data.Framework.Host.SystemMiddleware;

/// <summary>
/// Middleware to add content security policy header to every HTTP response from the kestrel sever to instruct a browser (client)
/// that execution of any external content is unsafe.
/// </summary>
public class ContentSecurityPolicyMiddleware
{
    private const string HeaderCspKey = "Content-Security-Policy";
    private const string HeaderCspValue = "default-src 'self';object-src 'none'";

    private readonly RequestDelegate _next;

    public ContentSecurityPolicyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        context?.Response.Headers.Add(HeaderCspKey, HeaderCspValue);

        await _next(context);
    }
}
