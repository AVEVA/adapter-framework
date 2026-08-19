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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Common.HttpCommunication;

public class HttpClientWrapper : IDisposable
{
    private readonly ILogger _logger;
    private readonly IEdgeDataProtector _dataProtector;
    private readonly AccessTokenManager _accessTokenManager;
    private readonly string _csrfValue;
    private SocketsHttpHandler _socketsHttpHandler;
    private bool _disposed;
    private volatile bool _recreateHttpClient;

    private bool _certExpirationMessageWritten;

    public HttpClientWrapper(
        IEndpointConfiguration configuration,
        IEdgeDataProtector dataProtector,
        IApplicationManifest manifest,
        ILogger logger)
    {
        ThrowHelper.ThrowIfArgumentNull(manifest, nameof(manifest));

        _logger = logger;

        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        _dataProtector = dataProtector;

        Client = GetHttpClient(configuration);

        _accessTokenManager = new AccessTokenManager(logger, dataProtector);

        _csrfValue = manifest.IsEdgeDataStore ? EndpointManagerConstants.EdsCsrfValue : EndpointManagerConstants.AdapterCsrfValue;
    }

    /// <inheritdoc/>
    public Uri Uri => Client.BaseAddress;

    protected bool ShouldRecreateHttpClient { get => _recreateHttpClient; }

    protected HttpClient Client { get; private set; }

    protected IEndpointConfiguration Configuration { get; private set; }

