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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Common.Security;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;
using static AdapterFramework.Data.Framework.Common.Constants.AccessTokenManagerConstants;

namespace AdapterFramework.Data.Framework.Common;

/// <summary>
/// OpenId client token manager implementation that helps to manage client token life-cycle for OpenId compatible endpoint.
/// </summary>
public class AccessTokenManager
{
    private const string UnableToGetToken = "Unable to retrieve access token. Endpoint URL: {Url}. Access token endpoint: {TokenEndpoint}.";
    private const string UnableToGetTokenWithReason = UnableToGetToken + " Status code: {Code}. Response: {Response}";

    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;
    private readonly IEdgeDataProtector _dataProtector;
    private DateTime _accessTokenExpiry = DateTime.MaxValue;
    private (string EndpointUrl, string TokenEndpointUrl) _cachedTokenEndpoint;
    private bool _inError;

    private bool _certExpirationMessageWritten;

    public AccessTokenManager(ILogger logger, IEdgeDataProtector dataProtector)
    {
        _logger = logger;
        _dataProtector = dataProtector;
    }

    /// <summary>
    /// Gets an access token from the specified URLs from <paramref name="endpointUrl"/> based on OpenId specification or by accessing <paramref name="tokenEndpointUrl"/> directly.
    /// </summary>
    /// <param name="endpointUrl">The URL an endpoint to get token for.</param>
    /// <param name="tokenEndpointUrl">The URL of the token endpoint. Set as null if not applicable.</param>
    /// <param name="clientId">The client ID.</param>
    /// <param name="protectedClientSecret">The string of the protected client secret.Unprotected secret will be accepted, but errors will be logged.</param>
    /// <param name="validateEndpointCertificate">Boolean flag to indicate endpoint certificate validation.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Access token string when successful. Null when failed.</returns>
    public async Task<string> GetAccessTokenAsync(string endpointUrl, string tokenEndpointUrl, string clientId,
        string protectedClientSecret, bool validateEndpointCertificate, CancellationToken cancellationToken)
    {
        if (!HasValidEndpoint(endpointUrl, tokenEndpointUrl))
        {
            LogEndpointNotSpecifiedError();
            return null;
        }

        HttpClient client = null;
        SocketsHttpHandler httpClientHandler = null;

        try
        {
            client = GetHttpClient(validateEndpointCertificate, out httpClientHandler);
            return await GetAccessTokenInternalAsync(client, endpointUrl, tokenEndpointUrl, clientId, protectedClientSecret, cancellationToken);
        }
        finally
        {
            httpClientHandler?.Dispose();
            client?.Dispose();
        }
    }

    /// <summary>
    /// Gets an access token from the specified URLs using supplied HTTP <paramref name="httpClient"/> from <paramref name="endpointUrl"/> based on OpenId specification 
    /// or by accessing <paramref name="tokenEndpointUrl"/> directly.
    /// </summary>
    /// <param name="httpClient">The http client to use to retrieve the access token.</param>
    /// <param name="endpointUrl">The URL an endpoint to get token for.</param>
    /// <param name="tokenEndpointUrl">The URL of the token endpoint. Set as null if not applicable.</param>
    /// <param name="clientId">The client ID.</param>
    /// <param name="protectedClientSecret">The string of the protected client secret.Unprotected secret will be accepted, but errors will be logged.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>Access token string when successful. Null when failed.</returns>
    public async Task<string> GetAccessTokenAsync(HttpClient httpClient, string endpointUrl, string tokenEndpointUrl, string clientId,
        string protectedClientSecret, CancellationToken cancellationToken)
    {
        ThrowHelper.ThrowIfArgumentNull(httpClient, nameof(httpClient));

        if (!HasValidEndpoint(endpointUrl, tokenEndpointUrl))
        {
            LogEndpointNotSpecifiedError();
            return null;
        }

        return await GetAccessTokenInternalAsync(httpClient, endpointUrl, tokenEndpointUrl, clientId, protectedClientSecret, cancellationToken);
    }

    /// <summary>
    /// Determines whether the last returned access token has expired or is about to expire.
    /// Should be called before calling <see cref="GetAccessTokenAsync"/> to make sure it's needed.
    /// </summary>
    /// <returns>True if the token has expired or is about to expire; otherwise false.</returns>
    public bool AccessTokenRequiresRefresh()
    {
        return DateTime.UtcNow > _accessTokenExpiry;
    }

    private static bool HasValidEndpoint(string endpointUrl, string tokenEndpointUrl)
    {
        return !string.IsNullOrWhiteSpace(endpointUrl) || !string.IsNullOrWhiteSpace(tokenEndpointUrl);
    }

    private static string GetOpenIdUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var endPointUri = new Uri(url);
        var uriBuilder = new UriBuilder(endPointUri.Scheme, endPointUri.Host)
        {
            Path = BearAuthConfigLocationWithSlash,
        };

        if (url.Contains($":{endPointUri.Port}", StringComparison.OrdinalIgnoreCase))
        {
            uriBuilder.Port = endPointUri.Port;
        }

