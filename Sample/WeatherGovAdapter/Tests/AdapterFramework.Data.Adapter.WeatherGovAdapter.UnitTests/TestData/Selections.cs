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

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;

/// <summary>
/// Shared <see cref="DataSelectionItem"/> builders for the configuration tests.
/// </summary>
internal static class Selections
{
    /// <summary>
    /// A fully-populated, valid selection. Each call returns a fresh instance, so a test can override a
    /// single property and assert that property's effect (the equality mutation matrix depends on this).
    /// </summary>
    public static DataSelectionItem Baseline() => new()
    {
        Selected = true,
        Name = "Seattle Station",
        Id = "selection-1",
        StreamId = "weathergov.station.ksea",
        DataFilterId = "filter-1",
        StationId = "KSEA",
        ScheduleId = AdapterConstants.DefaultScheduleId,
        IncludeFields = ["temperature_c", "dewpoint_c"],
    };
}
