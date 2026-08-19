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
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests;

internal class TestDiscoveryResult : EdgeConfigurationBase, IEquatable<TestDiscoveryResult>
{
    public const string DiscoveryId = "TestDiscoveryResult";
    
    public bool Selected { get; set; }

    public string Name { get; set; }

    public string StreamId { get; set; }

    public static bool AreEqual(TestDiscoveryResult first, TestDiscoveryResult second)
    {
        return first.Selected == second.Selected &&
               first.Name == second.Name &&
               first.StreamId == second.StreamId;
    }

    public bool Equals(TestDiscoveryResult other)
    {
        return Selected == other.Selected &&
               Name == other.Name &&
               StreamId == other.StreamId;
    }

    public override bool Equals(object other)
    {
        return other != null && other is TestDiscoveryResult result && Equals(result);
    }

    public override IEnumerable<ValidationResult> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(Name))
        {
            errors.Add($"{nameof(Name)} cannot be null or empty.");
        }

        if (string.IsNullOrEmpty(StreamId))
        {
            errors.Add($"{nameof(StreamId)} cannot be null or empty.");
        }

        foreach (var error in errors)
        {
            yield return new ValidationResult(error);
        }
    }

    public override int GetHashCode() => HashCode.Combine(Selected, Name, StreamId);
}
