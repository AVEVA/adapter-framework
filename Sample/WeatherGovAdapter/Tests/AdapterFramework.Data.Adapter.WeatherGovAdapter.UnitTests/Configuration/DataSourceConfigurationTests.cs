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
using System.ComponentModel.DataAnnotations;
using System.Linq;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.Configuration;

/// <summary>
/// The class for writing unit tests for the DataSourceConfiguration class in the adapter main project 
/// </summary>
public class DataSourceConfigurationTests
{
    /// <summary>
    /// Verifies that validation reports every invalid field at once instead of stopping at the first.
    /// </summary>
    [Fact]
    public void ValidateConfiguration_ReportsAllFieldErrorsAtOnce()
    {
        var configuration = new DataSourceConfiguration
        {
            BaseUrl = "not-a-url",
            RequestTimeoutMs = 0,
            MaxRetries = -1,
            RetryBackoffMs = -1,
            UserAgent = string.Empty,
            DefaultStreamIdPattern = "invalid.{UnknownKeyword}",
        };

        var errors = configuration.Validate(new ValidationContext(configuration)).ToArray();

        // The per-field SingleInvalidField theory pins each rule in isolation; this case pins that the
        // validator reports every invalid field together rather than short-circuiting on the first.
        Assert.Equal(6, errors.Length);
        Assert.Contains(errors, x => x.ErrorMessage.Contains(nameof(DataSourceConfiguration.BaseUrl)));
        Assert.Contains(errors, x => x.ErrorMessage.Contains(nameof(DataSourceConfiguration.RequestTimeoutMs)));
        Assert.Contains(errors, x => x.ErrorMessage.Contains(nameof(DataSourceConfiguration.MaxRetries)));
        Assert.Contains(errors, x => x.ErrorMessage.Contains(nameof(DataSourceConfiguration.RetryBackoffMs)));
        Assert.Contains(errors, x => x.ErrorMessage.Contains(nameof(DataSourceConfiguration.UserAgent)));
        Assert.Contains(errors, x => x.ErrorMessage.Contains(nameof(DataSourceConfiguration.DefaultStreamIdPattern)));
    }

