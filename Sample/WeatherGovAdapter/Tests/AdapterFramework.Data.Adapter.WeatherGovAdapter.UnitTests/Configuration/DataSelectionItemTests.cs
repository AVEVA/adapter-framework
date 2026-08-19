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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.Configuration;

/// <summary>
/// DataSelectionItem unit tests. Covers validation, equality, and hash code behavior.
/// </summary>
public class DataSelectionItemTests
{
    /// <summary>
    /// Verifies that a valid selection produces no validation errors.
    /// </summary>
    [Fact]
    public void ValidateConfiguration_AllFieldsValid_ReturnsNoErrors()
    {
        var selection = new DataSelectionItem
        {
            StationId = "KSEA",
            IncludeFields = ["temperature_c", "dewpoint_c"],
        };

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that a blank station id produces the required-field validation error.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateConfiguration_BlankStationId_ReturnsRequiredError(string stationId)
    {
        var selection = new DataSelectionItem
        {
            StationId = stationId,
        };

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        var error = Assert.Single(errors);
        Assert.Contains(nameof(DataSelectionItem.StationId), error.ErrorMessage);
    }

    /// <summary>
    /// Verifies that a blank include-field entry produces a validation error.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateConfiguration_BlankIncludeField_ReturnsError(string includeField)
    {
        var selection = new DataSelectionItem
        {
            StationId = "KSEA",
            IncludeFields = [includeField],
        };

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        var error = Assert.Single(errors);
        Assert.Contains("cannot contain null or empty", error.ErrorMessage);
    }

    /// <summary>
    /// Verifies that validation reports only the unsupported include field, not the valid one.
    /// </summary>
    [Fact]
    public void ValidateConfiguration_MixedIncludeFields_ReturnsOffenderOnly()
    {
        var selection = Selections.Baseline();
        selection.IncludeFields = ["temperature_c", "invalid_field"];

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        var error = Assert.Single(errors);
        Assert.Contains("invalid_field", error.ErrorMessage);
        Assert.DoesNotContain("temperature_c", error.ErrorMessage);
    }

    /// <summary>
    /// Verifies that include-field validation matches supported names case-insensitively.
    /// </summary>
    [Fact]
    public void ValidateConfiguration_IncludeFieldsCaseInsensitive()
    {
        var selection = new DataSelectionItem
        {
            StationId = "KSEA",
            IncludeFields = ["TEMPERATURE_C", "DEWPOINT_C"],
        };

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that an include field that is not an exact, case-insensitive match to a supported
    /// field is rejected as unsupported.
    /// </summary>
    [Theory]
    [InlineData("invalid_field")]
    [InlineData("temperature_c ")]      // trailing whitespace
    [InlineData(" temperature_c")]      // leading whitespace
    [InlineData("temperature_c!")]      // trailing punctuation
    [InlineData("temperature_\u2103")] // unicode variant of a supported field name
    public void ValidateConfiguration_UnsupportedIncludeField_ReturnsError(string includeField)
    {
        // Any field name that is not a supported field is rejected as unsupported: a wholly unknown name,
        // or a supported name that is not an exact match. Matching is case-insensitive but otherwise
        // exact: it does not trim, strip punctuation, or normalize unicode.
        var selection = new DataSelectionItem
        {
            StationId = "KSEA",
            IncludeFields = [includeField],
        };

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        var error = Assert.Single(errors);
        Assert.Contains("unsupported", error.ErrorMessage);
    }

    /// <summary>
    /// Verifies that a null IncludeFields collection produces no validation errors.
    /// </summary>
    [Fact]
    public void ValidateConfiguration_NullIncludeFields_ReturnsNoErrors()
    {
        var selection = Selections.Baseline();
        selection.IncludeFields = null;

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that each selectable schedule id is accepted by validation.
    /// </summary>
    [Theory]
    [InlineData(AdapterConstants.DefaultScheduleId)]
    [InlineData(AdapterConstants.ScheduleIdFiveMinutes)]
    [InlineData(AdapterConstants.ScheduleIdTenMinutes)]
    public void ValidateConfiguration_ValidScheduleId_ReturnsNoErrors(string scheduleId)
    {
        var selection = Selections.Baseline();
        selection.ScheduleId = scheduleId;

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies that a schedule id outside the selectable set is rejected as unsupported.
    /// </summary>
    [Fact]
    public void ValidateConfiguration_UnsupportedScheduleId_ReturnsError()
    {
        var selection = Selections.Baseline();
        selection.ScheduleId = "99";

        var errors = selection.Validate(new ValidationContext(selection)).ToArray();

        var error = Assert.Single(errors);
        Assert.Contains(nameof(DataSelectionItem.ScheduleId), error.ErrorMessage);
    }

    /// <summary>
    /// Verifies that a missing schedule id is rejected by the Required attribute. The custom
    /// ValidateConfiguration deliberately ignores a blank schedule id (the Required attribute owns that
    /// rule), so this exercises full attribute validation rather than the IValidatableObject path.
    /// </summary>
    [Fact]
    public void ScheduleId_Missing_FailsRequiredValidation()
    {
        var selection = Selections.Baseline();
        selection.ScheduleId = null;

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            selection, new ValidationContext(selection), results, validateAllProperties: true);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(DataSelectionItem.ScheduleId)));
    }

    /// <summary>
    /// Verifies that a null IncludeFields collection is treated as equal to an empty one,
    /// including the hash code.
    /// </summary>
    [Fact]
    public void Equals_NullIncludeFields_MatchesEmpty()
    {
        var nullFields = Selections.Baseline();
        nullFields.IncludeFields = null;
        var emptyFields = Selections.Baseline();
        emptyFields.IncludeFields = Array.Empty<string>();

        Assert.True(nullFields.Equals(emptyFields));
        Assert.True(emptyFields.Equals(nullFields));
        Assert.Equal(nullFields.GetHashCode(), emptyFields.GetHashCode());
    }

    /// <summary>
    /// Verifies that selections with equivalent values are equal and share a hash code.
    /// </summary>
    [Fact]
    public void Equals_ValuesMatch_ReturnsTrue()
    {
        var left = new DataSelectionItem
        {
            StationId = "KSEA",
            IncludeFields = ["temperature_c", "dewpoint_c"],
            Selected = true,
            StreamId = "weathergov.station.ksea",
        };

        var right = new DataSelectionItem
        {
            StationId = "ksea",
            IncludeFields = ["temperature_c", "dewpoint_c"],
            Selected = true,
            StreamId = "weathergov.station.ksea",
        };

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    /// <summary>
    /// Verifies that IncludeFields comparison is case-insensitive.
    /// </summary>
    [Fact]
    public void Equals_IncludeFieldsIsCaseInsensitive()
    {
        var left = Selections.Baseline();
        var right = Selections.Baseline();
        right.IncludeFields = ["TEMPERATURE_C", "DEWPOINT_C"];

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    /// <summary>
    /// Verifies that reordering IncludeFields makes two selections unequal.
    /// </summary>
    [Fact]
    public void Equals_IncludeFieldsReordered_ReturnsFalse()
    {
        var left = Selections.Baseline();
        left.IncludeFields = ["temperature_c", "dewpoint_c"];
        var right = Selections.Baseline();
        right.IncludeFields = ["dewpoint_c", "temperature_c"];

        // IncludeFields equality is SequenceEqual: order is significant even when the set of fields is identical.
        Assert.False(left.Equals(right));
    }

    /// <summary>
    /// Verifies that changing any single field makes two otherwise-equal selections unequal.
    /// </summary>
    [Theory]
    [MemberData(nameof(EqualsSingleFieldMutations))]
    public void Equals_SingleFieldDiffers_ReturnsFalse(string field)
    {
        var left = Selections.Baseline();
        var right = Selections.Baseline();

        switch (field)
        {
            case nameof(DataSelectionItem.StationId):
                // StationId equality is case-insensitive; the matching case is pinned by Equals_ValuesMatch_ReturnsTrue.
                right.StationId = "KPDX";
                break;
            case nameof(DataSelectionItem.StreamId):
                right.StreamId = "weathergov.station.kpdx";
                break;
            case nameof(DataSelectionItem.IncludeFields):
                right.IncludeFields = ["temperature_c", "wind_speed_mps"];
                break;
            case nameof(DataSelectionItem.Selected):
                right.Selected = false;
                break;
            case nameof(DataSelectionItem.Name):
                right.Name = "Portland Station";
                break;
            case nameof(DataSelectionItem.Id):
                right.Id = "selection-2";
                break;
            case nameof(DataSelectionItem.DataFilterId):
                right.DataFilterId = "filter-2";
                break;
            case nameof(DataSelectionItem.ScheduleId):
                right.ScheduleId = AdapterConstants.ScheduleIdFiveMinutes;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, "Unhandled mutation key.");
        }

        Assert.False(left.Equals(right));
    }

    /// <summary>
    /// Verifies that the data-selection help text names the required fields and lists the selectable
    /// schedule IDs, so operators see the accepted values.
    /// </summary>
    [Fact]
    public void GetHelpInfo_IncludesRequiredFieldsAndScheduleIds()
    {
        var help = DataSelectionItem.GetHelpInfo("HEADER");

        Assert.Contains("HEADER", help);
        Assert.Contains(nameof(DataSelectionItem.StationId), help);
        Assert.Contains(nameof(DataSelectionItem.ScheduleId), help);
        Assert.Contains(AdapterConstants.DefaultScheduleId, help);
        Assert.Contains(AdapterConstants.ScheduleIdTenMinutes, help);
    }

    /// <summary>
    /// Verifies that a null object or an object of a different type is not equal.
    /// </summary>
    [Theory]
    [InlineData(null)]                          // Equals((object)null)
    [InlineData("not-a-data-selection-item")]   // Equals over a different runtime type
    public void Equals_NullOrDifferentType_ReturnsFalse(object other)
    {
        var item = Selections.Baseline();

        Assert.False(item.Equals(other));
    }

    /// <summary>
    /// Provides the field names exercised by <see cref="Equals_SingleFieldDiffers_ReturnsFalse"/>.
    /// </summary>
    public static TheoryData<string> EqualsSingleFieldMutations() =>
    [
        nameof(DataSelectionItem.StationId),
        nameof(DataSelectionItem.StreamId),
        nameof(DataSelectionItem.IncludeFields),
        nameof(DataSelectionItem.Selected),
        nameof(DataSelectionItem.Name),
        nameof(DataSelectionItem.Id),
        nameof(DataSelectionItem.DataFilterId),
        nameof(DataSelectionItem.ScheduleId),
    ];
}
