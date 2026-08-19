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
using System.Text.RegularExpressions;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

[ConfigurationFacet(ManagementComponentId, SecretsFacetName, "1.0.0")]
public class ManagedSecretConfiguration : EdgeConfigurationBase, ISecretConfiguration, IEquatable<ManagedSecretConfiguration>
{
    [Id]
    [Required]
    public string Id { get; set; }

    public string Description { get; set; }

    public DateTime? ExpirationDate { get; set; }

    [Protected]
    [Required]
    public string Value { get; set; }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (Id.Contains('{') || Id.Contains('}'))
        {
            yield return new ValidationResult($"{nameof(Id)} cannot contain '{{' or '}}' characters.");
        }

        if (Regex.IsMatch(Value, SecretIdPlaceholderPattern))
        {
            yield return new ValidationResult($"{nameof(Value)} cannot start with '{{{{{{' and end with '}}}}}}'.");
        }
    }

    public bool Equals(ManagedSecretConfiguration other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Id == other.Id &&
               Description == other.Description &&
               Nullable.Equals(ExpirationDate, other.ExpirationDate) &&
               Value == other.Value;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((ManagedSecretConfiguration)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Description, ExpirationDate, Value);
    }
}
