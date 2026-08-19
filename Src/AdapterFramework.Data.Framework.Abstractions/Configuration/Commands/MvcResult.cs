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
using System.Net;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;

/// <summary>
/// Defines a result object with status code that is meant to be returned to MVC controller.
/// </summary>
public class MvcResult
{
    public MvcResult(HttpStatusCode statusCode, object content = null)
    {
        StatusCode = (int)statusCode;
        Content = content;
    }

    public MvcResult(int statusCode, object content = null)
    {
        StatusCode = statusCode;
        Content = content;
    }

    /// <summary>
    /// HTTP status code.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Content to return.
    /// </summary>
    public object Content { get; }
}
