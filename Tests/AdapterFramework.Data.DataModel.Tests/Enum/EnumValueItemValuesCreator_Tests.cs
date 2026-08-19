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
using System.Linq;
using AdapterFramework.Data.DataModel.Extensions;
using Xunit;

namespace AdapterFramework.Data.DataModel.Tests.Enum;

public class EnumValueItemValuesCreator_Tests
{
    private readonly Dictionary<string, int> _daysDictionary = new()
    {
        { "Tuesday", 2 },
        { "Wednesday", 3 },
        { "Thursday", 3 },
        { "Friday", 3 },
        { "Saturday", 3 },
        { "Sunday", 3 },
    };

    private enum Days
    {
        Tuesday = 2,
        Wednesday = 3,
        Thursday = 4,
        Friday = 5,
        Saturday = 6,
        Sunday = 7,
    }

    [Fact]
    public void GetEnum_WithEnum_Test()
    {
        var enumValueItems = EnumValuesCreator.GetEnum<Days>();

        foreach (var item in enumValueItems)
        {
            Assert.True(System.Enum.IsDefined(typeof(Days), item.Value));
            Assert.Equal(System.Enum.GetName(typeof(Days), item.Value), item.Name, ignoreCase: true);
        }

        var keys = new HashSet<string>(enumValueItems.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var item in System.Enum.GetNames(typeof(Days)))
        {
            Assert.Contains(item, keys);
        }

        Assert.Equal(System.Enum.GetValues(typeof(Days)).Length, enumValueItems.Count());
    }

    [Fact]
    public void GetEnum_WithDictionary_Test()
    {
        var enumValueItems = EnumValuesCreator.GetEnum(_daysDictionary);

        foreach (var item in enumValueItems)
        {
            Assert.True(_daysDictionary.ContainsKey(item.Name));
            Assert.True(_daysDictionary[item.Name] == (int)item.Value);
        }

        var keys = new HashSet<string>(enumValueItems.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var item in _daysDictionary.Keys)
        {
            Assert.Contains(item, keys);
        }

        Assert.Equal(_daysDictionary.Count, enumValueItems.Length);
    }
}
