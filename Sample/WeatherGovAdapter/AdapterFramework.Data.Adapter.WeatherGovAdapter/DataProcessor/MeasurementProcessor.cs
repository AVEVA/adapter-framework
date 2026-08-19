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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Interfaces;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;

/// <summary>
/// Transforms raw Weather.gov responses into measurement objects
/// and applies field-level filtering based on selection configuration.
/// </summary>
internal class MeasurementProcessor : IMeasurementProcessor
{
    private readonly WeatherMeasurementMapper _measurementMapper;

    public MeasurementProcessor(WeatherMeasurementMapper measurementMapper)
    {
        _measurementMapper = measurementMapper ?? throw new ArgumentNullException(nameof(measurementMapper));
    }

    /// <summary>
    /// Processes an observation response into a measurement.
    /// </summary>
    /// <param name="response">The raw Weather.gov observation response.</param>
    /// <param name="item">The selection item containing filtering configuration.</param>
    /// <param name="logger">Logger used to record values dropped for unrecognized units.</param>
    /// <returns>A processed and filtered weather observation measurement.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="response"/>, its <see cref="ObservationResponse.Properties"/>, or
    /// <paramref name="item"/> is <see langword="null"/>.
    /// </exception>
    public WeatherObservationMeasurement Process(ObservationResponse response, DataSelectionItem item, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(response.Properties);

        var measurement = _measurementMapper.Map(item.StationId, response.Properties, logger);

        if (measurement == null)
            return null;

        _measurementMapper.ApplyIncludeFields(measurement, item.IncludeFields);

        return measurement;
    }
}
