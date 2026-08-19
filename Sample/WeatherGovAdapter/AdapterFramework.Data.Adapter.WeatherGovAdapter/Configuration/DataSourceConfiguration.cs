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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.AdapterCommon.Configuration;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;

/// <summary>
/// Represents the Weather.gov adapter data source configuration.
/// </summary>
[DataContract]
[ConfigurationFacet(AdapterConstants.ComponentType, CommonConstants.DataSourceConfigurationName, "1.0.0")]
public class DataSourceConfiguration : DataSourceConfigurationBase, IEquatable<DataSourceConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataSourceConfiguration"/> class.
    /// </summary>
    public DataSourceConfiguration()
    {
        // Seed the framework's operator-overridable pattern from the adapter default so operators can
        // customize it via config without recompiling. StreamIdPrefix is intentionally left unset so
        // the framework's ComponentId fallback applies (aligned with sibling adapters).
        DefaultStreamIdPattern = AdapterConstants.DefaultStreamIdPattern;
    }

    /// <summary>
    /// Gets or sets the Weather.gov API base URL.
    /// </summary>
    [DataMember(Name = "baseUrl")]
    public string BaseUrl { get; set; } = AdapterConstants.WeatherGovBaseUrl;

    /// <summary>
    /// Gets or sets the HTTP request timeout in milliseconds.
    /// </summary>
    [DataMember(Name = "requestTimeoutMs")]
    public int RequestTimeoutMs { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts for transient request failures.
    /// </summary>
    [DataMember(Name = "maxRetries")]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base retry backoff delay in milliseconds.
    /// </summary>
    [DataMember(Name = "retryBackoffMs")]
    public int RetryBackoffMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of station observation requests issued concurrently within a
    /// single sampling pass. Applied once when the adapter starts; changing it takes effect on restart.
    /// </summary>
    [DataMember(Name = "maxConcurrentRequests")]
    public int MaxConcurrentRequests { get; set; } = 5;

    /// <summary>
    /// Gets or sets the User-Agent header sent to the Weather.gov API.
    /// </summary>
    [DataMember(Name = "userAgent")]
    public string UserAgent { get; set; } = "WeatherGovAdapterSample/1.0 (support@example.com)";

    /// <summary>
    /// When true, allows an HTTP (non-TLS) base URL. Intended for local development and testing only.
    /// Must not be set to true in production environments.
    /// </summary>
    [DataMember(Name = "allowInsecureBaseUrl")]
    public bool AllowInsecureBaseUrl { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether measurements with no non-timestamp values are discarded.
    /// </summary>
    [DataMember(Name = "dropNullMeasurements")]
    public bool DropNullMeasurements { get; set; } = true;

    /// <summary>
    /// Returns command-line help text for the data source configuration.
    /// The common command-line header is added by the caller.
    /// </summary>
    public static string GetHelpText()
    {
        return
            $"{nameof(StreamIdPrefix),-30} [Optional] Stream ID prefix{Environment.NewLine}" +
            $"{nameof(DefaultStreamIdPattern),-30} [Optional] Default stream ID pattern{Environment.NewLine}" +
            $"{nameof(BaseUrl),-30} [Optional] Weather.gov API base URL{Environment.NewLine}" +
            $"{nameof(RequestTimeoutMs),-30} [Optional] HTTP request timeout in milliseconds{Environment.NewLine}" +
            $"{nameof(MaxRetries),-30} [Optional] Maximum retry attempts for transient failures{Environment.NewLine}" +
            $"{nameof(RetryBackoffMs),-30} [Optional] Base retry backoff delay in milliseconds{Environment.NewLine}" +
            $"{nameof(MaxConcurrentRequests),-30} [Optional] Max concurrent station requests per sampling pass{Environment.NewLine}" +
            $"{nameof(UserAgent),-30} [Optional] User-Agent header sent to the API{Environment.NewLine}" +
            $"{nameof(AllowInsecureBaseUrl),-30} [Optional] Allow non-TLS HTTP base URL (development only){Environment.NewLine}" +
            $"{nameof(DropNullMeasurements),-30} [Optional] Discard measurements with no non-timestamp values";
    }

    /// <summary>
    /// Compares two DataSourceConfiguration instances to see if they match.
    /// </summary>
    /// <param name="other">The instance being compared.</param>
    /// <returns>True if 'other' equals/matches this instance.</returns>
    public bool Equals(DataSourceConfiguration other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(StreamIdPrefix, other.StreamIdPrefix, StringComparison.OrdinalIgnoreCase) &&
               DefaultStreamIdPattern == other.DefaultStreamIdPattern &&
               string.Equals(BaseUrl, other.BaseUrl, StringComparison.OrdinalIgnoreCase) &&
               RequestTimeoutMs == other.RequestTimeoutMs &&
               MaxRetries == other.MaxRetries &&
               RetryBackoffMs == other.RetryBackoffMs &&
               MaxConcurrentRequests == other.MaxConcurrentRequests &&
               string.Equals(UserAgent, other.UserAgent, StringComparison.Ordinal) &&
               AllowInsecureBaseUrl == other.AllowInsecureBaseUrl &&
               DropNullMeasurements == other.DropNullMeasurements;
    }

    /// <summary>
    /// Compares two DataSourceConfiguration instances as object to see if they match.
    /// </summary>
    /// <param name="obj">The object instance being compared.</param>
    /// <returns>True if 'other' equals/matches this instance.</returns>
    public override bool Equals(object obj)
    {
        return Equals(obj as DataSourceConfiguration);
    }

    /// <summary>
    /// Gets the hash code of the data source configuration instance.
    /// </summary>
    /// <returns>The hash code of the data source configuration instance.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StreamIdPrefix, StringComparer.OrdinalIgnoreCase);
        hash.Add(DefaultStreamIdPattern, StringComparer.Ordinal);
        hash.Add(BaseUrl, StringComparer.OrdinalIgnoreCase);
        hash.Add(RequestTimeoutMs);
        hash.Add(MaxRetries);
        hash.Add(RetryBackoffMs);
        hash.Add(MaxConcurrentRequests);
        hash.Add(UserAgent, StringComparer.Ordinal);
        hash.Add(AllowInsecureBaseUrl);
        hash.Add(DropNullMeasurements);

        return hash.ToHashCode();
    }

    /// <summary>
    /// This is called automatically by the platform to allow the adapter to validate the data source configuration.
    ///
    /// Note: you can use 'AdapterHelper.DataSourceConfiguration' to retrieve the entire data source configuration.
    /// </summary>
    /// <returns>A list of validation errors. An empty list implies there are no data source configuration errors.</returns>
    protected override IEnumerable<ValidationResult> ValidateConfiguration()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(AllowInsecureBaseUrl && uri.Scheme == Uri.UriSchemeHttp)))
        {
            yield return new ValidationResult(
                AllowInsecureBaseUrl
                    ? $"{nameof(BaseUrl)} must be a valid absolute URI (http or https)."
                    : $"{nameof(BaseUrl)} must be a valid HTTPS absolute URI. Set {nameof(AllowInsecureBaseUrl)}=true only for local development.");
        }

        if (RequestTimeoutMs <= 0)
        {
            yield return new ValidationResult($"{nameof(RequestTimeoutMs)} must be greater than 0.");
        }

        if (MaxRetries < 0)
        {
            yield return new ValidationResult($"{nameof(MaxRetries)} cannot be negative.");
        }

        if (RetryBackoffMs < 0)
        {
            yield return new ValidationResult($"{nameof(RetryBackoffMs)} cannot be negative.");
        }

        if (MaxConcurrentRequests <= 0)
        {
            yield return new ValidationResult($"{nameof(MaxConcurrentRequests)} must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            yield return new ValidationResult($"{nameof(UserAgent)} is required.");
        }

        if (!StreamIdPatternValidator.IsValidDefaultStreamIdPattern(
                DefaultStreamIdPattern,
                AdapterConstants.DefaultStreamIdPattern,
                AdapterConstants.DefaultStreamIdKeywords,
                out var patternError))
        {
            yield return new ValidationResult($"{nameof(DefaultStreamIdPattern)}: {patternError}");
        }
    }
}

