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
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

[ConfigurationFacet(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.GeneralFacetName, "1.0.0", false)]
public class GeneralConfiguration : EdgeConfigurationBase, IAdapterGeneralConfiguration
{
    private const string EnableMetadataPropertyName = "Enablemetadata";
    private const int HealthPrefixMaxLength = 100;

    public GeneralConfiguration()
    {
        EnableDiagnostics = true;
        MetadataLevel = MetadataInfo.Medium;
        IncludeSourceProperties = StreamProperties.All;
    }

    public bool EnableDiagnostics { get; set; }

    [EnumDataType(typeof(MetadataInfo))]
    public MetadataInfo MetadataLevel { get; set; }

    [FlagsEnumToArray]
    public StreamProperties IncludeSourceProperties { get; set; }

    [MaxLength(HealthPrefixMaxLength)]
    public string HealthPrefix { get; set; }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (JsonExtensionData.TryGetValue(EnableMetadataPropertyName, out var enableMetadata))
        {
            MetadataLevel = enableMetadata.GetBoolean() ? MetadataInfo.High : MetadataInfo.Low;

            JsonExtensionData.Remove(EnableMetadataPropertyName);
        }

        if (!string.IsNullOrEmpty(HealthPrefix))
        {
            HealthPrefix = HealthPrefix.ToOmfIdentifier();

            if (HealthPrefix.Length > HealthPrefixMaxLength)
            {
                yield return new ValidationResult(
                    $"{nameof(HealthPrefix)} - parameter must have {HealthPrefixMaxLength} characters or fewer: {HealthPrefix}");
            }

            if (string.IsNullOrWhiteSpace(HealthPrefix))
            {
                yield return new ValidationResult($"{nameof(HealthPrefix)} - parameter cannot contain only whitespace.");
            }
        }
    }
}
