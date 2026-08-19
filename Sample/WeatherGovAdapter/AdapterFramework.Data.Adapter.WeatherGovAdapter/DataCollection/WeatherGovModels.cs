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
using System.Text.Json.Serialization;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;

/// <summary>
/// Represents the Weather.gov point lookup response.
/// </summary>
internal sealed class PointResponse
{
    /// <summary>
    /// Gets or sets the point response properties payload.
    /// </summary>
    [JsonPropertyName("properties")]
    public PointProperties Properties { get; set; }
}

/// <summary>
/// Represents point metadata returned by Weather.gov.
/// </summary>
internal sealed class PointProperties
{
    /// <summary>
    /// Gets or sets the URL for the related observation stations collection.
    /// </summary>
    [JsonPropertyName("observationStations")]
    public string ObservationStations { get; set; }
}

/// <summary>
/// Represents the Weather.gov station collection response.
/// </summary>
internal sealed class StationsResponse
{
    /// <summary>
    /// Gets or sets the station features returned in the response.
    /// </summary>
    [JsonPropertyName("features")]
    public List<StationFeature> Features { get; set; } = new();
}

/// <summary>
/// Represents a Weather.gov station feature.
/// </summary>
internal sealed class StationFeature
{
    /// <summary>
    /// Gets or sets the station feature identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the station feature properties.
    /// </summary>
    [JsonPropertyName("properties")]
    public StationProperties Properties { get; set; }
}

/// <summary>
/// Represents Weather.gov station metadata.
/// </summary>
internal sealed class StationProperties
{
    /// <summary>
    /// Gets or sets the Weather.gov station identifier.
    /// </summary>
    [JsonPropertyName("stationIdentifier")]
    public string StationIdentifier { get; set; }

    /// <summary>
    /// Gets or sets the display name of the station.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

/// <summary>
/// Represents the Weather.gov latest observation response.
/// </summary>
internal sealed class ObservationResponse
{
    /// <summary>
    /// Gets or sets the observation properties payload.
    /// </summary>
    [JsonPropertyName("properties")]
    public ObservationProperties Properties { get; set; }
}

/// <summary>
/// Represents observation values returned by Weather.gov.
/// </summary>
internal sealed class ObservationProperties
{
    /// <summary>
    /// Gets or sets the observation timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the textual weather description.
    /// </summary>
    [JsonPropertyName("textDescription")]
    public string TextDescription { get; set; }

    /// <summary>
    /// Gets or sets the air temperature value.
    /// </summary>
    [JsonPropertyName("temperature")]
    public QuantitativeValue Temperature { get; set; }

    /// <summary>
    /// Gets or sets the dew point value.
    /// </summary>
    [JsonPropertyName("dewpoint")]
    public QuantitativeValue Dewpoint { get; set; }

    /// <summary>
    /// Gets or sets the relative humidity value.
    /// </summary>
    [JsonPropertyName("relativeHumidity")]
    public QuantitativeValue RelativeHumidity { get; set; }

    /// <summary>
    /// Gets or sets the wind speed value.
    /// </summary>
    [JsonPropertyName("windSpeed")]
    public QuantitativeValue WindSpeed { get; set; }

    /// <summary>
    /// Gets or sets the wind direction value.
    /// </summary>
    [JsonPropertyName("windDirection")]
    public QuantitativeValue WindDirection { get; set; }

    /// <summary>
    /// Gets or sets the barometric pressure value.
    /// </summary>
    [JsonPropertyName("barometricPressure")]
    public QuantitativeValue BarometricPressure { get; set; }

    /// <summary>
    /// Gets or sets the visibility value.
    /// </summary>
    [JsonPropertyName("visibility")]
    public QuantitativeValue Visibility { get; set; }
}

/// <summary>
/// Represents a numeric Weather.gov value with units.
/// </summary>
internal sealed class QuantitativeValue
{
    /// <summary>
    /// Gets or sets the numeric value.
    /// </summary>
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    /// <summary>
    /// Gets or sets the unit code for the value.
    /// </summary>
    [JsonPropertyName("unitCode")]
    public string UnitCode { get; set; }
}