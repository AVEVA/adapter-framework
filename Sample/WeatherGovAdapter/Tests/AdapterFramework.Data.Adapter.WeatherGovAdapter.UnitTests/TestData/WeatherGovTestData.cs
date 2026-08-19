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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;
using Microsoft.Extensions.Logging.Abstractions;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;

/// <summary>
/// Shared WeatherGov test fixtures reused across the adapter's unit tests: a fully-populated
/// observation, its mapped measurement, and a valid selection item. Centralizing them here keeps
/// the test classes free of duplicated builders.
/// </summary>
internal static class WeatherGovTestData
{
    /// <summary>
    /// A fully-populated observation with every measurement field set and a fixed UTC timestamp.
    /// Tests that need an edge case (such as a missing timestamp) construct their own inline.
    /// </summary>
    public static ObservationResponse CreateFullObservation() => new()
    {
        Properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.Zero),
            TextDescription = "Clear",
            Temperature = new QuantitativeValue { Value = 5.5, UnitCode = "wmoUnit:degC" },
            Dewpoint = new QuantitativeValue { Value = 1.2, UnitCode = "wmoUnit:degC" },
            RelativeHumidity = new QuantitativeValue { Value = 73.0, UnitCode = "wmoUnit:percent" },
            WindSpeed = new QuantitativeValue { Value = 4.4, UnitCode = "wmoUnit:m_s-1" },
            WindDirection = new QuantitativeValue { Value = 280.0, UnitCode = "wmoUnit:degree_(angle)" },
            BarometricPressure = new QuantitativeValue { Value = 100100, UnitCode = "wmoUnit:Pa" },
            Visibility = new QuantitativeValue { Value = 10000, UnitCode = "wmoUnit:m" },
        },
    };

    /// <summary>
    /// <see cref="CreateFullObservation"/> mapped to a measurement for station KSEA.
    /// </summary>
    public static WeatherObservationMeasurement CreateMappedMeasurementFromFullObservation() =>
        new WeatherMeasurementMapper().Map("KSEA", CreateFullObservation().Properties, NullLogger.Instance);

    /// <summary>
    /// A valid selection item for station KSEA with a canned stream id. The stream id is opaque here
    /// (production composition is delegated to the framework's stream-id generator); override it to
    /// exercise stream-routing or blank-stream-id paths.
    /// </summary>
    public static DataSelectionItem CreateValidSelectionItem(string streamId = "WEATHERGOV.STATION.KSEA") => new()
    {
        Id = "KSEA",
        StationId = "KSEA",
        Name = "KSEA Station",
        StreamId = streamId,
    };

    /// <summary>
    /// A fully-valid data source configuration: HTTPS base URL, a positive timeout value,
    /// non-negative retry settings, and a non-blank user agent. Tests mutate a single field to drive one
    /// validation rule or one equality comparison.
    /// </summary>
    public static DataSourceConfiguration CreateValidDataSourceConfiguration() => new()
    {
        BaseUrl = AdapterConstants.WeatherGovBaseUrl,
        RequestTimeoutMs = 10000,
        MaxRetries = 3,
        RetryBackoffMs = 1000,
        UserAgent = "WeatherGovAdapterSample/1.0 (test@example.com)",
        DropNullMeasurements = true,
    };

    /// <summary>
    /// A valid HTTPS data source configuration with the retry/timeout knobs exposed as parameters, so a
    /// test can vary a single behavior (retries, backoff, timeout, user agent, or station ids) while the
    /// rest stay at sane defaults. Shared by the client and adapter test suites.
    /// </summary>
    public static DataSourceConfiguration CreateDataSourceConfiguration(
        int maxRetries = 2,
        int retryBackoffMs = 0,
        int requestTimeoutMs = 30000,
        string userAgent = "WeatherGovAdapterTests/1.0 (test@example.com)") =>
        new()
        {
            BaseUrl = AdapterConstants.WeatherGovBaseUrl,
            MaxRetries = maxRetries,
            RetryBackoffMs = retryBackoffMs,
            RequestTimeoutMs = requestTimeoutMs,
            UserAgent = userAgent,
        };
}
