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
using System.Text.Json.Serialization;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Failover.Configuration;

[ConfigurationFacet(SystemComponentId, ClientFailoverFacetName, "1.0.0", false)]
public class ClientFailoverConfiguration : EndpointConfigurationBase, IEquatable<ClientFailoverConfiguration>
{
    // Note: keep FailoverTimeout's Description attribute in sync with the following timespan value
    private readonly TimeSpan _minTimeout = TimeSpan.FromSeconds(15);

    [Required]
    public string FailoverGroupId { get; set; }

    [MinLength(OneCharacter)]
    public string Name { get; set; }

    [MinLength(OneCharacter)]
    public string Description { get; set; }

    [Description($"Minimum failover timeout must be 00:00:15.")]
    [RegexPattern(NonNegativeTimespanRegexPattern)]
    public TimeSpan FailoverTimeout { get; set; } = TimeSpan.FromSeconds(60);

    [EnumOptions(false, 0)]
    public FailoverMode Mode { get; set; } = FailoverMode.Hot;

    [JsonIgnore]
    public override string Id { get; set; }

    [JsonIgnore]
    public override DateTime? DebugExpiration { get; set; }

    public bool Equals(ClientFailoverConfiguration other)
    {
        if (other == null)
        {
            return false;
        }

        return FailoverGroupId == other.FailoverGroupId &&
               Name == other.Name &&
               Description == other.Description &&
               FailoverTimeout == other.FailoverTimeout &&
               Mode == other.Mode &&
               base.Equals(other);
    }

    public override bool Equals(object obj)
    {
        return obj switch
        {
            ClientFailoverConfiguration other => Equals(other),
            _ => false,
        };
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FailoverGroupId, Name, Description, FailoverTimeout, Mode, base.GetHashCode());
    }

    public override IEnumerable<ValidationResult> Validate()
    {
        var errors = new List<ValidationResult>();

        errors.AddRange(base.Validate());

        if (FailoverTimeout < _minTimeout)
        {
            errors.Add(new ValidationResult($"{nameof(FailoverTimeout)} - cannot be less than {_minTimeout}."));
        }

        if (Mode != FailoverMode.Hot && Mode != FailoverMode.Cold && Mode != FailoverMode.Warm)
        {
            errors.Add(new ValidationResult($"{nameof(Mode)} - must be set to one of the valid failover modes. " +
                $"Valid failover modes: {nameof(FailoverMode.Hot)}, {nameof(FailoverMode.Warm)} or {nameof(FailoverMode.Cold)}"));
        }

        foreach (var error in errors)
        {
            yield return error;
        }
    }
}
