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
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;

/// <summary>
/// Represents a Weather.gov observation measurement with timestamped field values.
/// </summary>
[DataContract]
internal sealed class WeatherObservationMeasurement
{
    /// <summary>
    /// Gets or sets the timestamp of the observation.
    /// </summary>
    [Key]
    [DataMember(Name = "timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the air temperature in degrees Celsius.
    /// </summary>
    [DataMember(Name = "temperature_c", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? temperature_c { get; set; }

    /// <summary>
    /// Gets or sets the dew point in degrees Celsius.
    /// </summary>
    [DataMember(Name = "dewpoint_c", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? dewpoint_c { get; set; }

    /// <summary>
    /// Gets or sets the relative humidity percentage.
    /// </summary>
    [DataMember(Name = "relative_humidity_pct", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? relative_humidity_pct { get; set; }

    /// <summary>
    /// Gets or sets the wind speed in meters per second.
    /// </summary>
    [DataMember(Name = "wind_speed_mps", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? wind_speed_mps { get; set; }

    /// <summary>
    /// Gets or sets the wind direction in degrees.
    /// </summary>
    [DataMember(Name = "wind_direction_deg", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? wind_direction_deg { get; set; }

    /// <summary>
    /// Gets or sets the barometric pressure in pascals.
    /// </summary>
    [DataMember(Name = "barometric_pressure_pa", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? barometric_pressure_pa { get; set; }

    /// <summary>
    /// Gets or sets the visibility in meters.
    /// </summary>
    [DataMember(Name = "visibility_m", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? visibility_m { get; set; }

    /// <summary>
    /// Gets or sets the textual weather description.
    /// </summary>
    [DataMember(Name = "text_description", EmitDefaultValue = false)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string text_description { get; set; }

    /// <summary>
    /// Determines whether the measurement contains at least one value other than the timestamp.
    /// </summary>
    /// <returns><see langword="true"/> when at least one non-timestamp field is populated; otherwise, <see langword="false"/>.</returns>
    public bool HasAtLeastOneNonTimestampValue() =>
        temperature_c.HasValue ||
        dewpoint_c.HasValue ||
        relative_humidity_pct.HasValue ||
        wind_speed_mps.HasValue ||
        wind_direction_deg.HasValue ||
        barometric_pressure_pa.HasValue ||
        visibility_m.HasValue ||
        !string.IsNullOrWhiteSpace(text_description);

    /// <summary>
    /// Removes measurement fields that are not present in the provided include list.
    /// </summary>
    /// <param name="includeFields">The set of field names that should remain populated.</param>
    public void ApplyIncludeFields(IEnumerable<string> includeFields)
    {
        var selected = new HashSet<string>(includeFields ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            return;
        }

        if (!selected.Contains("temperature_c")) temperature_c = null;
        if (!selected.Contains("dewpoint_c")) dewpoint_c = null;
        if (!selected.Contains("relative_humidity_pct")) relative_humidity_pct = null;
        if (!selected.Contains("wind_speed_mps")) wind_speed_mps = null;
        if (!selected.Contains("wind_direction_deg")) wind_direction_deg = null;
        if (!selected.Contains("barometric_pressure_pa")) barometric_pressure_pa = null;
        if (!selected.Contains("visibility_m")) visibility_m = null;
        if (!selected.Contains("text_description")) text_description = null;
    }
}
