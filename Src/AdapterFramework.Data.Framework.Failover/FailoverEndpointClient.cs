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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Failover.Interfaces;

namespace AdapterFramework.Data.Framework.Failover;

/// <summary>
/// IClient implementation that uses HTTP client to configure, send, and receive failover messages.
/// </summary>
public class FailoverEndpointClient : HttpClientWrapper, IFailoverEndpointClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FailoverEndpointClient"/> class.
    /// </summary>
    /// <param name="configuration">Endpoint configuration.</param>
    /// <param name="dataProtector"><see cref="IEdgeDataProtector"/> instance.</param>
    /// <param name="manifest"><see cref="IApplicationManifest"/> instance.</param>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="httpClientTimeout">Timeout for the internal HTTP client.</param>
    public FailoverEndpointClient(
        IEndpointConfiguration configuration,
        IEdgeDataProtector dataProtector,
        IApplicationManifest manifest,
        ILogger logger,
        TimeSpan httpClientTimeout)
        : base(configuration, dataProtector, manifest, logger)
    {
        Client.Timeout = httpClientTimeout;
    }

    /// <inheritdoc/>
    public async Task<EndpointResponse> SendMessageAsync(string requestUri, byte[] msgBody, CancellationToken token, HttpVerb method = HttpVerb.Post)
    {
        if (ShouldRecreateHttpClient)
        {
            RecreateHttpClient();
        }

        try
        {
            var response = await SendMessageInternalAsync(requestUri, msgBody, token, method);
            return await HandleResponseAsync(response);
        }
        catch (HttpRequestException httpRequestException)
        {
            return new EndpointResponse(ResponseStatusEnum.Fail, $"{nameof(HttpRequestException)}: {httpRequestException.Message}");
        }
        catch (TaskCanceledException)
        {
            if (!token.IsCancellationRequested)
            {
                return new EndpointResponse(ResponseStatusEnum.Fail, $"{nameof(TaskCanceledException)}: The send operation timed out.");
            }

            throw;
        }
        catch (Exception ex)
        {
            return new EndpointResponse(ResponseStatusEnum.Fail, $"Send operation failed: {ex.GetExceptionTypeAndMessages()}.");
        }
    }

    private static async Task<EndpointResponse> HandleResponseAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return await CreateEndpointResponseAsync(ResponseStatusEnum.Success, response);
        }

        switch (response.StatusCode)
        {
            case System.Net.HttpStatusCode.BadRequest:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.BadRequest, response);
            case System.Net.HttpStatusCode.Forbidden:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.Forbidden, response);
            case System.Net.HttpStatusCode.Conflict:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.Conflict, response);
            case System.Net.HttpStatusCode.TooManyRequests:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.DelayRequired, response);
            case System.Net.HttpStatusCode.NotFound:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.NotFound, response);
        }

        return await CreateEndpointResponseAsync(ResponseStatusEnum.Fail, response);
    }

    private static async ValueTask<EndpointResponse> CreateEndpointResponseAsync(ResponseStatusEnum responseStatus, HttpResponseMessage responseMessage)
    {
        var messageContent = await responseMessage.Content.ReadAsStringAsync();

        return new EndpointResponse(responseStatus, messageContent);
    }

    private async Task<HttpResponseMessage> SendMessageInternalAsync(string requestUri, byte[] body, CancellationToken token, HttpVerb method = HttpVerb.Post)
    {
        var headers = new Dictionary<string, string>();

        AddCsrfHeader(headers);
        await SetAuthorizationHeaderAsync(token);

        HttpContent messageContent = null;
        try
        {
            if (body != null)
            {
                messageContent = BuildContent(body, headers);
                messageContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }

#pragma warning disable CA2234 // Pass system uri objects instead of strings
            switch (method)
            {
                case HttpVerb.Post:
                    return await Client.PostAsync(requestUri, messageContent, token);
                case HttpVerb.Put:
                    return await Client.PutAsync(requestUri, messageContent, token);
                case HttpVerb.Delete:
                    return await Client.DeleteAsync(requestUri, token);
                case HttpVerb.Get:
                    return await Client.GetAsync(requestUri, token);
                default:
                    return await Client.PostAsync(requestUri, messageContent, token);
            }
#pragma warning restore CA2234 // Pass system uri objects instead of strings
        }
        finally
        {
            messageContent?.Dispose();
        }
   } 
}
