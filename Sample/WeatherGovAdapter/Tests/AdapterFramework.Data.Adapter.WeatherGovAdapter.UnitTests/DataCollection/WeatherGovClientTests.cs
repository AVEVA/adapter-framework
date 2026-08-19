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
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.DataCollection;

/// <summary>
/// Unit tests for <see cref="WeatherGovClient"/>. A stub <see cref="HttpMessageHandler"/> stands in for
/// the network so the retry policy, per-request timeout, cancellation handling, 404 probing, and request
/// shaping (URL and User-Agent) are exercised deterministically without real HTTP.
/// </summary>
public class WeatherGovClientTests
{
    private const string StationId = "KSEA";

    // A fully-formed observation payload; only the fields the assertions read are populated.
    private const string ObservationJson =
        "{\"properties\":{\"timestamp\":\"2026-03-11T01:02:03+00:00\",\"textDescription\":\"Clear\"}}";

    // ----- Constructor -----

    /// <summary>
    /// Verifies that the constructor rejects a null HttpClient.
    /// </summary>
    [Fact]
    public void Constructor_NullHttpClient_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new WeatherGovClient(null, CreateConfig(), NullLogger.Instance));

        Assert.Equal("httpClient", exception.ParamName);
    }

    // ----- GetLatestObservation: success and request shaping -----

    /// <summary>
    /// Verifies that a successful response is deserialized and returned.
    /// </summary>
    [Fact]
    public async Task GetLatestObservation_Success_ReturnsDeserializedObservation()
    {
        var handler = new StubHttpMessageHandler(RespondJson(ObservationJson));
        var client = CreateClient(handler);

        var result = await client.GetLatestObservation(StationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Clear", result.Properties.TextDescription);
    }

    /// <summary>
    /// Verifies that the request targets the expected latest-observation URL and carries the configured User-Agent.
    /// </summary>
    [Fact]
    public async Task GetLatestObservation_Success_SendsExpectedUrlAndUserAgent()
    {
        var config = CreateConfig();
        var handler = new StubHttpMessageHandler(RespondJson(ObservationJson));
        var client = CreateClient(handler, config);

        await client.GetLatestObservation(StationId, CancellationToken.None);

        Assert.Equal(
            "https://api.weather.gov/stations/KSEA/observations/latest",
            handler.RequestUris[0].AbsoluteUri);
        Assert.Equal(config.UserAgent, handler.UserAgents[0]);
    }

    /// <summary>
    /// Verifies that the station id is percent-escaped when it is placed into the request path.
    /// </summary>
    [Theory]
    [InlineData("KSEA", "KSEA")]
    [InlineData("K SEA", "K%20SEA")]  // a space is escaped rather than breaking the path
    public async Task GetLatestObservation_EscapesStationIdInUrl(string stationId, string expectedSegment)
    {
        var handler = new StubHttpMessageHandler(RespondJson(ObservationJson));
        var client = CreateClient(handler);

        await client.GetLatestObservation(stationId, CancellationToken.None);

        Assert.Equal(
            $"https://api.weather.gov/stations/{expectedSegment}/observations/latest",
            handler.RequestUris[0].AbsoluteUri);
    }

    /// <summary>
    /// Verifies that a 200 response whose body is JSON null resolves to a null observation.
    /// </summary>
    [Fact]
    public async Task GetLatestObservation_NullJsonBody_ReturnsNull()
    {
        var handler = new StubHttpMessageHandler(RespondJson("null"));
        var client = CreateClient(handler);

        var result = await client.GetLatestObservation(StationId, CancellationToken.None);

        Assert.Null(result);
    }

    // ----- GetLatestObservation: retry policy -----

    /// <summary>
    /// Verifies that a transient failure (5xx or 429) is retried and the subsequent success is returned.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetLatestObservation_TransientStatusThenSuccess_RetriesAndReturns(HttpStatusCode transientStatus)
    {
        var handler = new StubHttpMessageHandler(RespondStatus(transientStatus), RespondJson(ObservationJson));
        var client = CreateClient(handler);

        var result = await client.GetLatestObservation(StationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>
    /// Verifies that when every attempt fails transiently the call makes exactly MaxRetries + 1 attempts
    /// (including the MaxRetries = 0 no-retry boundary) and then rethrows the last transient error.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    public async Task GetLatestObservation_AllAttemptsFailTransiently_MakesMaxRetriesPlusOneAttempts(
        int maxRetries,
        int expectedAttempts)
    {
        var handler = new StubHttpMessageHandler(RespondStatus(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler, CreateConfig(maxRetries: maxRetries));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetLatestObservation(StationId, CancellationToken.None));

        Assert.Equal(expectedAttempts, handler.CallCount);
    }

    /// <summary>
    /// Verifies that retry jitter math handles RetryBackoffMs at int.MaxValue without overflowing,
    /// and that cancellation during the delay exits promptly.
    /// </summary>
    [Fact]
    public async Task GetLatestObservation_MaxIntBackoff_DoesNotOverflowJitterMath()
    {
        var handler = new StubHttpMessageHandler(RespondStatus(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler, CreateConfig(maxRetries: 1, retryBackoffMs: int.MaxValue));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetLatestObservation(StationId, cts.Token));

        // If the jitter upper bound overflows, this call fails before reaching cancellable delay.
        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// Verifies that a terminal (4xx) status fails fast with a single request and no retry.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task GetLatestObservation_TerminalStatus_DoesNotRetry(HttpStatusCode terminalStatus)
    {
        var handler = new StubHttpMessageHandler(RespondStatus(terminalStatus));
        var client = CreateClient(handler, CreateConfig(maxRetries: 3));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetLatestObservation(StationId, CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// Verifies that a transport-level failure (an HttpRequestException with no status code) is retried.
    /// </summary>
    [Fact]
    public async Task GetLatestObservation_TransportError_IsRetried()
    {
        var handler = new StubHttpMessageHandler(
            Throw(new HttpRequestException("simulated transport failure")),
            RespondJson(ObservationJson));
        var client = CreateClient(handler);

        var result = await client.GetLatestObservation(StationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>
    /// Verifies that a per-request timeout (the linked timeout token firing, not the caller's token) is
    /// retried. The first attempt blocks until the configured RequestTimeoutMs elapses.
    /// </summary>
    [Fact]
    public async Task GetLatestObservation_PerRequestTimeout_IsRetried()
    {
        var config = CreateConfig(maxRetries: 1, requestTimeoutMs: 200);
        var handler = new StubHttpMessageHandler(BlockUntilCancelled(), RespondJson(ObservationJson));
        var client = CreateClient(handler, config);

        var result = await client.GetLatestObservation(StationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, handler.CallCount);
    }

    /// <summary>
    /// Verifies that when the caller's own token is cancelled the call throws without retrying.
    /// </summary>
    [Fact]
    public async Task GetLatestObservation_CallerCancels_ThrowsWithoutRetrying()
    {
        var handler = new StubHttpMessageHandler(RespondJson(ObservationJson));
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetLatestObservation(StationId, cts.Token));

        // A cancelled caller token stops the retry loop immediately: at most the first send is attempted.
        Assert.True(handler.CallCount <= 1, $"Expected no retries, but the handler was called {handler.CallCount} times.");
    }

    // ----- GetObservationStationsUrl -----

    /// <summary>
    /// Verifies that the observation-stations URL is read from the points payload.
    /// </summary>
    [Fact]
    public async Task GetObservationStationsUrl_ReturnsObservationStationsFromPayload()
    {
        const string stationsUrl = "https://api.weather.gov/gridpoints/SEW/124,68/stations";
        var handler = new StubHttpMessageHandler(RespondJson($"{{\"properties\":{{\"observationStations\":\"{stationsUrl}\"}}}}"));
        var client = CreateClient(handler);

        var result = await client.GetObservationStationsUrl(47.6062, -122.3321, CancellationToken.None);

        Assert.Equal(stationsUrl, result);
    }

    /// <summary>
    /// Verifies that coordinates are formatted with invariant culture and up to four decimals (trailing
    /// zeros trimmed, excess precision rounded) when building the points URL.
    /// </summary>
    [Theory]
    [InlineData(47.6062, -122.3321, "47.6062,-122.3321")]
    [InlineData(0, 0, "0,0")]                               // origin
    [InlineData(-33.8688, 151.2093, "-33.8688,151.2093")]  // southern / eastern hemisphere
    [InlineData(47.6, -122.3, "47.6,-122.3")]              // trailing zeros are trimmed
    [InlineData(90, -180, "90,-180")]                       // range boundaries as whole numbers
    [InlineData(1.23456, 2.34561, "1.2346,2.3456")]        // excess precision rounded to four decimals
    public async Task GetObservationStationsUrl_FormatsCoordinatesInvariant(
        double latitude,
        double longitude,
        string expectedCoordinates)
    {
        var handler = new StubHttpMessageHandler(RespondJson("{\"properties\":{}}"));
        var client = CreateClient(handler);

        await client.GetObservationStationsUrl(latitude, longitude, CancellationToken.None);

        Assert.Equal(
            $"https://api.weather.gov/points/{expectedCoordinates}",
            handler.RequestUris[0].AbsoluteUri);
    }

    /// <summary>
    /// Verifies that a points response missing the properties object or the observation-stations URL
    /// resolves to null.
    /// </summary>
    [Theory]
    [InlineData("{}")]                    // no properties
    [InlineData("{\"properties\":{}}")]   // properties present but no observationStations
    public async Task GetObservationStationsUrl_MissingObservationStations_ReturnsNull(string json)
    {
        var handler = new StubHttpMessageHandler(RespondJson(json));
        var client = CreateClient(handler);

        var result = await client.GetObservationStationsUrl(47.6062, -122.3321, CancellationToken.None);

        Assert.Null(result);
    }

    // ----- GetStations -----

    /// <summary>
    /// Verifies that a successful stations response is deserialized into its features.
    /// </summary>
    [Fact]
    public async Task GetStations_Success_ReturnsFeatures()
    {
        const string json = "{\"features\":[{\"id\":\"abc\",\"properties\":{\"stationIdentifier\":\"KSEA\",\"name\":\"Seattle\"}}]}";
        var handler = new StubHttpMessageHandler(RespondJson(json));
        var client = CreateClient(handler);

        var result = await client.GetStations("https://api.weather.gov/gridpoints/SEW/124,68/stations", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Features);
        Assert.Equal("KSEA", result.Features[0].Properties.StationIdentifier);
    }

    /// <summary>
    /// Verifies that a stations response with no features resolves to an empty, non-null collection.
    /// </summary>
    [Fact]
    public async Task GetStations_NoFeatures_ReturnsEmptyCollection()
    {
        var handler = new StubHttpMessageHandler(RespondJson("{}"));
        var client = CreateClient(handler);

        var result = await client.GetStations("https://api.weather.gov/gridpoints/SEW/124,68/stations", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.Features);
    }

    // ----- StationHasObservations -----

    /// <summary>
    /// Verifies that the probe maps each status to the expected result: only a definitive 404 means the
    /// station has no observations; any other status (including non-404 errors) is treated as reporting.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadRequest, true)]
    public async Task StationHasObservations_ByStatus_ReturnsExpected(HttpStatusCode status, bool expected)
    {
        var handler = new StubHttpMessageHandler(RespondStatus(status));
        var client = CreateClient(handler);

        var result = await client.StationHasObservations(StationId, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that the probe issues a single request and never retries, even for a server error.
    /// </summary>
    [Fact]
    public async Task StationHasObservations_IssuesSingleRequestWithoutRetry()
    {
        var handler = new StubHttpMessageHandler(RespondStatus(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler, CreateConfig(maxRetries: 3));

        await client.StationHasObservations(StationId, CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
    }

    /// <summary>
    /// Verifies that a non-cancellation failure during the probe does not drop the station (returns true).
    /// </summary>
    [Theory]
    [InlineData("http")]              // transport error
    [InlineData("taskCanceled")]      // e.g. a per-request timeout
    [InlineData("operationCanceled")] // a cancellation not originating from the caller's token
    public async Task StationHasObservations_NonCancellationFailure_ReturnsTrue(string exceptionKind)
    {
        var handler = new StubHttpMessageHandler(Throw(ExceptionFor(exceptionKind)));
        var client = CreateClient(handler);

        var result = await client.StationHasObservations(StationId, CancellationToken.None);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that a per-request timeout during the probe does not drop the station (returns true).
    /// </summary>
    [Fact]
    public async Task StationHasObservations_PerRequestTimeout_ReturnsTrue()
    {
        var config = CreateConfig(requestTimeoutMs: 200);
        var handler = new StubHttpMessageHandler(BlockUntilCancelled());
        var client = CreateClient(handler, config);

        var result = await client.StationHasObservations(StationId, CancellationToken.None);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that a cancellation requested by the caller propagates out of the probe.
    /// </summary>
    [Fact]
    public async Task StationHasObservations_CallerCancels_Throws()
    {
        var handler = new StubHttpMessageHandler(RespondStatus(HttpStatusCode.OK));
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.StationHasObservations(StationId, cts.Token));
    }

    // ----- Helpers -----

    private static WeatherGovClient CreateClient(HttpMessageHandler handler, DataSourceConfiguration config = null) =>
        new(new HttpClient(handler), config ?? CreateConfig(), NullLogger.Instance);

    private static DataSourceConfiguration CreateConfig(
        int maxRetries = 2,
        int retryBackoffMs = 0,
        int requestTimeoutMs = 30000) =>
        WeatherGovTestData.CreateDataSourceConfiguration(maxRetries, retryBackoffMs, requestTimeoutMs);

    private static Func<CancellationToken, Task<HttpResponseMessage>> RespondJson(string json) =>
        StubHttpMessageHandler.RespondJson(json);

    private static Func<CancellationToken, Task<HttpResponseMessage>> RespondStatus(HttpStatusCode statusCode) =>
        StubHttpMessageHandler.RespondStatus(statusCode);

    private static Func<CancellationToken, Task<HttpResponseMessage>> Throw(Exception exception) =>
        StubHttpMessageHandler.Throw(exception);

    private static Exception ExceptionFor(string kind) => kind switch
    {
        "http" => new HttpRequestException("simulated transport failure"),
        "taskCanceled" => new TaskCanceledException("simulated timeout"),
        "operationCanceled" => new OperationCanceledException("simulated cancellation"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled exception kind."),
    };

    // Blocks until the request's (linked) token is cancelled, then surfaces the cancellation. Used to
    // drive the real per-request timeout path deterministically regardless of machine speed.
    private static Func<CancellationToken, Task<HttpResponseMessage>> BlockUntilCancelled() =>
        StubHttpMessageHandler.BlockUntilCancelled();
}
