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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// The base class that implements <see cref="IValidatableObject"/> interface and yields an error when unknown fields are specified in Json payload.
/// The <see cref="Validate(ValidationContext)"/> calls also abstract <see cref="Validate()"/> method where
/// an object specific validation logic needs to be implemented.
/// </summary>
public abstract class EdgeConfigurationBase : IValidatableObject
{
    #region Public Fields

#pragma warning disable CA2227 // Collection properties should be read only
    [JsonExtensionData]
    public IDictionary<string, JsonElement> JsonExtensionData { get; set; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
#pragma warning restore CA2227 // Collection properties should be read only

    #endregion

    #region Static Methods

    /// <summary>
    /// Used for testing purpose. It combines validation results from DataAnnotations and IValidatableObject.Validate method implementation
    /// </summary>
    /// <param name="obj">Configuration object for validation.</param>
    /// <returns>A collection that holds failed-validation information.</returns>
    public static IReadOnlyList<ValidationResult> ValidateConfiguration(object obj)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(obj, null, null);
        Validator.TryValidateObject(obj, validationContext, validationResults, true);
        return validationResults;
    }

    #endregion

    #region Public Abstract Methods

    /// <summary>
    /// Provides way for an object to be invalidated.
    /// </summary>
    /// <returns>A collection that holds failed-validation information.</returns>
    public abstract IEnumerable<ValidationResult> Validate();

    #endregion

    #region IValidatableObject Implementation

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var customValidationResults = Validate();
        foreach (var validationResult in customValidationResults)
        {
            yield return validationResult;
        }

        if (JsonExtensionData.Count > 0)
        {
            var invalidFields = new StringBuilder();
            foreach (var kv in JsonExtensionData)
            {
                invalidFields.Append(invalidFields.Length > 0 ? ", " : string.Empty);
                invalidFields.Append(kv.Key);
            }

            yield return new ValidationResult($"Invalid fields specified - {invalidFields}");
        }
    }

    #endregion
}
