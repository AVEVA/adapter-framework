// Copyright 2018-2026 AVEVA Group Limited
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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

[ConfigurationFacet(AdapterComponentTypePlaceholder, DataFiltersFacetName, "1.0.0")]
public class DataFiltersConfiguration : EdgeConfigurationBase, IEquatable<DataFiltersConfiguration>
{
    [Id]
    [Required]
    public string Id { get; set; }

    [MutuallyExclusiveTo(nameof(PercentChange))]
    [Range(0, double.MaxValue)]
    public double? AbsoluteDeadband { get; set; }

    [MutuallyExclusiveTo(nameof(AbsoluteDeadband))]
    [Range(0, double.MaxValue)]
    public double? PercentChange { get; set; }

    [Description($"Expiration period must have a valid timespan format (e.g. hh:mm:ss).")]
    [RegexPattern(NonNegativeTimespanRegexPattern)]
    public TimeSpan? ExpirationPeriod { get; set; }

    public bool Equals(DataFiltersConfiguration other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return Id.Equals(other.Id, StringComparison.InvariantCultureIgnoreCase) &&
            AbsoluteDeadband == other.AbsoluteDeadband && 
            PercentChange == other.PercentChange && 
            ExpirationPeriod == other.ExpirationPeriod;
    }

    public override bool Equals(object obj)
    {
        if (obj is null)
            return false;

        if (obj.GetType() != GetType())
            return false;

        return Equals((DataFiltersConfiguration)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, AbsoluteDeadband, PercentChange, ExpirationPeriod);
    }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (AbsoluteDeadband >= 0 && PercentChange >= 0)
        {
            yield return new ValidationResult($"{nameof(AbsoluteDeadband)} and {nameof(PercentChange)} cannot be configured in the same filter.");
        }

        if ((AbsoluteDeadband == null) && (PercentChange == null))
        {
            yield return new ValidationResult($"At least one of {nameof(AbsoluteDeadband)} or {nameof(PercentChange)} must be configured to filter data.");
        }

        if (ExpirationPeriod != null)
        {
            if (ExpirationPeriod <= TimeSpan.Zero)
            {
                ExpirationPeriod = null; // do not run expirationPeriod check if value is negative or zero
            }  
        }
    }
}
