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
using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.AdapterCommon;

namespace AdapterFramework.Data.Adapter.AdapterNamePlaceholder.Configuration;

/// <summary>
/// Represents the data source configuration for the adapter.
/// </summary>
[DataContract]
[ConfigurationFacet(AdapterConstants.ComponentType, CommonConstants.DataSourceConfigurationName, "1.0.0")]
public class DataSourceConfiguration : DataSourceConfigurationBase, IEquatable<DataSourceConfiguration>
{
    // NOTE: You should add properties here that you will need for data source configuration.

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

        // TODO: Add comparisons for adapter-specific data source configuration properties here.
        return StreamIdPrefix == other.StreamIdPrefix &&
               DefaultStreamIdPattern == other.DefaultStreamIdPattern;
    }

    /// <summary>
    /// Compares two DataSourceConfiguration instances as object to see if they match.
    /// </summary>
    /// <param name="obj">The object instance being compared.</param>
    /// <returns>True if 'other' equals/matches this instance.</returns>
    public override bool Equals(object obj)
    {
        // TODO: Implement the logic to compare two data source configurations as objects and delete the line below. 
        return Equals(obj as DataSourceConfiguration);
    }

    /// <summary>
    /// Get the hash code of the data source configuration instance. 
    /// </summary>
    /// <returns>The hash code of the data source configuration instance. </returns>
    public override int GetHashCode()
    {
        // TODO: Implement the logic to generate hash code for the data source configuration instance and delete the line below. 
        return default;
    }

    /// <summary>
    /// This is called automatically by the platform to allow the adapter to validate the data source configuration.
    ///
    /// Note: you can use 'AdapterHelper.DataSourceConfiguration' to retrieve the entire data source configuration.
    /// </summary>
    /// <returns>A list of validation errors. En empty list implies there are no data source configuration errors.</returns>
    protected override IEnumerable<ValidationResult> ValidateConfiguration()
    {
        // TODO: Implement property validations.
        yield return ValidationResult.Success;
    }
}
