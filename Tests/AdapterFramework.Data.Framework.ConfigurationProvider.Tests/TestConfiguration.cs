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
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests;

public enum TestEnum
{
    Option1 = 0,
    Option2 = 1,
    Option3 = 2,
}

public class TestConfiguration : EdgeConfigurationBase, IEquatable<TestConfiguration>
{
    public const string ConfigName = "TestConfig";

    public string StringProp { get; set; }
    [Range(0, 45)]
    public int IntProp { get; set; }
    public TestEnum EnumProp { get; set; }
#pragma warning disable CA2227 // Collection properties should be read only
    public List<string> ListProp { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only

    public TimeSpan? TimeSpanProp { get; set; }

    public static bool AreEqual(TestConfiguration first, TestConfiguration second)
    {
        return first.StringProp == second.StringProp &&
               first.IntProp == second.IntProp &&
               first.EnumProp == second.EnumProp &&
               first.ListProp.SequenceEqual(second.ListProp) &&
               first.TimeSpanProp == second.TimeSpanProp;
    }

    public bool Equals(TestConfiguration other)
    {
        return StringProp == other.StringProp &&
               IntProp == other.IntProp &&
               EnumProp == other.EnumProp &&
               ListProp.SequenceEqual(other.ListProp) &&
               TimeSpanProp == other.TimeSpanProp;
    }

    public override bool Equals(object other)
    {
        return other != null && other is TestConfiguration configuration && Equals(configuration);
    }

    public override IEnumerable<ValidationResult> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(StringProp))
        {
            errors.Add($"{nameof(StringProp)} cannot be null or empty.");
        }

        foreach (var error in errors)
        {
            yield return new ValidationResult(error);
        }
    }

    public override int GetHashCode() => HashCode.Combine(StringProp, IntProp, EnumProp, ListProp, TimeSpanProp);
}