/// <summary>
/// Represents a latitude and longitude seed point used for station discovery.
/// </summary>
/// <param name="Latitude">The latitude component of the seed point.</param>
/// <param name="Longitude">The longitude component of the seed point.</param>
[DataContract]
public readonly record struct DiscoverySeedPoint(
    [property: DataMember(Name = "latitude")] double Latitude,
    [property: DataMember(Name = "longitude")] double Longitude)
{
    /// <summary>
    /// The minimum valid latitude value.
    /// </summary>
    public const double MinLatitude = -90;

    /// <summary>
    /// The maximum valid latitude value.
    /// </summary>
    public const double MaxLatitude = 90;

    /// <summary>
    /// The minimum valid longitude value.
    /// </summary>
    public const double MinLongitude = -180;

    /// <summary>
    /// The maximum valid longitude value.
    /// </summary>
    public const double MaxLongitude = 180;

    // NaN and the infinities fail every comparison, so they are rejected here too.
    /// <summary>
    /// Gets a value indicating whether <see cref="Latitude"/> is within the supported range.
    /// </summary>
    public bool IsLatitudeInRange => Latitude is >= MinLatitude and <= MaxLatitude;

    /// <summary>
    /// Gets a value indicating whether <see cref="Longitude"/> is within the supported range.
    /// </summary>
    public bool IsLongitudeInRange => Longitude is >= MinLongitude and <= MaxLongitude;

    /// <summary>
    /// True when both coordinates are in range. Single definition of "in range" shared by config
    /// validation and WeatherGovDiscovery.ParseQuerySeedPoints, so the two paths cannot drift.
    /// </summary>
    public bool HasValidCoordinates() => IsLatitudeInRange && IsLongitudeInRange;
}