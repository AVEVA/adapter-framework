// Copyright 2026 AVEVA Group Limited
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
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;

/// <summary>
/// Client responsible for interacting with the Weather.gov API.
/// Handles HTTP communication, retry logic, and response deserialization.
/// </summary>
internal class WeatherGovClient
{
    private readonly HttpClient _weatherGovApiClient;
    private readonly DataSourceConfiguration _config;
    private readonly ILogger _logger;

    // JsonSerializerOptions is thread-safe and caches per-type metadata, so a single shared instance
    // avoids rebuilding that cache for every client created on a data-source update.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance that consumes an injected <see cref="HttpClient"/>. The client is
    /// created and owned by the caller (the adapter, from an <see cref="IHttpClientFactory"/>), so this
    /// type never creates or disposes it. Per-request settings such as the User-Agent and timeout are
    /// applied on each call, so a single client can serve any configuration.
    /// </summary>
    /// <param name="httpClient">An <see cref="HttpClient"/> for the Weather.gov API, owned by the caller.</param>
    /// <param name="config">Configuration controlling API behavior such as base URL and timeouts.</param>
    /// <param name="logger">Logger used for recording retries, warnings, and failures.</param>
    public WeatherGovClient(HttpClient httpClient, DataSourceConfiguration config, ILogger logger)
    {
        _weatherGovApiClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // The configured base URL with any trailing slash removed, so path segments join cleanly.
    private string BaseUrl => _config.BaseUrl.TrimEnd('/');

    // Builds a station's latest-observation endpoint URL, percent-escaping the id into the path. Shared
    // by GetLatestObservation and the StationHasObservations discovery probe so the layout lives once.
    private string LatestObservationUrl(string stationId) =>
        $"{BaseUrl}/stations/{Uri.EscapeDataString(stationId)}/observations/latest";

    /// <summary>
    /// Resolves the observation-stations collection URL for a geographic point.
    /// Calls the Weather.gov <c>/points/{lat},{lon}</c> endpoint.
    /// </summary>
    /// <param name="latitude">Latitude of the seed point.</param>
    /// <param name="longitude">Longitude of the seed point.</param>
    /// <param name="token">Cancellation token used to cancel the request.</param>
    /// <returns>The observation-stations URL, or null when it cannot be resolved.</returns>
    public async Task<string> GetObservationStationsUrl(double latitude, double longitude, CancellationToken token)
    {
        // api.weather.gov expects coordinates with up to 4 decimal places, invariant formatting.
        var lat = latitude.ToString("0.####", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("0.####", CultureInfo.InvariantCulture);
        var url = $"{BaseUrl}/points/{lat},{lon}";

        var point = await GetWithRetries<PointResponse>(url, token).ConfigureAwait(false);
        return point?.Properties?.ObservationStations;
    }

    /// <summary>
    /// Retrieves the collection of observation stations from a stations collection URL.
    /// </summary>
    /// <param name="stationsUrl">Absolute stations collection URL (for example, from <see cref="GetObservationStationsUrl"/>).</param>
    /// <param name="token">Cancellation token used to cancel the request.</param>
    /// <returns>The stations response, or null when it cannot be retrieved.</returns>
    public Task<StationsResponse> GetStations(string stationsUrl, CancellationToken token)
    {
        return GetWithRetries<StationsResponse>(stationsUrl, token);
    }

    /// <summary>
    /// Retrieves the latest observation data for a given station.
    /// Implements retry logic for transient failures.
    /// </summary>
    /// <param name="stationId">The Weather.gov station identifier.</param>
    /// <param name="token">Cancellation token used to cancel the request.</param>
    /// <returns>The latest observation response for the specified station.</returns>
    /// <exception cref="HttpRequestException">Thrown when all retry attempts fail.</exception>
    public Task<ObservationResponse> GetLatestObservation(string stationId, CancellationToken token)
    {
        return GetWithRetries<ObservationResponse>(LatestObservationUrl(stationId), token);
    }

    /// <summary>
    /// Probes a station's latest-observation endpoint once (no retries) to determine whether it
    /// currently reports observations. Returns <c>false</c> only for a definitive <c>404 Not Found</c>,
    /// which for api.weather.gov means the station has no observation endpoint and will never report;
    /// transient failures return <c>true</c> so a usable station is not dropped on a temporary error.
    /// Used at discovery time to avoid surfacing stations that would fail every sampling cycle.
    /// </summary>
    /// <param name="stationId">The Weather.gov station identifier.</param>
    /// <param name="token">Cancellation token used to cancel the request.</param>
    /// <returns><c>false</c> if the latest-observation endpoint returns Not Found; otherwise <c>true</c>.</returns>
    public async Task<bool> StationHasObservations(string stationId, CancellationToken token)
    {
        try
        {
            return await SendGetAsync<bool>(
                LatestObservationUrl(stationId),
                token,
                static (response, _) => Task.FromResult(response.StatusCode != HttpStatusCode.NotFound))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException)
        {
            // A real cancellation propagates; a transient probe failure must not drop a possibly-good station.
            if (token.IsCancellationRequested)
            {
                throw;
            }

            return true;
        }
    }

    /// <summary>
    /// Sends a single GET for <paramref name="url"/> with the configured per-request timeout applied via a
    /// linked token (never mutating the shared HttpClient), then hands the response and that token to
    /// <paramref name="onResponse"/>. The request, response, and timeout sources are disposed once
    /// <paramref name="onResponse"/> completes, so the response must be consumed inside the callback.
    /// </summary>
    /// <typeparam name="TResult">The result the response callback produces.</typeparam>
    /// <param name="url">Absolute request URL.</param>
    /// <param name="token">Caller cancellation token, linked with the per-request timeout.</param>
    /// <param name="onResponse">Callback that reads the response under the linked token.</param>
    /// <returns>The value produced by <paramref name="onResponse"/>.</returns>
    private async Task<TResult> SendGetAsync<TResult>(
        string url,
        CancellationToken token,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> onResponse)
    {
        // Apply the configured per-request timeout without mutating the shared HttpClient.
        using var timeoutCts = new CancellationTokenSource(_config.RequestTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", _config.UserAgent);

        using var response = await _weatherGovApiClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token)
            .ConfigureAwait(false);

        return await onResponse(response, linkedCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a GET request for the given URL and deserializes the JSON response,
    /// retrying transient (5xx / 429) failures using the configured retry policy.
    /// </summary>
    /// <typeparam name="T">Type to deserialize the response into.</typeparam>
    /// <param name="url">Absolute request URL.</param>
    /// <param name="token">Cancellation token used to cancel the request.</param>
    /// <returns>The deserialized response.</returns>
    /// <exception cref="HttpRequestException">Thrown when all retry attempts fail.</exception>
    private async Task<T> GetWithRetries<T>(string url, CancellationToken token)
    {
        Exception lastError = null;

        for (int attempt = 1; attempt <= _config.MaxRetries + 1; attempt++)
        {
            try
            {
                return await SendGetAsync<T>(url, token, async (response, requestToken) =>
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        // Attach the status code so the retry filter can tell transient from terminal.
                        // Only 5xx and 429 are transient; any other status (4xx) is terminal and must fail
                        // fast, since retrying it wastes calls and can trip rate limiting.
                        throw new HttpRequestException(
                            $"Request to '{url}' failed with status {(int)response.StatusCode} ({response.StatusCode}).",
                            inner: null,
                            statusCode: response.StatusCode);
                    }

                    var stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);

                    return await JsonSerializer.DeserializeAsync<T>(
                        stream,
                        JsonOptions,
                        requestToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRetriable(ex) && !token.IsCancellationRequested)
            {
                // Retriable only: a transient status (5xx / 429), a transport error, or a per-request
                // timeout (timeoutCts). Terminal statuses and real cancellations fall through the
                // filter and propagate immediately.
                lastError = ex;

                if (attempt > _config.MaxRetries)
                    break;

                // Exponential backoff with jitter: the base delay doubles each attempt (1x, 2x, 4x, ...),
                // and a random 0..base component de-synchronizes concurrent clients so they don't retry in
                // lockstep and re-collide (the "thundering herd"). The exponent is capped so a large
                // MaxRetries cannot overflow the shift or produce a runaway delay (truncated exponential).
                var exponent = Math.Min(attempt - 1, 10);
                // Compute in long so a large configured RetryBackoffMs cannot overflow the int multiplication
                // (the shift alone is capped, but RetryBackoffMs is unbounded), then clamp to int for Task.Delay.
                var backoff = (long)_config.RetryBackoffMs << exponent;
                var jitter = Random.Shared.NextInt64(0, (long)_config.RetryBackoffMs + 1);
                var delay = (int)Math.Min(backoff + jitter, int.MaxValue);

                _logger.LogWarning(
                    ex,
                    "Retry {Attempt}, delay {Delay}ms",
                    attempt,
                    delay);

                await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }

        // Every retriable failure is surfaced as an HttpRequestException (or a cancellation), so throw
        // the same specific type on this defensive fallback rather than a base Exception that callers
        // cannot catch precisely.
        throw lastError ?? new HttpRequestException($"Failed to fetch '{url}'.");
    }

    // Transient failures are retried; terminal ones (such as a 4xx) propagate immediately. A
    // transport-level HttpRequestException carries no status code and is treated as transient.
    private static bool IsRetriable(Exception ex) => ex switch
    {
        HttpRequestException httpEx => httpEx.StatusCode is null || IsTransientStatus(httpEx.StatusCode.Value),
        OperationCanceledException => true,
        _ => false,
    };

    // Only server errors (5xx) and rate limiting (429) are worth retrying.
    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        (int)statusCode >= 500 || statusCode == HttpStatusCode.TooManyRequests;
}
