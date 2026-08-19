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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Interfaces;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;

/// <summary>
/// Handles metadata registration and data writes to the adapter framework.
/// Ensures types and streams are created before sending values.
/// </summary>
internal partial class MetadataService : IMetadataService
{
    private const string SendingTypeFormat = "Sending type {TypeName} with id {TypeId}.";
    private const string SendingStreamFormat = "Sending stream {StreamName} with StreamId {StreamId} and typeId {StreamTypeId}.";
    private const string SendingMeasurementFormat = "Sending measurement for stream {StreamId}: {MeasurementPayload}";

    // Delegates used to access framework services safely
    private readonly Func<IAdapterMessageProcessor> _getProcessor;
    private readonly Func<string> _getTypeId;

    // Tracks created streams to avoid duplicate registrations
    private readonly HashSet<string> _streams =
        new(StringComparer.OrdinalIgnoreCase);

    // Cached type definition
    private DataType _type;

    private readonly WeatherMeasurementMapper _measurementMapper;

    // Set once the type has been written, so hot-path writes skip the registration lock entirely.
    private volatile bool _typeRegistered;

    // Synchronization lock for thread safety
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the metadata service.
    /// </summary>
    /// <param name="getProcessor">Delegate that returns the message processor.</param>
    /// <param name="getTypeId">Delegate that returns the data type identifier.</param>
    /// <param name="measurementMapper">Mapper that defines measurement fields and their CLR types.</param>
    public MetadataService(
        Func<IAdapterMessageProcessor> getProcessor,
        Func<string> getTypeId,
        WeatherMeasurementMapper measurementMapper)
    {
        _getProcessor = getProcessor;
        _getTypeId = getTypeId;
        _measurementMapper = measurementMapper ?? throw new ArgumentNullException(nameof(measurementMapper));
    }

    /// <summary>
    /// Ensures the Weather Observation data type is registered once.
    /// </summary>
    public void EnsureTypeRegistered()
    {
        EnsureTypeRegistered(logger: null);
    }

    /// <summary>
    /// Ensures the Weather Observation data type is registered once and logs the registration.
    /// </summary>
    public void EnsureTypeRegistered(ILogger logger = null)
    {
        if (_typeRegistered)
            return;

        var messageProcessor = _getProcessor();

        if (messageProcessor == null)
            return;

        lock (_lock)
        {
            if (_typeRegistered)
                return;

            // Timestamp is the index/key; the value fields come from the shared measurement schema so the
            // registered type stays in lockstep with mapping and filtering.
            var propertiesDictionary = new Dictionary<string, (Type PropertyType, bool IsIndex, bool IsQuality, string QualitySchema)>
            {
                ["Timestamp"] = (typeof(DateTime), true, false, null),
            };

            foreach (var field in _measurementMapper.Fields)
            {
                propertiesDictionary[field.Name] = (field.PropertyType, false, false, null);
            }

            var properties = new ReadOnlyDictionary<string, (Type PropertyType, bool IsIndex, bool IsQuality, string QualitySchema)>(propertiesDictionary);

            var type = new DynamicDataType(
                _getTypeId(),
                "WeatherObservationMeasurement",
                properties);

            // Publish _type and mark registration complete only after the framework accepts the type. If
            // WriteType throws, _type stays null and _typeRegistered stays false, so the next call retries
            // instead of leaving a poisoned service that writes values for a type the framework never got.
            messageProcessor.WriteType(type);
            _type = type;
            _typeRegistered = true;

            if (logger != null)
            {
                LogSendingType(logger, type.Name, type.Id);
            }
        }
    }

    /// <summary>
    /// Writes a measurement to the adapter framework.
    /// Ensures type and stream are registered before sending data.
    /// </summary>
    /// <param name="item">Selection item associated with the measurement.</param>
    /// <param name="measurement">Measurement to write.</param>
    /// <param name="logger">Optional logger used to record measurements dropped because of a missing stream id.</param>
    public void Write(
        DataSelectionItem item,
        WeatherObservationMeasurement measurement,
        ILogger logger = null)
    {
        var messageProcessor = _getProcessor();

        if (messageProcessor == null)
            return;

        EnsureTypeRegistered(logger);

        // Never emit a stream or value before the type is registered: a failed or not-yet-ready
        // registration must not produce data for a type the framework never received.
        if (!_typeRegistered)
            return;

        if (string.IsNullOrWhiteSpace(item.StreamId))
        {
            // StreamId is [Required] on DataSelectionItem, so this guard is defensive. Debug keeps
            // the drop diagnosable without adding noise on the hot path.
            logger?.LogDebug(
                "Dropping measurement for selection '{SelectionId}' because its stream id is missing.",
                item.Id);
            return;
        }

        // Hold the lock across both writes so a concurrent first sample for the same stream cannot
        // send a value before the stream metadata write has been issued.
        lock (_lock)
        {
            if (_streams.Add(item.StreamId))
            {
                var stream = new DataStream
                {
                    Id = item.StreamId,
                    Name = item.Name,
                    TypeId = _type.Id
                };

                messageProcessor.WriteStream(stream);

                if (logger != null)
                {
                    LogSendingStream(logger, stream.Name, stream.Id, stream.TypeId);
                }
            }

            if (logger?.IsEnabled(LogLevel.Information) == true)
            {
                LogSendingMeasurement(logger, item.StreamId, FormatMeasurementForLog(measurement));
            }

            messageProcessor.WriteDynamicValue(item, measurement);
        }
    }

    private static string FormatMeasurementForLog(WeatherObservationMeasurement measurement)
    {
        var payload = new Dictionary<string, object>
        {
            ["timestamp"] = measurement.Timestamp,
        };

        if (measurement.temperature_c.HasValue)
            payload["temperature_c"] = measurement.temperature_c.Value;

        if (measurement.dewpoint_c.HasValue)
            payload["dewpoint_c"] = measurement.dewpoint_c.Value;

        if (measurement.relative_humidity_pct.HasValue)
            payload["relative_humidity_pct"] = measurement.relative_humidity_pct.Value;

        if (measurement.wind_speed_mps.HasValue)
            payload["wind_speed_mps"] = measurement.wind_speed_mps.Value;

        if (measurement.wind_direction_deg.HasValue)
            payload["wind_direction_deg"] = measurement.wind_direction_deg.Value;

        if (measurement.barometric_pressure_pa.HasValue)
            payload["barometric_pressure_pa"] = measurement.barometric_pressure_pa.Value;

        if (measurement.visibility_m.HasValue)
            payload["visibility_m"] = measurement.visibility_m.Value;

        if (!string.IsNullOrWhiteSpace(measurement.text_description))
            payload["text_description"] = measurement.text_description;

        return JsonSerializer.Serialize(payload);
    }

    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Information,
        Message = SendingTypeFormat)]
    private static partial void LogSendingType(ILogger logger, string typeName, string typeId);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = SendingStreamFormat)]
    private static partial void LogSendingStream(ILogger logger, string streamName, string streamId, string streamTypeId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = SendingMeasurementFormat)]
    private static partial void LogSendingMeasurement(ILogger logger, string streamId, string measurementPayload);
}