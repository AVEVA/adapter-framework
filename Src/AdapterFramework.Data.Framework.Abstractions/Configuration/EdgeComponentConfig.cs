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
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// Defines Edge System Component configuration.
/// </summary>
[ConfigurationFacet(SystemComponentId, ComponentsFacetName, "1.0.0")]
public class EdgeComponentConfig : EdgeConfigurationBase
{
    private const string UnsupportedCharactersMessage = "The supplied component id {0} contains the following unsupported characters: {1} .";
    private const string LeadingOrTrailingSpacesMessage = "The supplied component id {0} contains leading or trailing spaces, which is not supported.";

    /// <summary>
    /// Defines ID of <see cref="IEdgeComponent"/>.
    /// </summary>
    [Id]
    [Required]
    [MaxLength(MaximumComponentIdLength)]
    public string ComponentId { get; set; }

    /// <summary>
    /// Defines Type of <see cref="IEdgeComponent"/>.
    /// </summary>
    [Required]
    public string ComponentType { get; set; }

    public override string ToString()
    {
        return $"{nameof(ComponentId)}: {ComponentId}, {nameof(ComponentType)}: {ComponentType}";
    }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (ComponentId.ToOmfIdentifier().Length > MaximumComponentIdLength)
        {
            var message = string.Format(
                CultureInfo.InvariantCulture,
                MaximumIdentifierLengthExceededError,
                nameof(ComponentId),
                MaximumComponentIdLength);

            yield return new ValidationResult(message);
        }

        if (ComponentId.ContainsEdsIdentifierUnsupportedCharacters(out var unsupportedCharacters))
        {
            yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, UnsupportedCharactersMessage, ComponentId, string.Join(" ", unsupportedCharacters)));
        }

        if (ComponentId.ContainsLeadingOrTrailingSpaces())
        {
            yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, LeadingOrTrailingSpacesMessage, ComponentId));
        }
    }
}
