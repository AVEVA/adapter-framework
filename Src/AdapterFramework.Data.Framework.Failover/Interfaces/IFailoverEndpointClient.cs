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
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;

namespace AdapterFramework.Data.Framework.Failover.Interfaces;

public interface IFailoverEndpointClient : IDisposable
{
    /// <summary>
    /// Gets the Uri
    /// </summary>
    Uri Uri { get; }

    /// <summary>
    /// Updates the configuration.
    /// </summary>
    /// <param name="configuration">The new configuration that should be updated to.</param>
    void UpdateConfiguration(IEndpointConfiguration configuration);

    /// <summary>
    /// Sends a message to the endpoint.
    /// </summary>
    /// <param name="requestUri">The path to send the request to.</param>
    /// <param name="messageBody">Body content of the request (Post and Put only).</param>
    /// <param name="token">The cancellation token.</param>
    /// <param name="method">The Http verb to use.</param>
    /// <returns>An <see cref="EndpointResponse"/> that contains the response.</returns>
    Task<EndpointResponse> SendMessageAsync(string requestUri, byte[] messageBody, CancellationToken token, HttpVerb method = HttpVerb.Post);
}
