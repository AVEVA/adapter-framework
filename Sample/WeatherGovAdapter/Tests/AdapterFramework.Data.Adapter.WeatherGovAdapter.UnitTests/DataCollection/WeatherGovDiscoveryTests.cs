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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Discovery;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.DataCollection;

/// <summary>
/// Unit tests for <see cref="WeatherGovDiscovery"/>.
/// </summary>
public class WeatherGovDiscoveryTests
{
    // Sentinel returned by the delegate under test to prove BuildSelectionItem stores whatever the
    // framework's stream-id generator produces without doing any composition of its own.
    private const string TestStreamId = "__test_stream_id__";
    private static string StubStreamId(DataSelectionItem item) => TestStreamId;

    /// <summary>
    /// Verifies that BuildSelectionItem canonicalizes the station id and derives config-independent keys.
    /// </summary>
    [Fact]
    public void BuildSelectionItem_CanonicalizesStationIdAndKeys()
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // "  k sEa  " exercises trim, kept inner space, upper-invariant casing.
        var selection = discovery.BuildSelectionItem("  k sEa  ");

        Assert.Equal("K SEA", selection.StationId);
        Assert.Equal("weathergov.station.K SEA", selection.Id);
        Assert.True(selection.Selected);
        Assert.Equal(AdapterConstants.SupportedMeasurementFields.Count, selection.IncludeFields.Length);
        Assert.True(
            AdapterConstants.SupportedMeasurementFields
                .All(field => selection.IncludeFields.Contains(field, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Verifies that a discovered selection is assigned the default collection schedule so the framework
    /// can persist it (ScheduleId is [Required]) and route it to a schedule for sampling.
    /// </summary>
    [Fact]
    public void BuildSelectionItem_AssignsDefaultScheduleId()
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        var selection = discovery.BuildSelectionItem("KSEA");

        Assert.Equal(AdapterConstants.DefaultScheduleId, selection.ScheduleId);
    }

    /// <summary>
    /// Verifies that ParseQuerySeedPoints skips invalid coordinate pairs and keeps the valid ones.
    /// </summary>
    [Fact]
    public void ParseQuerySeedPoints_SkipsInvalidPairs()
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        var points = discovery.ParseQuerySeedPoints("47.61,-122.33;abc,123;44.1;90.0,180.0");

        Assert.Equal(2, points.Count);
        Assert.Equal(47.61, points[0].Latitude, 3);
        Assert.Equal(-122.33, points[0].Longitude, 3);
        Assert.Equal(90.0, points[1].Latitude, 3);
        Assert.Equal(180.0, points[1].Longitude, 3);
    }

    /// <summary>
    /// Verifies that blank discovery input yields no seed points.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseQuerySeedPoints_BlankInput_ReturnsEmpty(string discoveryQuery)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        Assert.Empty(discovery.ParseQuerySeedPoints(discoveryQuery));
    }

