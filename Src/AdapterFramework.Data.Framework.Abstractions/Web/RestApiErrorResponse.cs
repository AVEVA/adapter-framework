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
namespace AdapterFramework.Data.Framework.Abstractions.Web;

/// <summary>
/// Defines the REST API error response.
/// </summary>
public class RestApiErrorResponse
{
    /// <summary>
    /// Constructor for JSON Response when there is a REST API error.
    /// </summary>
    /// <param name="error">What went wrong</param>
    /// <param name="reason">Why the error occurred</param>
    /// <param name="resolution">What can be done to fix the problem</param>
    public RestApiErrorResponse(string error = "",
                                string reason = "",
                                string resolution = "")
    {
        Error = error;
        Reason = reason;
        Resolution = resolution;
    }

    /// <summary>
    /// What went wrong 
    /// </summary>
    public string Error { get; set; }

    /// <summary>
    /// Why the error occurred 
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// What can be done to fix the problem
    /// </summary>
    public string Resolution { get; set; }
}
