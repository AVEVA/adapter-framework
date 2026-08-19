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
using System.Linq;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter;

/// <summary>
/// Defines shared constants used by the Weather.gov adapter.
/// </summary>
public static class AdapterConstants
{
    /// <summary>
    /// Gets the adapter component type identifier.
    /// </summary>
    public const string ComponentType = "WeatherGovAdapter";

    /// <summary>
    /// Gets the named Weather.gov HTTP client registered with the factory.
    /// </summary>
    public const string ClientName = "weathergov";

    /// <summary>
    /// Gets the always-present timestamp field name used by observation measurements.
    /// </summary>
    public const string TimestampFieldName = "timestamp";

    /// <summary>
    /// Gets the default Weather.gov API base URL.
    /// </summary>
    public const string WeatherGovBaseUrl = "https://api.weather.gov";

    /// <summary>
    /// Gets the key prefix used for discovered station items.
    /// </summary>
    public const string DiscoveryKeyPrefix = "weathergov.station.";

    /// <summary>
    /// Gets the separator between discovery seed point pairs.
    /// </summary>
    public const char DiscoveryPairSeparator = ';';

    /// <summary>
    /// Gets the separator between latitude and longitude within a discovery seed point.
    /// </summary>
    public const char DiscoveryCoordinateSeparator = ',';

    // Selectable collection cadences. The user picks schedule "1", "2", or "3"; each ID maps to a
    // fixed period in the Schedules facet. weather.gov is rate-limited and observations update
    // roughly hourly, so one minute is the fastest offered option.

    /// <summary>
    /// Gets the schedule ID for one-minute collection, assigned to newly discovered stations.
    /// </summary>
    public const string DefaultScheduleId = "1";

    /// <summary>
    /// Gets the schedule ID for five-minute collection.
    /// </summary>
    public const string ScheduleIdFiveMinutes = "2";

    /// <summary>
    /// Gets the schedule ID for ten-minute collection.
    /// </summary>
    public const string ScheduleIdTenMinutes = "3";

    /// <summary>
    /// Gets the canonical collection period for each selectable schedule ID.
    /// </summary>
    /// <remarks>
    /// Single source of truth for the cadences the adapter seeds into the Schedules facet, so a fresh
    /// component collects at the documented rate instead of the framework's generic 5-second default.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, TimeSpan> SchedulePeriods =
        new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultScheduleId] = TimeSpan.FromMinutes(1),
            [ScheduleIdFiveMinutes] = TimeSpan.FromMinutes(5),
            [ScheduleIdTenMinutes] = TimeSpan.FromMinutes(10),
        };

    /// <summary>
    /// Gets the full set of schedule IDs a data selection item may reference.
    /// </summary>
    public static readonly IReadOnlySet<string> SelectableScheduleIds =
        SchedulePeriods.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Placeholder name the framework substitutes into DefaultStreamIdPattern, defined once so the
    // pattern and its keyword list cannot disagree.
    private const string StationIdKeyword = "StationId";

    /// <summary>
    /// Gets the default stream ID template applied to discovered stations. The framework applies the
    /// configured stream-ID prefix (with ComponentId fallback) at egress; this template only describes
    /// the adapter-specific portion.
    /// </summary>
    public static readonly string DefaultStreamIdPattern = $"station.{{{StationIdKeyword}}}";

    /// <summary>
    /// Gets the stream ID template keywords supported by the default pattern.
    /// </summary>
    public static readonly string[] DefaultStreamIdKeywords = [StationIdKeyword];

    /// <summary>
    /// Gets the measurement field names that can be selected for output. Derived from
    /// <see cref="DataProcessor.WeatherMeasurementMapper"/> so the selectable set, the mapped fields, and
    /// the registered data type share one definition.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedMeasurementFields =
        new DataProcessor.WeatherMeasurementMapper().SupportedFieldNames;
}