    /// <summary>
    /// Verifies that a malformed coordinate is skipped.
    /// </summary>
    [Theory]
    [InlineData("47.61!,-122.33")]
    [InlineData("47 .61,-122.33")]    // embedded whitespace inside a coordinate
    [InlineData("\u0664\u0667,-122.33")] // unicode (Arabic-Indic) digits
    [InlineData("47.61.,-122.33")]    // trailing punctuation on a coordinate
    public void ParseQuerySeedPoints_SkipsMalformedCoordinate(string query)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // double.TryParse(Float, InvariantCulture) rejects each input, so the only pair is skipped.
        Assert.Empty(discovery.ParseQuerySeedPoints(query));
    }

    /// <summary>
    /// Verifies that ParseQuerySeedPoints enforces the latitude and longitude ranges at their boundaries.
    /// </summary>
    [Theory]
    // Latitude boundary [-90, 90]: exact boundary accepted, smallest representable overage rejected.
    [InlineData("90,-122.33", true)]
    [InlineData("-90,-122.33", true)]
    [InlineData("90.00000000001,-122.33", false)]
    [InlineData("-90.00000000001,-122.33", false)]
    [InlineData("0,0", true)]
    // Longitude boundary [-180, 180]: exact boundary accepted, smallest representable overage rejected.
    [InlineData("47.61,180", true)]
    [InlineData("47.61,-180", true)]
    [InlineData("47.61,180.00000000001", false)]
    [InlineData("47.61,-180.00000000001", false)]
    // Interior points across the four sign-distinct hemispheres.
    [InlineData("47.61,-122.33", true)]
    [InlineData("-33.87,151.21", true)]
    [InlineData("35.68,139.69", true)]
    [InlineData("-22.91,-43.17", true)]
    // Extreme/precision doubles parse successfully, then the parsed value is range-checked.
    [InlineData("1e-308,0", true)]                     // subnormal latitude rounds into range, so accepted
    [InlineData("1.7976931348623157e+308,0", false)]  // ~double.MaxValue latitude, so rejected
    // Clearly out of range coordinates.
    [InlineData("91,45", false)]
    [InlineData("-95.4,12", false)]
    [InlineData("45,181", false)]
    [InlineData("12,-200.5", false)]
    [InlineData("200,300", false)]
    public void ParseQuerySeedPoints_EnforcesCoordinateRange(string query, bool expectAccepted)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        var points = discovery.ParseQuerySeedPoints(query);

        // A single in-range pair yields one seed; an out-of-range pair yields none.
        Assert.Equal(expectAccepted, points.Count == 1);
    }

    #region Edge Cases - Numeric Precision and Rounding

    /// <summary>
    /// Verifies that coordinates are parsed exactly, including high-precision and scientific-notation literals.
    /// </summary>
    [Theory]
    // High-precision literals: the range check runs on the PARSED double, so a literal that textually
    // exceeds 90 but rounds down to exactly 90 is still accepted.
    [InlineData("47.6111111111111111111111,0", 47.61111111111111, 0.0)]  // excess decimals truncated to nearest double
    [InlineData("90.0000000000000000000001,0", 90.0, 0.0)]              // excess precision below ULP rounds to exactly 90
    [InlineData("1.0000000000000002,0", 1.0000000000000002, 0.0)]        // smallest double greater than 1
    // Scientific notation parses through double.TryParse the same way.
    [InlineData("1.23456789012345e1,-122.33", 12.3456789012345, -122.33)]  // exponent form for latitude
    [InlineData("4.761e1,-1.2233e2", 47.61, -122.33)]                       // both coordinates in exponent form
    [InlineData("-1.33e1,1.8e2", -13.3, 180.0)]                             // negative latitude; exponent form lands on +180
    public void ParseQuerySeedPoints_ParsesCoordinatesExactly(string query, double expectedLatitude, double expectedLongitude)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        var points = discovery.ParseQuerySeedPoints(query);

        Assert.Single(points);
        Assert.Equal(expectedLatitude, points[0].Latitude, 10);
        Assert.Equal(expectedLongitude, points[0].Longitude, 10);
    }
    /// <summary>
    /// Verifies that NaN and infinity coordinates are rejected by the range check.
    /// </summary>
    [Theory]
    // NaN/Infinity PARSE successfully (Float, InvariantCulture), so they're rejected at the range check, not
    // the parse step -- the same predicate the config-validation path uses.
    [InlineData("NaN,0")]        // NaN latitude: every comparison is false, so the range check fails
    [InlineData("0,NaN")]        // NaN longitude: lat passes, lon fails the range check
    [InlineData("Infinity,0")]   // +Inf latitude > 90
    [InlineData("-Infinity,0")]  // -Inf latitude < -90
    [InlineData("0,Infinity")]   // +Inf longitude > 180
    [InlineData("0,-Infinity")]  // -Inf longitude < -180
    public void ParseQuerySeedPoints_RejectsNaNAndInfinity(string query)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        Assert.Empty(discovery.ParseQuerySeedPoints(query));
    }
    #endregion

    #region Edge Cases - String Extremes

    /// <summary>
    /// Verifies that BuildSelectionItem rejects a null, empty, or whitespace station id at the boundary.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n\r")]
    public void BuildSelectionItem_NullOrWhitespaceStationId_Throws(string stationId)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null and
        // ArgumentException for empty/whitespace, so ThrowsAny<ArgumentException> covers both.
        Assert.ThrowsAny<ArgumentException>(
            () => discovery.BuildSelectionItem(stationId));
    }

    /// <summary>
    /// Pins the canonicalized station id for inputs the earlier blank/whitespace and Turkish-culture
    /// theories do not cover: a control character embedded mid-id (which is not whitespace, so Trim
    /// keeps it and ToUpperInvariant leaves it unchanged) and a non-ASCII lowercase id (which
    /// ToUpperInvariant maps via invariant casing).
    /// </summary>
    [Theory]
    [InlineData("K\u0000SEA", "K\u0000SEA")]   // NUL is not whitespace, survives Trim; no upper mapping
    [InlineData("k\u0153st\u00E4dt", "K\u0152ST\u00C4DT")]   // "kœstädt" -> "KŒSTÄDT" via invariant casing
    public void BuildSelectionItem_NonStandardStationId_Canonicalizes(string stationId, string expectedCanonical)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        var selection = discovery.BuildSelectionItem(stationId);

        Assert.Equal(expectedCanonical, selection.StationId);
        Assert.Equal($"{AdapterConstants.DiscoveryKeyPrefix}{expectedCanonical}", selection.Id);
    }

    /// <summary>
    /// Verifies that the station id is uppercased using invariant culture even under Turkish culture.
    /// </summary>
    [Fact]
    public void BuildSelectionItem_UppercasesStationIdUnderTurkishCulture()
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // ToUpperInvariant() vs ToUpper(): under tr-TR "i".ToUpper() is dotted "\u0130". Invariant ignores
        // culture, so "ksi" becomes "KSI"; a ToUpper() regression would surface here as "KS\u0130".
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var selection = discovery.BuildSelectionItem("ksi");

            Assert.Equal("KSI", selection.StationId);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    #endregion

    #region Edge Cases - String + Number Combinations

    /// <summary>
    /// Verifies that input containing only separators yields no seed points.
    /// </summary>
    [Theory]
    [InlineData(";")]
    [InlineData(";;;")]    // repeated separators collapse to zero pairs the same way
    public void ParseQuerySeedPoints_SeparatorsOnly_ReturnsEmpty(string query)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // Distinct from the only-commas case: here the loop body never runs because there are no pairs.
        Assert.Empty(discovery.ParseQuerySeedPoints(query));
    }

    /// <summary>
    /// Verifies that malformed pair shapes yield no seed points.
    /// </summary>
    [Theory]
    [InlineData(",")]
    [InlineData(",,")]                    // ["", "", ""] is length 3, rejected by the values.Length != 2 guard
    [InlineData("47.61,,-122.33")]        // ["47.61", "", "-122.33"]: empty middle survives (TrimEntries, no RemoveEmptyEntries), length 3, rejected
    [InlineData("47.61,-122.33,99")]      // three values, length 3, rejected
    [InlineData("47.61,-122.33,99,100")]  // four values, length 4, rejected
    public void ParseQuerySeedPoints_MalformedPairShape_ReturnsEmpty(string query)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // Same code path as separators-only, but here each pair runs through the loop and gets discarded either
        // by values.Length != 2 or (for ",") both empty values fail to parse. "47.61,,-122.33" is the regression 
        // guard: without RemoveEmptyEntries, the empty middle field keeps the split at length 3 instead of 
        // silently collapsing to a valid-looking (47.61, -122.33).
        Assert.Empty(discovery.ParseQuerySeedPoints(query));
    }

    #endregion

    #region Edge Cases - Whitespace Variations

    /// <summary>
    /// Verifies that whitespace surrounding coordinates is trimmed before parsing.
    /// </summary>
    [Theory]
    [InlineData("  47.61  ,  -122.33  ")]            // ASCII spaces around both coordinates
    [InlineData("\t47.61\t,\t-122.33\t")]            // tabs around both coordinates
    [InlineData("\r\n47.61\r\n,\r\n-122.33\r\n")]    // newlines / CRLF around both coordinates
    public void ParseQuerySeedPoints_TrimsCoordinateWhitespace(string query)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // TrimEntries on the ';' and ',' splits uses char.IsWhiteSpace, so spaces, tabs and newlines
        // surrounding a coordinate are all removed before parsing.
        var points = discovery.ParseQuerySeedPoints(query);

        Assert.Single(points);
        Assert.Equal(47.61, points[0].Latitude, 3);
        Assert.Equal(-122.33, points[0].Longitude, 3);
    }

    /// <summary>
    /// Verifies that empty pairs are dropped while valid pairs are kept.
    /// </summary>
    [Theory]
    [InlineData(";47.61,-122.33;", 1)]            // leading/trailing separators drop to empty pairs; one valid remains
    [InlineData("47.61,-122.33;  ;90,-180", 2)]   // a whitespace-only pair between two valid pairs is dropped
    public void ParseQuerySeedPoints_DropsEmptyPairsKeepsValid(string query, int expectedCount)
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        var points = discovery.ParseQuerySeedPoints(query);

        Assert.Equal(expectedCount, points.Count);
    }

    #endregion

    #region Edge Cases - Stream ID Delegate

    /// <summary>
    /// Verifies that BuildSelectionItem stores whatever the caller-supplied stream-id delegate
    /// returns, without composing or mutating it. Stream-id shape (prefix, layout, ComponentId
    /// fallback) is the framework's responsibility, not this method's.
    /// </summary>
    [Fact]
    public void BuildSelectionItem_AssignsStreamIdFromDelegate()
    {
        DataSelectionItem captured = null;
        string Generator(DataSelectionItem item)
        {
            captured = item;
            return "framework.stream.id";
        }

        var discovery = new WeatherGovDiscovery(logger: null, Generator);
        var selection = discovery.BuildSelectionItem("KSEA");

        Assert.Equal("framework.stream.id", selection.StreamId);
        // The delegate must see the canonicalized station id, so the framework generator can
        // substitute it into the registered pattern.
        Assert.NotNull(captured);
        Assert.Equal("KSEA", captured.StationId);
    }

    /// <summary>
    /// Verifies that BuildSelectionItem rejects a null stream-id delegate at the boundary so a
    /// caller cannot accidentally surface items without a stream id.
    /// </summary>
    [Fact]
    public void BuildSelectionItem_NullStreamIdDelegate_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new WeatherGovDiscovery(logger: null, getDefaultStreamId: null));

        Assert.Equal("getDefaultStreamId", exception.ParamName);
    }

    #endregion

    #region Edge Cases - Field Inclusion

    /// <summary>
    /// Verifies that each selection receives an independent copy of the include fields array.
    /// </summary>
    [Fact]
    public void BuildSelectionItem_ReturnsIndependentIncludeFields()
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // IncludeFields is SupportedMeasurementFields.ToArray(), a fresh copy per call. Two selections must
        // not share one array, or mutating one would silently corrupt the other.
        var first = discovery.BuildSelectionItem("KSEA");
        var second = discovery.BuildSelectionItem("KPDX");

        Assert.NotSame(first.IncludeFields, second.IncludeFields);
        first.IncludeFields[0] = "MUTATED";
        Assert.NotEqual("MUTATED", second.IncludeFields[0]);
    }

    #endregion

    #region Dropped Seed Point Warnings

    /// <summary>
    /// Verifies that a malformed pair logs a shape warning and is dropped.
    /// </summary>
    [Theory]
    [InlineData("44.1")]                // one value, wrong shape
    [InlineData("47.61,-122.33,5")]     // three values, wrong shape
    public void ParseQuerySeedPoints_MalformedPair_LogsWarning(string query)
    {
        var logger = new Mock<ILogger>();
        var discovery = new WeatherGovDiscovery(logger.Object, StubStreamId);

        var points = discovery.ParseQuerySeedPoints(query);

        Assert.Empty(points);
        VerifyWarning(logger, "expected 'latitude,longitude'", Times.Once());
    }

    /// <summary>
    /// Verifies that a well-shaped but invalid coordinate logs a coordinate-level drop reason.
    /// </summary>
    [Theory]
    [InlineData("abc,123", "not valid numbers")]   // a well-shaped pair whose coordinates are non-numeric
    [InlineData("91,0", "latitude must be in")]     // a numeric pair whose latitude is out of range
    public void ParseQuerySeedPoints_InvalidCoordinate_LogsDropReason(string query, string expectedWarning)
    {
        // Both inputs clear the shape guard that ParseQuerySeedPoints_MalformedPair_LogsWarning covers, so
        // they are dropped for a coordinate-level reason instead. The reason text distinguishes the two paths.
        var logger = new Mock<ILogger>();
        var discovery = new WeatherGovDiscovery(logger.Object, StubStreamId);

        var points = discovery.ParseQuerySeedPoints(query);

        Assert.Empty(points);
        VerifyWarning(logger, expectedWarning, Times.Once());
    }

    /// <summary>
    /// Verifies that a valid seed point logs no warning.
    /// </summary>
    [Fact]
    public void ParseQuerySeedPoints_ValidPoint_LogsNoWarning()
    {
        var logger = new Mock<ILogger>();
        var discovery = new WeatherGovDiscovery(logger.Object, StubStreamId);

        var points = discovery.ParseQuerySeedPoints("47.61,-122.33");

        Assert.Single(points);
        // Empty substring matches any warning, so Never asserts nothing was warned.
        VerifyWarning(logger, string.Empty, Times.Never());
    }

    /// <summary>
    /// Verifies that each rejected point logs its own warning without coalescing.
    /// </summary>
    [Fact]
    public void ParseQuerySeedPoints_MultipleInvalidPoints_WarnsPerPoint()
    {
        var logger = new Mock<ILogger>();
        var discovery = new WeatherGovDiscovery(logger.Object, StubStreamId);
        // One warning per rejected point, each naming its own reason; warnings are not coalesced.

        var points = discovery.ParseQuerySeedPoints("44.1;abc,123;91,0");

        Assert.Empty(points);
        VerifyWarning(logger, "expected 'latitude,longitude'", Times.Once());
        VerifyWarning(logger, "not valid numbers", Times.Once());
        VerifyWarning(logger, "latitude must be in", Times.Once());
        VerifyWarning(logger, string.Empty, Times.Exactly(3));
    }

    /// <summary>
    /// Verifies that valid points are kept while invalid neighbours are dropped with a warning.
    /// </summary>
    [Fact]
    public void ParseQuerySeedPoints_MixedValidAndInvalid_KeepsValidAndWarnsForInvalid()
    {
        var logger = new Mock<ILogger>();
        var discovery = new WeatherGovDiscovery(logger.Object, StubStreamId);
        // The valid point is still returned; only its invalid neighbour warns, so exactly one warning fires.

        var points = discovery.ParseQuerySeedPoints("47.61,-122.33;91,0");

        Assert.Single(points);
        Assert.Equal(47.61, points[0].Latitude, 3);
        VerifyWarning(logger, "latitude must be in", Times.Once());
        VerifyWarning(logger, string.Empty, Times.Once());
    }

    /// <summary>
    /// Verifies that blank input returns before the parse loop and logs no warning.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseQuerySeedPoints_BlankInput_LogsNoWarning(string query)
    {
        var logger = new Mock<ILogger>();
        var discovery = new WeatherGovDiscovery(logger.Object, StubStreamId);
        // Blank input returns before the parse loop, so nothing is dropped and nothing is warned.

        var points = discovery.ParseQuerySeedPoints(query);

        Assert.Empty(points);
        VerifyWarning(logger, string.Empty, Times.Never());
    }

    /// <summary>
    /// Verifies that parsing stays null-safe when no logger is supplied.
    /// </summary>
    [Fact]
    public void ParseQuerySeedPoints_NullLogger_DoesNotThrow()
    {
        var discovery = new WeatherGovDiscovery(logger: null, StubStreamId);
        // The logger is optional (logger?.LogWarning), so every drop reason must stay null-safe.
        var points = discovery.ParseQuerySeedPoints("44.1;abc,123;91,0");

        Assert.Empty(points);
    }

    // Delegates to the shared verification helper so the Moq log expression lives in exactly one place.
    private static void VerifyWarning(Mock<ILogger> logger, string contains, Times times) =>
        MockLoggerHelpers.VerifyWarning(logger, contains, times);

    #endregion

}
