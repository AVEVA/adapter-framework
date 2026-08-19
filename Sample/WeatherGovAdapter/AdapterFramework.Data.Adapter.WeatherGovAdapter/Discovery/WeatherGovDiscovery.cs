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
using System.Globalization;
using System.Linq;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.Discovery;

internal class WeatherGovDiscovery
{
    private readonly ILogger _logger;
    private readonly Func<DataSelectionItem, string> _getDefaultStreamId;

    public WeatherGovDiscovery(ILogger logger, Func<DataSelectionItem, string> getDefaultStreamId)
    {
        ArgumentNullException.ThrowIfNull(getDefaultStreamId);
        _logger = logger;
        _getDefaultStreamId = getDefaultStreamId;
    }

    public IReadOnlyList<DiscoverySeedPoint> ParseQuerySeedPoints(string discoveryQuery)
    {
        var seeds = new List<DiscoverySeedPoint>();
        if (string.IsNullOrWhiteSpace(discoveryQuery))
        {
            return seeds;
        }

        var pairs = discoveryQuery.Split(AdapterConstants.DiscoveryPairSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            // Keep TrimEntries but not RemoveEmptyEntries on the comma split: an empty coordinate such as
            // "47.61,,-122.33" must survive as a third element so the values.Length != 2 guard rejects it,
            // instead of collapsing to (47.61, -122.33). RemoveEmptyEntries stays on the semicolon split above.
            var values = pair.Split(AdapterConstants.DiscoveryCoordinateSeparator, StringSplitOptions.TrimEntries);
            if (values.Length != 2)
            {
                _logger?.LogWarning("Skipping discovery seed point '{Pair}': expected 'latitude,longitude'.", pair);
                continue;
            }

            if (!double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                _logger?.LogWarning("Skipping discovery seed point '{Pair}': coordinates are not valid numbers.", pair);
                continue;
            }

            // The range test lives in DiscoverySeedPoint.HasValidCoordinates so this query path and
            // config validation share one definition of "in range".
            var seed = new DiscoverySeedPoint(lat, lon);
            if (!seed.HasValidCoordinates())
            {
                _logger?.LogWarning(
                    "Skipping discovery seed point '{Pair}': latitude must be in [{MinLat}, {MaxLat}] and longitude in [{MinLon}, {MaxLon}].",
                    pair,
                    DiscoverySeedPoint.MinLatitude,
                    DiscoverySeedPoint.MaxLatitude,
                    DiscoverySeedPoint.MinLongitude,
                    DiscoverySeedPoint.MaxLongitude);
                continue;
            }

            seeds.Add(seed);
        }

        return seeds;
    }

    public DataSelectionItem BuildSelectionItem(string stationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);

        var canonicalStationId = stationId.Trim().ToUpperInvariant();
        var item = new DataSelectionItem
        {
            Id = $"{AdapterConstants.DiscoveryKeyPrefix}{canonicalStationId}",
            Name = $"Weather station {canonicalStationId}",
            Selected = true,
            StationId = canonicalStationId,
            IncludeFields = AdapterConstants.SupportedMeasurementFields.ToArray(),
            ScheduleId = AdapterConstants.DefaultScheduleId,
        };

        // Delegate stream-id composition to the framework's default stream-id generator (via the
        // adapter's GetDefaultStreamId override) so the configured StreamIdPrefix and its
        // ComponentId fallback are applied in exactly one place.
        item.StreamId = _getDefaultStreamId(item);
        return item;
    }
}