    /// <summary>
    /// Verifies that a valid configuration produces no validation errors.
    /// </summary>
    [Fact]
    public void ValidateConfiguration_WithValidValues_ReturnsNoErrors()
    {
        var configuration = WeatherGovTestData.CreateValidDataSourceConfiguration();

        var errors = configuration.Validate(new ValidationContext(configuration)).ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that a single invalid field produces exactly one validation error naming that field.
    /// </summary>
    [Theory]
    [MemberData(nameof(SingleInvalidFieldMutations))]
    public void ValidateConfiguration_SingleInvalidField_ReturnsOneError(string field)
    {
        var configuration = WeatherGovTestData.CreateValidDataSourceConfiguration();

        switch (field)
        {
            case nameof(DataSourceConfiguration.BaseUrl):
                configuration.BaseUrl = "not-a-url";
                break;
            case nameof(DataSourceConfiguration.RequestTimeoutMs):
                configuration.RequestTimeoutMs = 0;
                break;
            case nameof(DataSourceConfiguration.MaxRetries):
                configuration.MaxRetries = -1;
                break;
            case nameof(DataSourceConfiguration.RetryBackoffMs):
                configuration.RetryBackoffMs = -1;
                break;
            case nameof(DataSourceConfiguration.MaxConcurrentRequests):
                configuration.MaxConcurrentRequests = 0;
                break;
            case nameof(DataSourceConfiguration.UserAgent):
                configuration.UserAgent = "   ";
                break;
            case nameof(DataSourceConfiguration.DefaultStreamIdPattern):
                // An unknown keyword makes the pattern invalid against DefaultStreamIdKeywords (which only
                // contains StationId). The validator reports it as a DefaultStreamIdPattern error.
                configuration.DefaultStreamIdPattern = "invalid.{UnknownKeyword}";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Unhandled validation mutation key.");
        }

        var errors = configuration.Validate(new ValidationContext(configuration)).ToArray();

        var error = Assert.Single(errors);
        Assert.Contains(field, error.ErrorMessage);
    }

    /// <summary>
    /// Verifies that equality applies the expected per-field case sensitivity.
    /// </summary>
    [Theory]
    [MemberData(nameof(EqualsCaseSensitivityCases))]
    public void Equals_AppliesPerFieldCaseSensitivity(string field, bool expectEqual)
    {
        var left = WeatherGovTestData.CreateValidDataSourceConfiguration();
        var right = WeatherGovTestData.CreateValidDataSourceConfiguration();

        switch (field)
        {
            case nameof(DataSourceConfiguration.StreamIdPrefix):  // OrdinalIgnoreCase
                left.StreamIdPrefix = "weathergov";
                right.StreamIdPrefix = "WEATHERGOV";
                break;
            case nameof(DataSourceConfiguration.BaseUrl):         // OrdinalIgnoreCase (host)
                left.BaseUrl = "https://api.weather.gov";
                right.BaseUrl = "https://API.WEATHER.GOV";
                break;
            case nameof(DataSourceConfiguration.UserAgent):       // Ordinal (case-sensitive)
                left.UserAgent = "WeatherGovAdapterSample/1.0 (test@example.com)";
                right.UserAgent = "weathergovadaptersample/1.0 (test@example.com)";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Unhandled case-sensitivity field.");
        }

        Assert.Equal(expectEqual, left.Equals(right));

        // Equal objects must hash equal (contract). Unequal objects may legally share a hash, so only
        // the equal rows assert hash equality.
        if (expectEqual)
        {
            Assert.Equal(left.GetHashCode(), right.GetHashCode());
        }
    }

    /// <summary>
    /// Verifies that changing any single field makes two otherwise-equal configurations unequal.
    /// </summary>
    [Theory]
    [MemberData(nameof(EqualsSingleFieldMutations))]
    public void Equals_SingleFieldDiffers_ReturnsFalse(string field)
    {
        var left = WeatherGovTestData.CreateValidDataSourceConfiguration();
        var right = WeatherGovTestData.CreateValidDataSourceConfiguration();

        switch (field)
        {
            case nameof(DataSourceConfiguration.StreamIdPrefix):
                right.StreamIdPrefix = "differentprefix";
                break;
            case nameof(DataSourceConfiguration.DefaultStreamIdPattern):
                right.DefaultStreamIdPattern = "{StreamIdPrefix}.{StationId}.alt";
                break;
            case nameof(DataSourceConfiguration.BaseUrl):
                right.BaseUrl = "https://example.weather.test";
                break;
            case nameof(DataSourceConfiguration.RequestTimeoutMs):
                right.RequestTimeoutMs = 1;
                break;
            case nameof(DataSourceConfiguration.MaxRetries):
                right.MaxRetries = 0;
                break;
            case nameof(DataSourceConfiguration.RetryBackoffMs):
                right.RetryBackoffMs = 0;
                break;
            case nameof(DataSourceConfiguration.MaxConcurrentRequests):
                right.MaxConcurrentRequests = 1;
                break;
            case nameof(DataSourceConfiguration.UserAgent):
                right.UserAgent = "DifferentAgent/2.0";
                break;
            case nameof(DataSourceConfiguration.AllowInsecureBaseUrl):
                right.AllowInsecureBaseUrl = !right.AllowInsecureBaseUrl;
                break;
            case nameof(DataSourceConfiguration.DropNullMeasurements):
                right.DropNullMeasurements = !right.DropNullMeasurements;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Unhandled mutation key.");
        }

        Assert.False(left.Equals(right));
    }

    /// <summary>
    /// Verifies that a null object or an object of a different type is not equal.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-data-source-configuration")]   // Equals over a different runtime type
    public void Equals_NullOrDifferentType_ReturnsFalse(object other)
    {
        var configuration = WeatherGovTestData.CreateValidDataSourceConfiguration();

        Assert.False(configuration.Equals(other));
    }

    /// <summary>
    /// Verifies that only https base urls are accepted unless the insecure flag opts in to http.
    /// </summary>
    [Theory]
    // Scheme and insecure-flag matrix: https always valid, http only when the flag is on.
    [InlineData("https://api.weather.gov", false, false)]  // https is always allowed
    [InlineData("https://api.weather.gov", true, false)]   // the insecure flag only relaxes; https stays valid when on
    [InlineData("http://api.weather.gov", true, false)]    // http allowed only when explicitly opted in
    [InlineData("http://api.weather.gov", false, true)]    // http with the insecure flag off is rejected
    // Non-http(s) schemes are never allowed, even with the insecure flag on.
    [InlineData("ftp://api.weather.gov", false, true)]     // non-http(s) scheme rejected
    [InlineData("ftp://api.weather.gov", true, true)]      // the insecure flag opens http only, never ftp
    // Malformed or blank base urls fail Uri.TryCreate, so they are rejected.
    [InlineData("api.weather.gov", false, true)]           // missing scheme, not an absolute uri
    [InlineData("   ", false, true)]                       // whitespace-only
    [InlineData("", false, true)]                          // empty
    [InlineData("https://api.weather.gov/", false, false)] // a trailing slash is still valid
    public void ValidateConfiguration_EnforcesHttpsUnlessInsecureBaseUrlAllowed(
        string baseUrl,
        bool allowInsecureBaseUrl,
        bool expectBaseUrlError)
    {
        var configuration = WeatherGovTestData.CreateValidDataSourceConfiguration();
        configuration.BaseUrl = baseUrl;
        configuration.AllowInsecureBaseUrl = allowInsecureBaseUrl;

        var errors = configuration.Validate(new ValidationContext(configuration)).ToArray();

        Assert.Equal(
            expectBaseUrlError,
            errors.Any(x => x.ErrorMessage.Contains(nameof(DataSourceConfiguration.BaseUrl))));
    }

    /// <summary>
    /// Verifies that the data-source help text names the stream-id configuration options.
    /// </summary>
    [Fact]
    public void GetHelpText_NamesStreamIdOptions()
    {
        var help = DataSourceConfiguration.GetHelpText();

        Assert.Contains(nameof(DataSourceConfiguration.StreamIdPrefix), help);
        Assert.Contains(nameof(DataSourceConfiguration.DefaultStreamIdPattern), help);
    }

    /// <summary>
    /// Provides the field names exercised by ValidateConfiguration_SingleInvalidField_ReturnsOneError.
    /// </summary>
    public static TheoryData<string> SingleInvalidFieldMutations() =>
    [
        nameof(DataSourceConfiguration.BaseUrl),
        nameof(DataSourceConfiguration.RequestTimeoutMs),
        nameof(DataSourceConfiguration.MaxRetries),
        nameof(DataSourceConfiguration.RetryBackoffMs),
        nameof(DataSourceConfiguration.MaxConcurrentRequests),
        nameof(DataSourceConfiguration.UserAgent),
        nameof(DataSourceConfiguration.DefaultStreamIdPattern),
    ];

    /// <summary>
    /// Provides the field names exercised by Equals_SingleFieldDiffers_ReturnsFalse.
    /// </summary>
    public static TheoryData<string> EqualsSingleFieldMutations() =>
    [
        nameof(DataSourceConfiguration.StreamIdPrefix),
        nameof(DataSourceConfiguration.DefaultStreamIdPattern),
        nameof(DataSourceConfiguration.BaseUrl),
        nameof(DataSourceConfiguration.RequestTimeoutMs),
        nameof(DataSourceConfiguration.MaxRetries),
        nameof(DataSourceConfiguration.RetryBackoffMs),
        nameof(DataSourceConfiguration.MaxConcurrentRequests),
        nameof(DataSourceConfiguration.UserAgent),
        nameof(DataSourceConfiguration.AllowInsecureBaseUrl),
        nameof(DataSourceConfiguration.DropNullMeasurements),
    ];

    /// <summary>
    /// Provides the field and expected-equality pairs exercised by Equals_AppliesPerFieldCaseSensitivity.
    /// </summary>
    public static TheoryData<string, bool> EqualsCaseSensitivityCases() => new()
    {
        { nameof(DataSourceConfiguration.StreamIdPrefix), true },   // OrdinalIgnoreCase
        { nameof(DataSourceConfiguration.BaseUrl), true },          // OrdinalIgnoreCase
        { nameof(DataSourceConfiguration.UserAgent), false },       // Ordinal (case-sensitive)
    };
}
