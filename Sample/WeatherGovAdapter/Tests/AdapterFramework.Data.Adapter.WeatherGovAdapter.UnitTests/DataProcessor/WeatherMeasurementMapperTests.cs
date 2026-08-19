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

using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Globalization;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.DataProcessor;

/// <summary>
/// Unit tests for <see cref="WeatherMeasurementMapper"/> mapping: field population, unit conversion,
/// timestamp handling, and the drop-and-warn behavior for unrecognized or missing units. Envelope-level
/// null validation is owned by <see cref="MeasurementProcessor"/> and covered by its tests.
/// </summary>
public class WeatherMeasurementMapperTests
{
    private readonly WeatherMeasurementMapper _measurementMapper = new();

    /// <summary>
    /// Verifies that Map maps each source field to its expected measurement value.
    /// </summary>
    [Fact]
    public void Map_MapsExpectedFields()
    {
        var properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.FromHours(-5)),
            TextDescription = "Clear",
            Temperature = new QuantitativeValue { Value = 5.5, UnitCode = "wmoUnit:degC" },
            Dewpoint = new QuantitativeValue { Value = 1.2, UnitCode = "wmoUnit:degC" },
            RelativeHumidity = new QuantitativeValue { Value = 73.0, UnitCode = "wmoUnit:percent" },
            WindSpeed = new QuantitativeValue { Value = 4.4, UnitCode = "wmoUnit:m_s-1" },
            WindDirection = new QuantitativeValue { Value = 280.0, UnitCode = "wmoUnit:degree_(angle)" },
            BarometricPressure = new QuantitativeValue { Value = 100100, UnitCode = "wmoUnit:Pa" },
            Visibility = new QuantitativeValue { Value = 10000, UnitCode = "wmoUnit:m" },
        };

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        Assert.Equal(5.5, mapped.temperature_c);
        Assert.Equal(1.2, mapped.dewpoint_c);
        Assert.Equal(73.0, mapped.relative_humidity_pct);
        Assert.Equal(4.4, mapped.wind_speed_mps);
        Assert.Equal(280.0, mapped.wind_direction_deg);
        Assert.Equal(100100, mapped.barometric_pressure_pa);
        Assert.Equal(10000, mapped.visibility_m);
        Assert.Equal("Clear", mapped.text_description);
        // 01:02:03 at -05:00 converts to 06:02:03 UTC, proving UtcDateTime is applied.
        Assert.Equal(new DateTime(2026, 3, 11, 6, 2, 3, DateTimeKind.Utc), mapped.Timestamp);
    }

    /// <summary>
    /// Verifies that an observation with a null timestamp is dropped (maps to null) and warns through
    /// the injected logger.
    /// </summary>
    [Fact]
    public void Map_NullTimestamp_ReturnsNullAndWarns()
    {
        var logger = MockLoggerHelpers.CreateLogger();
        var properties = new ObservationProperties
        {
            Timestamp = null,
            Temperature = new QuantitativeValue { Value = 10.0 },
        };

        var mapped = _measurementMapper.Map("KSEA", properties, logger.Object);

        Assert.Null(mapped);
        VerifyWarning(logger, "no timestamp", Times.Once());
    }

    /// <summary>
    /// Verifies that a mappable observation logs no warning.
    /// </summary>
    [Fact]
    public void Map_WithTimestamp_LogsNoWarning()
    {
        // Positive direction: a mappable observation must not warn. Empty substring matches any warning,
        // so Never asserts nothing was logged on the success path. IsEnabled is set so a warning would be
        // observed if one were emitted (the source-generated logger checks IsEnabled before logging).
        var logger = MockLoggerHelpers.CreateLogger();

        var mapped = _measurementMapper.Map("KSEA", WeatherGovTestData.CreateFullObservation().Properties, logger.Object);

        Assert.NotNull(mapped);
        VerifyWarning(logger, string.Empty, Times.Never());
    }

    /// <summary>
    /// Verifies that missing optional source values map to null measurement fields.
    /// </summary>
    [Fact]
    public void Map_MissingOptionalValues_MapsNulls()
    {
        var properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.Zero),
            TextDescription = null,
            Temperature = null,
            Dewpoint = null,
            RelativeHumidity = null,
            WindSpeed = null,
            WindDirection = null,
            BarometricPressure = null,
            Visibility = null,
        };

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        Assert.NotNull(mapped);
        Assert.Null(mapped.temperature_c);
        Assert.Null(mapped.dewpoint_c);
        Assert.Null(mapped.relative_humidity_pct);
        Assert.Null(mapped.wind_speed_mps);
        Assert.Null(mapped.wind_direction_deg);
        Assert.Null(mapped.barometric_pressure_pa);
        Assert.Null(mapped.visibility_m);
        Assert.Null(mapped.text_description);
    }

    /// <summary>
    /// Verifies that the timestamp is converted to UTC using the offset embedded in the source value.
    /// </summary>
    [Theory]
    [InlineData(2026, 1, 15, 1, 2, 3, -5, "2026-01-15T06:02:03Z")]
    [InlineData(2026, 6, 1, 0, 0, 0, 0, "2026-06-01T00:00:00Z")]     // already UTC
    [InlineData(2026, 1, 1, 9, 30, 0, 9, "2026-01-01T00:30:00Z")]    // positive offset (UTC+9)
    [InlineData(2026, 3, 8, 3, 30, 0, -4, "2026-03-08T07:30:00Z")]   // US Eastern just after spring-forward (EDT, UTC-4)
    public void Map_ConvertsTimestampToUtcUsingEmbeddedOffset(
        int year, int month, int day, int hour, int minute, int second, int offsetHours, string expectedUtc)
    {
        var properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(offsetHours)),
            Temperature = new QuantitativeValue { Value = 1.0, UnitCode = "wmoUnit:degC" },
        };

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        // The mapper honors the offset embedded in the timestamp; it does no timezone lookup.
        var expected = DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;
        Assert.Equal(expected, mapped.Timestamp);
        Assert.Equal(DateTimeKind.Utc, mapped.Timestamp.Kind);
    }

    /// <summary>
    /// Verifies that an absent source field nulls only that field and leaves the rest intact.
    /// </summary>
    [Theory]
    [InlineData("temperature_c")]
    [InlineData("dewpoint_c")]
    [InlineData("relative_humidity_pct")]
    [InlineData("wind_speed_mps")]
    [InlineData("wind_direction_deg")]
    [InlineData("barometric_pressure_pa")]
    [InlineData("visibility_m")]
    [InlineData("text_description")]
    public void Map_AbsentSourceField_NullsOnlyThatField(string absentField)
    {
        var properties = WeatherGovTestData.CreateFullObservation().Properties;
        switch (absentField)
        {
            case "temperature_c": properties.Temperature = null; break;
            case "dewpoint_c": properties.Dewpoint = null; break;
            case "relative_humidity_pct": properties.RelativeHumidity = null; break;
            case "wind_speed_mps": properties.WindSpeed = null; break;
            case "wind_direction_deg": properties.WindDirection = null; break;
            case "barometric_pressure_pa": properties.BarometricPressure = null; break;
            case "visibility_m": properties.Visibility = null; break;
            case "text_description": properties.TextDescription = null; break;
            default: throw new ArgumentOutOfRangeException(nameof(absentField), absentField, "Unhandled field key.");
        }

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        Assert.NotNull(mapped);
        Assert.Equal(absentField == "temperature_c" ? null : 5.5, mapped.temperature_c);
        Assert.Equal(absentField == "dewpoint_c" ? null : 1.2, mapped.dewpoint_c);
        Assert.Equal(absentField == "relative_humidity_pct" ? null : 73.0, mapped.relative_humidity_pct);
        Assert.Equal(absentField == "wind_speed_mps" ? null : 4.4, mapped.wind_speed_mps);
        Assert.Equal(absentField == "wind_direction_deg" ? null : 280.0, mapped.wind_direction_deg);
        Assert.Equal(absentField == "barometric_pressure_pa" ? null : 100100.0, mapped.barometric_pressure_pa);
        Assert.Equal(absentField == "visibility_m" ? null : 10000.0, mapped.visibility_m);
        Assert.Equal(absentField == "text_description" ? null : "Clear", mapped.text_description);
    }

    /// <summary>
    /// Verifies that a wind speed reported in km/h (the unit the live API sends) is converted to m/s
    /// rather than written through unchanged - the A5 regression.
    /// </summary>
    [Fact]
    public void Map_WindSpeedKilometersPerHour_ConvertsToMetersPerSecond()
    {
        var properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.Zero),
            WindSpeed = new QuantitativeValue { Value = 36.0, UnitCode = "wmoUnit:km_h-1" },
        };

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        // 36 km/h = 10 m/s. Writing 36 straight through would be the bug.
        Assert.Equal(10.0, mapped.wind_speed_mps);
    }

    /// <summary>
    /// Verifies that a value with an unrecognized unit is dropped and warned, while the other fields on
    /// the same observation still map (the positive counterpart to the drop).
    /// </summary>
    [Fact]
    public void Map_UnrecognizedUnit_DropsValueAndWarnsButMapsOthers()
    {
        var logger = MockLoggerHelpers.CreateLogger();
        var properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.Zero),
            Temperature = new QuantitativeValue { Value = 20.0, UnitCode = "wmoUnit:degRankine" }, // unsupported temperature unit
            Dewpoint = new QuantitativeValue { Value = 1.2, UnitCode = "wmoUnit:degC" },      // valid neighbor
        };

        var mapped = _measurementMapper.Map("KSEA", properties, logger.Object);

        Assert.Null(mapped.temperature_c);     // dropped, not written mislabeled
        Assert.Equal(1.2, mapped.dewpoint_c);  // sibling still maps
        VerifyWarning(logger, "temperature_c", Times.Once());
    }

    /// <summary>
    /// Verifies that a value present with a missing unitCode is dropped and warned.
    /// </summary>
    [Fact]
    public void Map_MissingUnitCode_DropsValueAndWarns()
    {
        var logger = MockLoggerHelpers.CreateLogger();
        var properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.Zero),
            WindSpeed = new QuantitativeValue { Value = 4.4, UnitCode = null },
        };

        var mapped = _measurementMapper.Map("KSEA", properties, logger.Object);

        Assert.Null(mapped.wind_speed_mps);
        VerifyWarning(logger, "wind_speed_mps", Times.Once());
    }

    /// <summary>
    /// Verifies that each recognized temperature unit is converted to degrees Celsius.
    /// </summary>
    [Theory]
    [InlineData("wmoUnit:degC", 20.0, 20.0)]     // identity
    [InlineData("wmoUnit:degF", 212.0, 100.0)]   // Fahrenheit -> Celsius
    [InlineData("wmoUnit:K", 273.15, 0.0)]       // Kelvin -> Celsius
    public void Map_TemperatureUnits_ConvertToCelsius(string unitCode, double raw, double expected)
    {
        var properties = PropertiesWith(p => p.Temperature = new QuantitativeValue { Value = raw, UnitCode = unitCode });

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        Assert.Equal(expected, mapped.temperature_c!.Value, 4);
    }

    /// <summary>
    /// Verifies that each recognized wind-speed unit is converted to meters per second.
    /// </summary>
    [Theory]
    [InlineData("wmoUnit:m_s-1", 5.0, 5.0)]      // identity
    [InlineData("wmoUnit:km_h-1", 36.0, 10.0)]   // km/h -> m/s
    [InlineData("wmoUnit:kt", 10.0, 5.1444)]     // knots -> m/s (10 * 1852 / 3600)
    public void Map_WindSpeedUnits_ConvertToMetersPerSecond(string unitCode, double raw, double expected)
    {
        var properties = PropertiesWith(p => p.WindSpeed = new QuantitativeValue { Value = raw, UnitCode = unitCode });

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        Assert.Equal(expected, mapped.wind_speed_mps!.Value, 4);
    }

    /// <summary>
    /// Verifies that each recognized pressure unit is converted to pascals.
    /// </summary>
    [Theory]
    [InlineData("wmoUnit:Pa", 101325.0, 101325.0)]  // identity
    [InlineData("wmoUnit:hPa", 1013.25, 101325.0)]  // hectopascals -> pascals
    [InlineData("wmoUnit:kPa", 100.0, 100000.0)]    // kilopascals -> pascals
    public void Map_PressureUnits_ConvertToPascals(string unitCode, double raw, double expected)
    {
        var properties = PropertiesWith(p => p.BarometricPressure = new QuantitativeValue { Value = raw, UnitCode = unitCode });

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        Assert.Equal(expected, mapped.barometric_pressure_pa!.Value, 4);
    }

    /// <summary>
    /// Verifies that each recognized visibility unit is converted to meters.
    /// </summary>
    [Theory]
    [InlineData("wmoUnit:m", 10000.0, 10000.0)]  // identity
    [InlineData("wmoUnit:km", 10.0, 10000.0)]    // kilometers -> meters
    public void Map_VisibilityUnits_ConvertToMeters(string unitCode, double raw, double expected)
    {
        var properties = PropertiesWith(p => p.Visibility = new QuantitativeValue { Value = raw, UnitCode = unitCode });

        var mapped = _measurementMapper.Map("KSEA", properties, NullLogger.Instance);

        Assert.Equal(expected, mapped.visibility_m!.Value, 4);
    }

    // Builds observation properties with a fixed timestamp, then applies the mutation that sets the one
    // field under test, so each conversion arm can be exercised in isolation.
    private static ObservationProperties PropertiesWith(Action<ObservationProperties> configure)
    {
        var properties = new ObservationProperties
        {
            Timestamp = new DateTimeOffset(2026, 3, 11, 1, 2, 3, TimeSpan.Zero),
        };
        configure(properties);
        return properties;
    }

    // Delegates to the shared verification helper so the Moq log expression lives in exactly one place.
    private static void VerifyWarning(Mock<ILogger> logger, string contains, Times times) =>
        MockLoggerHelpers.VerifyWarning(logger, contains, times);
}