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
using System.Diagnostics.CodeAnalysis;
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests;

public class TestDataSelectionWithId : DataSelectionConfigurationBase, IScanDataSelectionConfiguration, IEquatable<TestDataSelectionWithId>
{
    public TestDataSelectionWithId(string id = "test", string scheduleId = null, bool selected = true, string name = "testname")
    {
        StreamId = id;
        ScheduleId = scheduleId;
        Selected = selected;
        Name = name;
    }

    public string ScheduleId { get; set; }

    public bool Equals([AllowNull] TestDataSelectionWithId other)
    {
        bool equal;
        if (other == null)
        {
            equal = false;
        }
        else
        {
            equal = ScheduleId == other.ScheduleId &&
                Name == other.Name &&
                StreamId == other.StreamId;
        }

        return equal;
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as TestDataSelectionWithId);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ScheduleId, Name, StreamId);
    }

    protected override IEnumerable<ValidationResult> ValidateConfiguration()
    {
        return new List<ValidationResult>();
    }
}
