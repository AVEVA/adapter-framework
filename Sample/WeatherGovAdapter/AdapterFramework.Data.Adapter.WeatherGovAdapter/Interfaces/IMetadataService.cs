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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.Interfaces;

/// <summary>
/// Registers the Weather.gov measurement type and per-stream metadata with the adapter framework and
/// writes measurements. Extracted behind an interface so the adapter can be unit-tested with a
/// substitute that records framework writes, instead of requiring a live CommonService.
/// </summary>
internal interface IMetadataService
{
    /// <summary>
    /// Ensures the Weather Observation data type is registered with the framework exactly once.
    /// </summary>
    void EnsureTypeRegistered();

    /// <summary>
    /// Writes a measurement to the framework, registering the item's stream on its first write.
    /// </summary>
    /// <param name="item">The selection item the measurement belongs to.</param>
    /// <param name="measurement">The measurement to write.</param>
    /// <param name="logger">Optional logger used to record measurements dropped because of a missing stream id.</param>
    void Write(DataSelectionItem item, WeatherObservationMeasurement measurement, ILogger logger = null);
}