    /// <summary>
    /// Used to update the current configuration.
    /// </summary>
    public void UpdateConfiguration(IEndpointConfiguration configuration)
    {
        if (configuration == null)
        {
            return;
        }

        if (!_recreateHttpClient)
        {
            _recreateHttpClient = Configuration.Endpoint != configuration.Endpoint
                                  || Configuration.ValidateEndpointCertificate !=
                                  configuration.ValidateEndpointCertificate
                                  || EnableKerberos(Configuration) !=
                                  EnableKerberos(configuration);
        }

        Configuration = configuration;

        var authenticationHeader = GetOmfAuthenticationHeaderAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (authenticationHeader != null)
        {
            Client.DefaultRequestHeaders.Authorization = authenticationHeader;
        }
        else
        {
            Client.DefaultRequestHeaders.Remove(EndpointManagerConstants.AuthorizationHeaderName);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected static HttpContent BuildContent(string body, IDictionary<string, string> headers)
    {
        ThrowHelper.ThrowIfArgumentNull(headers, nameof(headers));

        var stringContent = new StringContent(body, Encoding.UTF8, "application/json");

        foreach (var (headerKey, headerValue) in headers)
        {
            stringContent.Headers.Add(headerKey, headerValue);
        }

        return stringContent;
    }

    protected static HttpContent BuildContent(byte[] bodyBytes, IDictionary<string, string> headers)
    {
        ThrowHelper.ThrowIfArgumentNull(headers, nameof(headers));
        ThrowHelper.ThrowIfArgumentNull(bodyBytes, nameof(bodyBytes));

        var byteArrayContent = new ByteArrayContent(bodyBytes);

        foreach (var (headerKey, headerValue) in headers)
        {
            byteArrayContent.Headers.Add(headerKey, headerValue);
        }

        return byteArrayContent;
    }

    protected void RecreateHttpClient()
    {
        try
        {
            _socketsHttpHandler?.Dispose();
            Client?.Dispose();
            Client = GetHttpClient(Configuration);

            _logger.LogDebug("HTTP client for endpoint with Id '{Id}' has been re-created due to a configuration change.", Configuration.Id);
        }
        finally
        {
            _recreateHttpClient = false;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        _socketsHttpHandler?.Dispose();
        Client?.Dispose();

        _disposed = true;
    }

    protected void AddCsrfHeader(IDictionary<string, string> headers)
    {
        ThrowHelper.ThrowIfArgumentNull(headers, nameof(headers));

        // PI Web API only.
        if (string.IsNullOrWhiteSpace(Configuration.ClientId))
        {
            headers.Add(EndpointManagerConstants.CsrfKey, _csrfValue);
        }
    }

    protected void AddAcceptVerbosityHeader(IDictionary<string, string> headers)
    {
        ThrowHelper.ThrowIfArgumentNull(headers, nameof(headers));

        // PI Web API only. This adds Accept-Verbosity header with value 'verbose'.
        // PWA as a result returns all parameters in error response.
        if (!string.IsNullOrWhiteSpace(Configuration.UserName))
        {
            headers.Add(EndpointManagerConstants.AcceptVerbosityKey, EndpointManagerConstants.AcceptVerbosityValue);
        }
    }

    protected async Task SetAuthorizationHeaderAsync(CancellationToken cancellationToken)
    {
        if (!Client.DefaultRequestHeaders.Contains(EndpointManagerConstants.AuthorizationHeaderName) || _accessTokenManager.AccessTokenRequiresRefresh())
        {
            var authenticationHeader = await GetOmfAuthenticationHeaderAsync(cancellationToken);

            if (authenticationHeader != null)
            {
                Client.DefaultRequestHeaders.Authorization = authenticationHeader;
            }
        }
    }

    private static bool EnableKerberos(IEndpointConfiguration configuration)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
               && string.IsNullOrEmpty(configuration.UserName)
               && string.IsNullOrEmpty(configuration.ClientId);
    }

    private string UnprotectValue(string protectedValue)
    {
        var unprotectedValue = _dataProtector.Unprotect(protectedValue);

        return unprotectedValue ?? protectedValue;
    }

    private HttpClient GetHttpClient(IEndpointConfiguration configuration)
    {
        if (new Uri(configuration.Endpoint).Scheme != Uri.UriSchemeHttp && !configuration.ValidateEndpointCertificate)
        {
            _logger?.LogWarning(
                "Endpoint certificate validation is disabled for '{EndpointUrl}' endpoint. " +
                "AVEVA strongly recommends using this setting for testing purposes only. ",
                configuration.Endpoint);
        }

        _socketsHttpHandler = HttpClientHelper.GetCommonSocketHttpHandler(_certExpirationMessageWritten,
                (expirationDate, endpointUrl) => _logger.LogWarning(CertificateExpirationMessage, endpointUrl, expirationDate),
                configuration.ValidateEndpointCertificate);

        if (EnableKerberos(configuration))
        {
            _socketsHttpHandler.Credentials = CredentialCache.DefaultCredentials;
        }

        _certExpirationMessageWritten = false;
        return new HttpClient(_socketsHttpHandler) { BaseAddress = GetBaseAddress(configuration.Endpoint) };
    }

    private async Task<AuthenticationHeaderValue> GetOmfAuthenticationHeaderAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Configuration.UserName) && !string.IsNullOrWhiteSpace(Configuration.Password))
        {
            var byteArray = Encoding.UTF8.GetBytes(Configuration.UserName + ":" + UnprotectValue(Configuration.Password));
            return new AuthenticationHeaderValue(EndpointManagerConstants.BasicAuthorizationType, Convert.ToBase64String(byteArray));
        }

        if (!string.IsNullOrWhiteSpace(Configuration.ClientId) && !string.IsNullOrEmpty(Configuration.ClientSecret))
        {
            var token = await _accessTokenManager.GetAccessTokenAsync(Client, Configuration.Endpoint, Configuration.TokenEndpoint, Configuration.ClientId,
                Configuration.ClientSecret, cancellationToken);

            return token == null ? null : new AuthenticationHeaderValue(EndpointManagerConstants.BearerAuthorizationTypeString, token);
        }

        return null;
    }

    private Uri GetBaseAddress(string endpoint)
    {
        var baseAddress = new Uri(endpoint);

        if (baseAddress.Scheme == Uri.UriSchemeHttp && !baseAddress.IsLoopback)
        {
            _logger?.LogWarning(
                "Endpoint: '{EndpointUrl}' uses HTTP transport instead of HTTPS. " +
                "Data (including credentials) is being sent over the network unprotected (in a plaintext form). It is " +
                "strongly recommended to use HTTPS transport.", endpoint);
        }

        return baseAddress;
    }
}