        return uriBuilder.ToString();
    }

    private HttpClient GetHttpClient(bool validateEndpointCertificate, out SocketsHttpHandler httpClientHandler)
    {
        httpClientHandler = HttpClientHelper.GetCommonSocketHttpHandler(_certExpirationMessageWritten,
           (expirationDate, endpointUrl) => _logger.LogWarning(CertificateExpirationMessage, endpointUrl, expirationDate),
           validateEndpointCertificate);

        _certExpirationMessageWritten = false;
        return new HttpClient(httpClientHandler);
    }

    private void LogEndpointNotSpecifiedError()
    {
        if (!_inError)
        {
            _logger.LogError("Cannot retrieve token if no URLs are specified.");
            _inError = true;
        }
    }

    private async Task<string> GetAccessTokenInternalAsync(HttpClient httpClient, string endpointUrl, string tokenEndpointUrl, string clientId,
        string protectedClientSecret, CancellationToken cancellationToken)
    {
        try
        {
            tokenEndpointUrl = await GetTokenEndpointAsync(endpointUrl, tokenEndpointUrl, httpClient, cancellationToken);
            if (string.IsNullOrWhiteSpace(tokenEndpointUrl))
            {
                if (!_inError)
                {
                    _logger.LogError("Cannot retrieve token endpoint URL.");
                    _inError = true;
                }

                return null;
            }

            _logger.LogDebug("Getting access token for {Endpoint} endpoint using {TokenEndpoint} as the token endpoint.", endpointUrl, tokenEndpointUrl);

            var values = new Dictionary<string, string>
            {
                { GrantTypeString, ClientCredentialsString },
                { ClientIdString, clientId },
                { ClientSecretString, UnprotectValue(protectedClientSecret) },
            };

            using var content = new FormUrlEncodedContent(values);
            using var response = await httpClient.PostAsync(new Uri(tokenEndpointUrl), content, cancellationToken);

            if (response != null && !response.IsSuccessStatusCode)
            {
                if (!_inError)
                {
                    _logger.LogWarning(UnableToGetTokenWithReason, endpointUrl, tokenEndpointUrl, response.StatusCode,
                        await response.Content.ReadAsStringAsync(cancellationToken));
                }

                _inError = true;
                return null;
            }

            var accessTokenObject = JsonSerializer.Deserialize<AccessToken>(await response.Content.ReadAsStringAsync(cancellationToken), _jsonSerializerOptions);
            _accessTokenExpiry = DateTime.UtcNow.AddSeconds(accessTokenObject.ExpiryTime - AccessTokenExpiryDelta);
            _inError = false;

            _logger.LogDebug("Getting access token for {Endpoint} endpoint was successful.", endpointUrl);

            return accessTokenObject.TokenString;
        }
        catch (TaskCanceledException tce)
        {
            if (!_inError && !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogError(tce, UnableToGetToken, endpointUrl, tokenEndpointUrl);
                _inError = true;
            }

            return null;
        }
        catch (Exception ex)
        {
            if (!_inError)
            {
                _logger?.LogError(ex, UnableToGetToken, endpointUrl, tokenEndpointUrl);
                _cachedTokenEndpoint = default;
                _inError = true;
            }

            return null;
        }
    }

    private async Task<string> GetTokenEndpointAsync(string endpointUrl, string tokenEndpointUrl, HttpClient client,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(tokenEndpointUrl))
        {
            return tokenEndpointUrl;
        }

        if (endpointUrl.Equals(_cachedTokenEndpoint.EndpointUrl, StringComparison.OrdinalIgnoreCase))
        {
            return _cachedTokenEndpoint.TokenEndpointUrl;
        }

        var openIdUrl = GetOpenIdUrl(endpointUrl);

        if (string.IsNullOrEmpty(openIdUrl))
        {
            _logger.LogWarning("Unable to obtain access token - specified endpoint {URL} is empty.", openIdUrl);
            return null;
        }

        using var openIdEndpointResponse = await client.GetAsync(new Uri(openIdUrl), cancellationToken);

        openIdEndpointResponse.EnsureSuccessStatusCode();

        var openIdResponse = JsonSerializer.Deserialize<OpenIdResponse>(await openIdEndpointResponse.Content.ReadAsStreamAsync(cancellationToken), _jsonSerializerOptions);

        if (openIdResponse == null || string.IsNullOrWhiteSpace(openIdResponse.TokenEndpoint))
        {
            if (!_inError)
            {
                _logger.LogWarning("Token endpoint returned from {URL} is null or empty.", openIdUrl);
            }

            return null;
        }

        _cachedTokenEndpoint = (endpointUrl, openIdResponse.TokenEndpoint);

        return openIdResponse.TokenEndpoint;
    }

    private string UnprotectValue(string protectedValue)
    {
        var unprotectedValue = _dataProtector.Unprotect(protectedValue);

        return unprotectedValue ?? protectedValue;
    }
}
