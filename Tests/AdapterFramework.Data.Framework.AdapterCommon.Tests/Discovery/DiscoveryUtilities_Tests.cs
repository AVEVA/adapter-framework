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
using AdapterFramework.Data.Framework.AdapterCommon.Discovery;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Discovery;

public class DiscoveryUtilities_Tests
{
    #region MergeConfigurations

    [Theory]
    [InlineData(false, "original1", "original2", "new1", "new2")]
    [InlineData(true, "original1", "original2", "new1", "new2")]
    public void MergeConfigurationsTest_AllMembersUnique(bool selected, params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0]),
            new TestDataSelectionWithId(streamIds[1]),
        };

        var newDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[2], selected: false),
            new TestDataSelectionWithId(streamIds[3], selected: false),
        };

        var mergedDataSelections = DiscoveryUtilities.MergeConfigurations(currentDataSelections, newDataSelections, selected).ToArray();
        Assert.Equal(mergedDataSelections.Length, currentDataSelections.Length + newDataSelections.Length);
        Assert.True(mergedDataSelections[0].Selected);
        Assert.True(mergedDataSelections[1].Selected);
        Assert.Equal(selected, mergedDataSelections[2].Selected);
        Assert.Equal(selected, mergedDataSelections[3].Selected);
    }

    [Theory]
    [InlineData(false, "original1", "original2", "new1", "new2")]
    [InlineData(true, "original1", "original2", "new1", "new2")]
    public void MergeConfigurationsTest_AllMembersUnique_Dictionary(bool selected, params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0]),
            new TestDataSelectionWithId(streamIds[1]),
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], selected: false) },
            { streamIds[3], new TestDataSelectionWithId(streamIds[3], selected: false) },
        };

        var newDataSelectionsOriginalCount = newDataSelections.Count;

        var mergedDataSelections = DiscoveryUtilities.MergeConfigurations(currentDataSelections, newDataSelections, selected).ToArray();
        Assert.Equal(mergedDataSelections.Length, currentDataSelections.Length + newDataSelectionsOriginalCount);

        foreach (var selection in mergedDataSelections)
        {
            if (selection.StreamId.Contains("original"))
            {
                Assert.True(selection.Selected);
            }
            else
            {
                Assert.Equal(selected, selection.Selected);
            }
        }
    }

    [Theory]
    [InlineData(false, "original1", "original2", "originaL1", "new2")]
    [InlineData(true, "original1", "original2", "oriGinal1", "new2")]
    public void MergeConfigurationsTest_SomeMembersUnique(bool selected, params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0]),
            new TestDataSelectionWithId(streamIds[1]),
        };

        var newDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[2], selected: false),
            new TestDataSelectionWithId(streamIds[3], selected: false),
        };

        var mergedDataSelections = DiscoveryUtilities.MergeConfigurations(currentDataSelections, newDataSelections, selected).ToArray();
        Assert.NotEqual(mergedDataSelections.Length, currentDataSelections.Length + newDataSelections.Length);
        Assert.True(mergedDataSelections[0].Selected);
        Assert.True(mergedDataSelections[1].Selected);
        Assert.Equal(streamIds[3], mergedDataSelections[2].StreamId);
        Assert.Equal(selected, mergedDataSelections[2].Selected);
    }

    [Theory]
    [InlineData(false, "original1", "original2", "Original1", "new2")]
    [InlineData(true, "original1", "original2", "oriGinal1", "new2")]
    public void MergeConfigurationsTest_SomeMembersUnique_Dictionary(bool selected, params string[] streamIds)
    {
        var originalScheduleId = "abc";
        var newScheduleId = "123";
        
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], originalScheduleId),
            new TestDataSelectionWithId(streamIds[1], originalScheduleId),
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], newScheduleId, false) },
            { streamIds[3], new TestDataSelectionWithId(streamIds[3], newScheduleId, false) },
        };

        var newDataSelectionsOriginalCount = newDataSelections.Count;

        var mergedDataSelections = DiscoveryUtilities.MergeConfigurations(currentDataSelections, newDataSelections, selected).ToArray();
        Assert.NotEqual(mergedDataSelections.Length, currentDataSelections.Length + newDataSelectionsOriginalCount);

        foreach (var selection in mergedDataSelections)
        {
            if (selection.StreamId.Contains("original"))
            {
                Assert.True(selection.Selected);
                Assert.Equal(originalScheduleId, selection.ScheduleId);
            }
            else
            {
                Assert.Equal(selected, selection.Selected);
                Assert.Equal(newScheduleId, selection.ScheduleId);
            }
        }
    }

    [Theory]
    [InlineData(false, "original1", "original2", "oRiginal1", "orIginal2")]
    [InlineData(true, "original1", "original2", "oriGinal1", "Original2")]
    public void MergeConfigurationsTest_NoMembersUnique(bool selected, params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[2], "123", selected: false),
            new TestDataSelectionWithId(streamIds[3], "456", selected: false),
        };

        var mergedDataSelections = DiscoveryUtilities.MergeConfigurations(currentDataSelections, newDataSelections, selected).ToArray();
        Assert.Equal(mergedDataSelections.Length, currentDataSelections.Length);
        Assert.True(mergedDataSelections[0].Selected);
        Assert.True(mergedDataSelections[1].Selected);
        Assert.Equal(currentDataSelections[0].ScheduleId, mergedDataSelections[0].ScheduleId);
        Assert.Equal(currentDataSelections[1].ScheduleId, mergedDataSelections[1].ScheduleId);
    }

    [Theory]
    [InlineData(false, "original1", "original2", "originAl1", "Original2")]
    [InlineData(true, "original1", "original2", "oriGinal1", "oRiginal2")]
    public void MergeConfigurationsTest_NoMembersUnique_Dictionary(bool selected, params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], "123", selected: false) },
            { streamIds[3],  new TestDataSelectionWithId(streamIds[3], "456", selected: false) },
        };

        var mergedDataSelections = DiscoveryUtilities.MergeConfigurations(currentDataSelections, newDataSelections, selected).ToArray();
        Assert.Equal(mergedDataSelections.Length, currentDataSelections.Length);
        Assert.True(mergedDataSelections[0].Selected);
        Assert.True(mergedDataSelections[1].Selected);
        Assert.Equal(currentDataSelections[0].ScheduleId, mergedDataSelections[0].ScheduleId);
        Assert.Equal(currentDataSelections[1].ScheduleId, mergedDataSelections[1].ScheduleId);
    }

    #endregion

    #region DiffConfigurations

    [Theory]
    [InlineData("original1", "original2", "new1", "new2")]
    public void DiffConfigurationsTest_AllUnique(params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[2], "123"),
            new TestDataSelectionWithId(streamIds[3], "456"),
        };

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Equal(diffConfigurations.Length, newDataSelections.Length);
        Assert.Equal(diffConfigurations[0].StreamId, streamIds[2]);
        Assert.Equal(diffConfigurations[1].StreamId, streamIds[3]);
    }

    [Theory]
    [InlineData("original1", "original2", "new1", "new2")]
    public void DiffConfigurationsTest_AllUnique_EnumDictionary(params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], "123") },
            { streamIds[3],  new TestDataSelectionWithId(streamIds[3], "456") },
        };

        var newDataSelectionsOriginalCount = newDataSelections.Count;

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Equal(diffConfigurations.Length, newDataSelectionsOriginalCount);
        Assert.Equal(diffConfigurations[0].StreamId, streamIds[2]);
        Assert.Equal(diffConfigurations[1].StreamId, streamIds[3]);
    }

    [Theory]
    [InlineData("original1", "original2", "new1", "new2")]
    public void DiffConfigurationsTest_AllUnique_DictionaryDictionary(params string[] streamIds)
    {
        var currentDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[0], new TestDataSelectionWithId(streamIds[0], "abc") },
            { streamIds[1], new TestDataSelectionWithId(streamIds[1], "def") },
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], "123") },
            { streamIds[3],  new TestDataSelectionWithId(streamIds[3], "456") },
        };

        var newDataSelectionsOriginalCount = newDataSelections.Count;

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Equal(diffConfigurations.Length, newDataSelectionsOriginalCount);
        Assert.Equal(diffConfigurations[0].StreamId, streamIds[2]);
        Assert.Equal(diffConfigurations[1].StreamId, streamIds[3]);
    }

    [Theory]
    [InlineData("original1", "original2", "origiNal1", "new2")]
    public void DiffConfigurationsTest_SomeUnique(params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[2], "123"),
            new TestDataSelectionWithId(streamIds[3], "dEf"),
        };

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Equal(diffConfigurations.Length, newDataSelections.Length - 1);
        Assert.Equal(diffConfigurations[0].StreamId, streamIds[3]);
    }

    [Theory]
    [InlineData("original1", "original2", "Original1", "new2")]
    public void DiffConfigurationsTest_SomeUnique_EnumDictionary(params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], "123") },
            { streamIds[3], new TestDataSelectionWithId(streamIds[3], "Def") },
        };

        var newDataSelectionsOriginalCount = newDataSelections.Count;

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Equal(diffConfigurations.Length, newDataSelectionsOriginalCount - 1);
        Assert.Equal(diffConfigurations[0].StreamId, streamIds[3]);
    }

    [Theory]
    [InlineData("original1", "original2", "orIginal1", "new2")]
    public void DiffConfigurationsTest_SomeUnique_DictionaryDictionary(params string[] streamIds)
    {
        var currentDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[0], new TestDataSelectionWithId(streamIds[0], "abc") },
            { streamIds[1], new TestDataSelectionWithId(streamIds[1], "def") },
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], "123") },
            { streamIds[3], new TestDataSelectionWithId(streamIds[3], "deF") },
        };

        var newDataSelectionsOriginalCount = newDataSelections.Count;

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Equal(diffConfigurations.Length, newDataSelectionsOriginalCount - 1);
        Assert.Equal(diffConfigurations[0].StreamId, streamIds[3]);
    }

    [Theory]
    [InlineData("original1", "original2", "origInal1", "Original2")]
    public void DiffConfigurationsTest_NoUnique(params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[2], "123"),
            new TestDataSelectionWithId(streamIds[3], "456"),
        };

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Empty(diffConfigurations);
    }

    [Theory]
    [InlineData("original1", "original2", "oRiginal1", "orIGinal2")]
    public void DiffConfigurationsTest_NoUnique_EnumDictionary(params string[] streamIds)
    {
        var currentDataSelections = new[]
        {
            new TestDataSelectionWithId(streamIds[0], "abc"),
            new TestDataSelectionWithId(streamIds[1], "def"),
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], "123") },
            { streamIds[3], new TestDataSelectionWithId(streamIds[3], "456") },
        };

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Empty(diffConfigurations);
    }

    [Theory]
    [InlineData("original1", "original2", "ORiginal1", "originAL2")]
    public void DiffConfigurationsTest_NoUnique_DictionaryDictionary(params string[] streamIds)
    {
        var currentDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[0], new TestDataSelectionWithId(streamIds[0], "abc") },
            { streamIds[1], new TestDataSelectionWithId(streamIds[1], "def") },
        };

        var newDataSelections = new Dictionary<string, TestDataSelectionWithId>(StringComparer.OrdinalIgnoreCase)
        {
            { streamIds[2], new TestDataSelectionWithId(streamIds[2], "123") },
            { streamIds[3], new TestDataSelectionWithId(streamIds[3], "456") },
        };

        var diffConfigurations = DiscoveryUtilities.CompareConfigurations(currentDataSelections, newDataSelections).ToArray();
        Assert.Empty(diffConfigurations);
    }

    #endregion
}
