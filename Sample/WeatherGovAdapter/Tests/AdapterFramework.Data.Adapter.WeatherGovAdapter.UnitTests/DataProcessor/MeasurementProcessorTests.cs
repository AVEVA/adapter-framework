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

using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.DataProcessor;

/// <summary>
/// Unit tests for <see cref="MeasurementProcessor"/> and the
/// <see cref="WeatherObservationMeasurement.ApplyIncludeFields"/> filtering it relies on.
/// </summary>
public class MeasurementProcessorTests
{
    private readonly WeatherMeasurementMapper _measurementMapper = new();
    private readonly MeasurementProcessor _processor;

    /// <summary>
    /// Creates the test subject with the shared mapper instance used by the production path.
    /// </summary>
    public MeasurementProcessorTests()
    {
        _processor = new MeasurementProcessor(_measurementMapper);
    }

    //  Process: orchestration + null short-circuit 

    /// <summary>
    /// Verifies that Process rejects a null response, a null response.Properties, or a null selection
    /// item at the boundary, throwing ArgumentNullException that names the offending parameter, so a
    /// caller misuse fails with the correct parameter context instead of a NullReferenceException deep
    /// inside the mapper.
    /// </summary>
    [Theory]
    [InlineData("response", "response")]
    [InlineData("properties", "response.Properties")]  // response is present but its Properties is null
    [InlineData("item", "item")]
    public void Process_NullArgument_Throws(string nullParameter, string expectedParamName)
    {
        var response = nullParameter switch
        {
            "response" => null,
            "properties" => new ObservationResponse { Properties = null },
            _ => new ObservationResponse { Properties = new ObservationProperties { Timestamp = null } },
        };
        var item = nullParameter == "item" ? null : new DataSelectionItem { StationId = "KSEA" };

        var exception = Assert.Throws<ArgumentNullException>(
            () => _processor.Process(response, item, NullLogger.Instance));

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    /// <summary>
    /// Verifies that Process returns null when mapping drops the observation.
    /// </summary>
    [Fact]
    public void Process_DroppedMeasurement_ReturnsNull()
    {
        // No timestamp means MapObservation returns null, so Process short-circuits
        // before ApplyIncludeFields.
        var response = new ObservationResponse
        {
            Properties = new ObservationProperties { Timestamp = null },
        };
        var item = new DataSelectionItem { StationId = "KSEA", IncludeFields = ["temperature_c"] };

        var result = _processor.Process(response, item, NullLogger.Instance);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that Process applies the include-fields filter, keeping everything for a null list
    /// and only the named fields otherwise.
    /// </summary>
    [Theory]
    // A null include list is a no-op in ApplyIncludeFields, so every mapped field passes through; an explicit
    // subset keeps only the named fields and nulls the rest. (Case/whitespace handling of the names is pinned by
    // ApplyIncludeFields_RetainsOnlySelectedFields; here the point is that Process delegates filtering both ways.)
    [InlineData(null, "temperature_c,dewpoint_c,relative_humidity_pct,wind_speed_mps,wind_direction_deg,barometric_pressure_pa,visibility_m,text_description")]  // null include list keeps everything
    [InlineData("temperature_c,text_description", "temperature_c,text_description")]                                                                              // explicit subset keeps only those
    public void Process_AppliesIncludeFieldsFilter(string includeCsv, string expectedRetainedCsv)
    {
        var item = new DataSelectionItem
        {
            StationId = "KSEA",
            IncludeFields = includeCsv?.Split(','),
        };
        var retained = new HashSet<string>(expectedRetainedCsv.Split(','), StringComparer.Ordinal);

        var result = _processor.Process(WeatherGovTestData.CreateFullObservation(), item, NullLogger.Instance);

        Assert.NotNull(result);
        // A field keeps its full-observation value only when retained; otherwise Process nulls it.
        Assert.Equal(retained.Contains("temperature_c") ? 5.5 : null, result.temperature_c);
        Assert.Equal(retained.Contains("dewpoint_c") ? 1.2 : null, result.dewpoint_c);
        Assert.Equal(retained.Contains("relative_humidity_pct") ? 73.0 : null, result.relative_humidity_pct);
        Assert.Equal(retained.Contains("wind_speed_mps") ? 4.4 : null, result.wind_speed_mps);
        Assert.Equal(retained.Contains("wind_direction_deg") ? 280.0 : null, result.wind_direction_deg);
        Assert.Equal(retained.Contains("barometric_pressure_pa") ? 100100.0 : null, result.barometric_pressure_pa);
        Assert.Equal(retained.Contains("visibility_m") ? 10000.0 : null, result.visibility_m);
        Assert.Equal(retained.Contains("text_description") ? "Clear" : null, result.text_description);
        // Timestamp is never touched by filtering, regardless of the include list.
        Assert.Equal(new DateTime(2026, 3, 11, 1, 2, 3, DateTimeKind.Utc), result.Timestamp);
    }

    /// <summary>
    /// Verifies that Process threads the caller's logger into the mapper, so a value dropped for an
    /// unrecognized unit is warned through that logger (B1).
    /// </summary>
    [Fact]
    public void Process_UnrecognizedUnit_WarnsThroughProvidedLogger()
    {
        var logger = MockLoggerHelpers.CreateLogger();
        var response = new ObservationResponse
        {
            Properties = new ObservationProperties
            {
                Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.Zero),
                WindSpeed = new QuantitativeValue { Value = 4.4, UnitCode = "bogus" },
            },
        };
        var item = new DataSelectionItem { StationId = "KSEA", IncludeFields = ["wind_speed_mps"] };

        var result = _processor.Process(response, item, logger.Object);

        Assert.Null(result.wind_speed_mps);
        MockLoggerHelpers.VerifyWarning(logger, "wind_speed_mps", Times.Once());
    }

    //  ApplyIncludeFields: filtering semantics 

    /// <summary>
    /// Verifies that a null or empty include list keeps every field.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]  // an empty include list keeps everything
    public void ApplyIncludeFields_NullOrEmpty_KeepsEverything(string includeListScenario)
    {
        var measurement = WeatherGovTestData.CreateMappedMeasurementFromFullObservation();
        var includeFields = includeListScenario == "null" ? null : Array.Empty<string>();

        measurement.ApplyIncludeFields(includeFields);

        // A null or empty include list is a no-op: every field keeps its full-observation value.
        Assert.Equal(5.5, measurement.temperature_c);
        Assert.Equal(1.2, measurement.dewpoint_c);
        Assert.Equal(73.0, measurement.relative_humidity_pct);
        Assert.Equal("Clear", measurement.text_description);
    }

    // Filtering retains exactly the selected fields and nulls the rest. Matching is
    // case-insensitive, surrounding whitespace is NOT trimmed, and unknown names match nothing.
    /// <summary>
    /// Verifies that filtering retains only the selected fields, matching case-insensitively and
    /// without trimming whitespace.
    /// </summary>
    [Theory]
    [InlineData("temperature_c", "temperature_c")]
    [InlineData("temperature_c,wind_speed_mps", "temperature_c,wind_speed_mps")]     // multiple fields
    [InlineData("TEMPERATURE_C", "temperature_c")]                                   // upper-case matches
    [InlineData("Temperature_C", "temperature_c")]                                   // mixed-case matches
    [InlineData("temperature_c,not_a_field", "temperature_c")]                       // unknown name ignored
    [InlineData("not_a_field", "")]                                                  // no real field matched all nulled
    [InlineData(" temperature_c ", "")]                                              // surrounding whitespace not trimmed, so dropped
    public void ApplyIncludeFields_RetainsOnlySelectedFields(string includeCsv, string expectedRetainedCsv)
    {
        var measurement = WeatherGovTestData.CreateMappedMeasurementFromFullObservation();
        var includeFields = includeCsv.Split(',');
        var retained = new HashSet<string>(
            expectedRetainedCsv.Length == 0 ? Array.Empty<string>() : expectedRetainedCsv.Split(','),
            StringComparer.Ordinal);

        measurement.ApplyIncludeFields(includeFields);

        // A field keeps its full-observation value only when it is in the expected retained set; otherwise it is nulled.
        Assert.Equal(retained.Contains("temperature_c") ? 5.5 : null, measurement.temperature_c);
        Assert.Equal(retained.Contains("dewpoint_c") ? 1.2 : null, measurement.dewpoint_c);
        Assert.Equal(retained.Contains("relative_humidity_pct") ? 73.0 : null, measurement.relative_humidity_pct);
        Assert.Equal(retained.Contains("wind_speed_mps") ? 4.4 : null, measurement.wind_speed_mps);
        Assert.Equal(retained.Contains("wind_direction_deg") ? 280.0 : null, measurement.wind_direction_deg);
        Assert.Equal(retained.Contains("barometric_pressure_pa") ? 100100.0 : null, measurement.barometric_pressure_pa);
        Assert.Equal(retained.Contains("visibility_m") ? 10000.0 : null, measurement.visibility_m);
        Assert.Equal(retained.Contains("text_description") ? "Clear" : null, measurement.text_description);
    }

    //  HasAtLeastOneNonTimestampValue: suppression predicate (both directions) 

    /// <summary>
    /// Verifies that HasAtLeastOneNonTimestampValue reflects whether any non-timestamp field is present.
    /// </summary>
    [Theory]
    // Only the timestamp is set, yielding false, proving the timestamp itself never counts as a value.
    [InlineData("none", false)]
    // Each non-timestamp field, set in isolation, is enough to count as a value.
    [InlineData("temperature_c", true)]
    [InlineData("dewpoint_c", true)]
    [InlineData("relative_humidity_pct", true)]
    [InlineData("wind_speed_mps", true)]
    [InlineData("wind_direction_deg", true)]
    [InlineData("barometric_pressure_pa", true)]
    [InlineData("visibility_m", true)]
    [InlineData("text_description", true)]
    // A zero numeric value still counts: presence is HasValue, not truthiness.
    [InlineData("temperature_zero", true)]
    // Whitespace-only text does NOT count: the text check is IsNullOrWhiteSpace, not null-only.
    [InlineData("text_whitespace", false)]
    public void HasAtLeastOneNonTimestampValue_ReflectsFieldPresence(string presentField, bool expected)
    {
        // Timestamp is always set so every row also proves the timestamp alone is never "a value".
        var measurement = new WeatherObservationMeasurement
        {
            Timestamp = new DateTime(2026, 3, 11, 1, 2, 3, DateTimeKind.Utc),
        };
        switch (presentField)
        {
            case "none": break;
            case "temperature_c": measurement.temperature_c = 5.5; break;
            case "dewpoint_c": measurement.dewpoint_c = 1.2; break;
            case "relative_humidity_pct": measurement.relative_humidity_pct = 73.0; break;
            case "wind_speed_mps": measurement.wind_speed_mps = 4.4; break;
            case "wind_direction_deg": measurement.wind_direction_deg = 280.0; break;
            case "barometric_pressure_pa": measurement.barometric_pressure_pa = 100100.0; break;
            case "visibility_m": measurement.visibility_m = 10000.0; break;
            case "text_description": measurement.text_description = "Clear"; break;
            case "temperature_zero": measurement.temperature_c = 0.0; break;
            case "text_whitespace": measurement.text_description = "   "; break;
            default: throw new ArgumentOutOfRangeException(nameof(presentField), presentField, "Unhandled field key.");
        }

        Assert.Equal(expected, measurement.HasAtLeastOneNonTimestampValue());
    }
}
