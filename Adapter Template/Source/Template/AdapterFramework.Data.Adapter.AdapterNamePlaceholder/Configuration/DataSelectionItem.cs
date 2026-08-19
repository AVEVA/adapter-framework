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
/// Represents a data selection item configuration for the adapter.
/// </summary>
[DataContract]
[ConfigurationFacet(AdapterConstants.ComponentType, CommonConstants.DataSelectionConfigurationName, "1.0.0")]
public class DataSelectionItem : DataSelectionConfigurationBase, IEquatable<DataSelectionItem>
{
    // NOTE: You should add properties here that you will need for data item selection.

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

        // TODO: Change the implementation to perform property-wise comparison for two data selection items.
        return Selected == other.Selected && 
               Name == other.Name && 
               StreamId == other.StreamId &&
               DataFilterId == other.DataFilterId; // TODO: Add comparison for adapter specific properties. 
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
        // TODO: Change the implementation to include all properties defined in the data selection item.
        return HashCode.Combine(Selected, Name, StreamId, DataFilterId); // TODO: Add adapter specific properties. 
    }

    /// <summary>
    /// This method is automatically called by the platform to allow the adapter
    /// a chance to validate data selections.
    /// </summary>
    /// <returns>A list of validation errors. An empty list implies the data selections are all valid.</returns>
    protected override IEnumerable<ValidationResult> ValidateConfiguration()
    {
        // TODO: Implement property validations.
        yield return ValidationResult.Success;
    }
}
