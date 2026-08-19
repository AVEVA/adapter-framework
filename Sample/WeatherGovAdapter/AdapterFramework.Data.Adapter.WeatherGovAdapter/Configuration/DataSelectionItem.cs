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
using System.Linq;
using System.Runtime.Serialization;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.AdapterCommon;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;

/// <summary>
/// Represents a Weather.gov station selection configured for polling.
/// </summary>
[DataContract]
[ConfigurationFacet(AdapterConstants.ComponentType, CommonConstants.DataSelectionConfigurationName, "1.0.0")]
public class DataSelectionItem : DataSelectionConfigurationBase, IScanDataSelectionConfiguration, IEquatable<DataSelectionItem>
{
    /// <summary>
    /// Gets or sets the selection item identifier.
    /// </summary>
    [DataMember(Name = "id")]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the Weather.gov station identifier.
    /// </summary>
    [DataMember(Name = "stationId")]
    public string StationId { get; set; }

    private string[] _includeFields = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the measurement fields to include for the station.
    /// </summary>
    [DataMember(Name = "includeFields")]
    public string[] IncludeFields
    {
        get => _includeFields;
        // Coerce null (from deserialization or explicit assignment) so readers never NRE.
        set => _includeFields = value ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets or sets the schedule ID that selects the station's collection cadence.
    /// </summary>
    [DataMember(Name = "scheduleId")]
    [Required]
    public string ScheduleId { get; set; }

    /// <summary>
    /// Provides help text for data selection configuration.
    /// </summary>
    /// <param name="header">The command-line help header.</param>
    /// <returns>The formatted help text.</returns>
    public static string GetHelpInfo(string header)
    {
        return $@"{header}

{nameof(Selected)}          [Optional] Whether enabled
{nameof(Name)}              [Optional] Friendly name
{nameof(StreamId)}          [Optional] Stream ID (auto-generated if empty)
{nameof(Id)}                [Optional] Selection ID
{nameof(StationId)}         [Required] Station ID
{nameof(IncludeFields)}     [Optional] Included measurement fields
{nameof(ScheduleId)}        [Required] Schedule ID. One of: {AdapterConstants.DefaultScheduleId} (1 min), {AdapterConstants.ScheduleIdFiveMinutes} (5 min), {AdapterConstants.ScheduleIdTenMinutes} (10 min)

Note: Configure via JSON or manual entry.
";
    }

    /// <summary>
    /// The equality comparison implementation to compare two data selection items. 
    /// This method should implement property-wise comparison for the data selection item to perform the equality check.
    /// </summary>
    /// <param name="other">Data selection item provided to compare. </param>
    /// <returns>Whether the provided data selection item equals to the current item. </returns>
    public bool Equals(DataSelectionItem other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Selected == other.Selected &&
               Name == other.Name &&
               Id == other.Id &&
               StreamId == other.StreamId &&
               DataFilterId == other.DataFilterId &&
               string.Equals(StationId, other.StationId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ScheduleId, other.ScheduleId, StringComparison.OrdinalIgnoreCase) &&
               IncludeFields.SequenceEqual(other.IncludeFields, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Method override for equality.
    /// </summary>
    /// <param name="obj">The provided object. </param>
    /// <returns>Whether the provided object equals to the current item. </returns>
    public override bool Equals(object obj) => Equals(obj as DataSelectionItem);

    /// <summary>
    /// Method override for getting hashcode of the data selection item.
    /// This method should generate hashcode by combining the hashcode of all the properties defined in the data selection item.  
    /// </summary>
    /// <returns>The hashcode generated from all properties defined in the data selection item. </returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Selected);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(StreamId, StringComparer.Ordinal);
        hash.Add(DataFilterId, StringComparer.Ordinal);
        hash.Add(StationId, StringComparer.OrdinalIgnoreCase);
        hash.Add(ScheduleId, StringComparer.OrdinalIgnoreCase);

        foreach (var field in IncludeFields)
        {
            hash.Add(field, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// This method is automatically called by the platform to allow the adapter
    /// a chance to validate data selections.
    /// </summary>
    /// <returns>A list of validation errors. An empty list implies the data selections are all valid.</returns>
    protected override IEnumerable<ValidationResult> ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(StationId))
        {
            yield return new ValidationResult($"{nameof(StationId)} is required.");
        }

        if (IncludeFields.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult($"{nameof(IncludeFields)} cannot contain null or empty values.");
        }

        var invalidFields = IncludeFields
            .Where(field => !string.IsNullOrWhiteSpace(field) &&
                            !AdapterConstants.SupportedMeasurementFields.Contains(field))
            .ToArray();

        if (invalidFields.Length > 0)
        {
            yield return new ValidationResult(
                $"{nameof(IncludeFields)} contains unsupported values: {string.Join(", ", invalidFields)}");
        }

        if (!string.IsNullOrWhiteSpace(ScheduleId) &&
            !AdapterConstants.SelectableScheduleIds.Contains(ScheduleId))
        {
            yield return new ValidationResult(
                $"{nameof(ScheduleId)} must be one of: {string.Join(", ", AdapterConstants.SelectableScheduleIds)}.");
        }
    }
}