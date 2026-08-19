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
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

public abstract class DataSourceConfigurationBase : EdgeConfigurationBase, IDataSourceConfiguration
{
    [MaxLength(MaximumStreamIdPrefixLength)]
    public string StreamIdPrefix { get; set; }

    [MinLength(OneCharacter)]
    public string DefaultStreamIdPattern { get; set; }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (!string.IsNullOrWhiteSpace(StreamIdPrefix))
        {
            if (StreamIdPrefix.ToOmfIdentifier().Length > MaximumStreamIdPrefixLength)
            {
                yield return new ValidationResult(string.Format(
                    CultureInfo.InvariantCulture,
                    MaximumIdentifierLengthExceededError,
                    nameof(StreamIdPrefix),
                    MaximumStreamIdPrefixLength));
            }
        }

        foreach (var validationResult in ValidateConfiguration())
        {
            if (validationResult != null)
            {
                yield return validationResult;
            }
        }
    }

    /// <summary>
    /// This method should be used for any adapter-specific property validation.
    /// </summary>
    /// <returns>An enumerable of the validation results.</returns>
    protected abstract IEnumerable<ValidationResult> ValidateConfiguration();
}
