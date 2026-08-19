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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;

/// <summary>
/// Maps Weather.gov observation payloads to measurements and exposes field descriptors used by
/// metadata registration.
/// </summary>
internal partial class WeatherMeasurementMapper
{
    /// <summary>
    /// Describes one non-timestamp measurement field.
    /// </summary>
    public sealed class Field
    {
        public string Name { get; init; }

        public Type PropertyType { get; init; }

        public Action<ObservationProperties, string, ILogger, WeatherObservationMeasurement> Map { get; init; }
    }

    /// <summary>
    /// Gets the non-timestamp measurement fields, in output order.
    /// </summary>
    public IReadOnlyList<Field> Fields { get; }

    /// <summary>
    /// Gets the selectable measurement field names.
    /// </summary>
    public IReadOnlySet<string> SupportedFieldNames { get; }

    public WeatherMeasurementMapper()
    {
        Fields =
        [
            Numeric("temperature_c", ConvertCelsius, p => p.Temperature, (m, v) => m.temperature_c = v),
            Numeric("dewpoint_c", ConvertCelsius, p => p.Dewpoint, (m, v) => m.dewpoint_c = v),
            Numeric("relative_humidity_pct", ConvertPercent, p => p.RelativeHumidity, (m, v) => m.relative_humidity_pct = v),
            Numeric("wind_speed_mps", ConvertWindSpeed, p => p.WindSpeed, (m, v) => m.wind_speed_mps = v),
            Numeric("wind_direction_deg", ConvertAngle, p => p.WindDirection, (m, v) => m.wind_direction_deg = v),
            Numeric("barometric_pressure_pa", ConvertPascals, p => p.BarometricPressure, (m, v) => m.barometric_pressure_pa = v),
            Numeric("visibility_m", ConvertMeters, p => p.Visibility, (m, v) => m.visibility_m = v),
            Text("text_description", p => p.TextDescription, (m, v) => m.text_description = v),
        ];

        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AdapterConstants.TimestampFieldName };
        foreach (var field in Fields)
        {
            supported.Add(field.Name);
        }

        SupportedFieldNames = supported;
    }

    public WeatherObservationMeasurement Map(string stationId, ObservationProperties properties, ILogger logger)
    {
        if (!properties.Timestamp.HasValue)
        {
            LogMissingTimestamp(logger, stationId);
            return null;
        }

        var measurement = new WeatherObservationMeasurement
        {
            Timestamp = properties.Timestamp.Value.UtcDateTime,
        };

        foreach (var field in Fields)
        {
            field.Map(properties, stationId, logger, measurement);
        }

        return measurement;
    }

    public void ApplyIncludeFields(WeatherObservationMeasurement measurement, IEnumerable<string> includeFields)
    {
        measurement.ApplyIncludeFields(includeFields);
    }

    private static Field Numeric(
        string name,
        Func<QuantitativeValue, string, string, ILogger, double?> convert,
        Func<ObservationProperties, QuantitativeValue> source,
        Action<WeatherObservationMeasurement, double?> set) =>
        new()
        {
            Name = name,
            PropertyType = typeof(double),
            Map = (properties, stationId, logger, measurement) =>
                set(measurement, convert(source(properties), name, stationId, logger)),
        };

    private static Field Text(
        string name,
        Func<ObservationProperties, string> source,
        Action<WeatherObservationMeasurement, string> set) =>
        new()
        {
            Name = name,
            PropertyType = typeof(string),
            Map = (properties, _, _, measurement) => set(measurement, source(properties)),
        };

    private static double? Convert(
        QuantitativeValue value,
        string field,
        string stationId,
        ILogger logger,
        Func<double, string, double?> convert)
    {
        if (value?.Value is not double raw)
        {
            return null;
        }

        return convert(raw, value.UnitCode) ?? DropUnrecognizedUnit(logger, stationId, field, value.UnitCode);
    }

    private static double? ConvertCelsius(QuantitativeValue value, string field, string stationId, ILogger logger) =>
        Convert(value, field, stationId, logger, static (raw, unit) => unit switch
        {
            "wmoUnit:degC" => raw,
            "wmoUnit:degF" => (raw - 32.0) * 5.0 / 9.0,
            "wmoUnit:K" => raw - 273.15,
            _ => null,
        });

    private static double? ConvertPercent(QuantitativeValue value, string field, string stationId, ILogger logger) =>
        Convert(value, field, stationId, logger, static (raw, unit) => unit switch
        {
            "wmoUnit:percent" => raw,
            _ => null,
        });

    private static double? ConvertWindSpeed(QuantitativeValue value, string field, string stationId, ILogger logger) =>
        Convert(value, field, stationId, logger, static (raw, unit) => unit switch
        {
            "wmoUnit:m_s-1" => raw,
            "wmoUnit:km_h-1" => raw / 3.6,
            "wmoUnit:kt" => raw * 1852.0 / 3600.0,
            _ => null,
        });

    private static double? ConvertAngle(QuantitativeValue value, string field, string stationId, ILogger logger) =>
        Convert(value, field, stationId, logger, static (raw, unit) => unit switch
        {
            "wmoUnit:degree_(angle)" => raw,
            _ => null,
        });

    private static double? ConvertPascals(QuantitativeValue value, string field, string stationId, ILogger logger) =>
        Convert(value, field, stationId, logger, static (raw, unit) => unit switch
        {
            "wmoUnit:Pa" => raw,
            "wmoUnit:hPa" => raw * 100.0,
            "wmoUnit:kPa" => raw * 1000.0,
            _ => null,
        });

    private static double? ConvertMeters(QuantitativeValue value, string field, string stationId, ILogger logger) =>
        Convert(value, field, stationId, logger, static (raw, unit) => unit switch
        {
            "wmoUnit:m" => raw,
            "wmoUnit:km" => raw * 1000.0,
            _ => null,
        });

    private static double? DropUnrecognizedUnit(ILogger logger, string stationId, string field, string unitCode)
    {
        LogUnrecognizedUnit(logger, stationId, field, unitCode ?? "(missing)");
        return null;
    }

    [LoggerMessage(1, LogLevel.Warning, "Station {StationId}: observation has no timestamp; measurement dropped.")]
    static partial void LogMissingTimestamp(ILogger logger, string stationId);

    [LoggerMessage(2, LogLevel.Warning, "Station {StationId}: dropping {Field}; unrecognized unit '{UnitCode}'.")]
    static partial void LogUnrecognizedUnit(ILogger logger, string stationId, string field, string unitCode);
}