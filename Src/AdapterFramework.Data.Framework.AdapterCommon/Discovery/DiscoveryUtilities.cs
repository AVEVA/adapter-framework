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
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.AdapterCommon.Discovery;

public static class DiscoveryUtilities
{
    public static IEnumerable<TSelection> MergeConfigurations<TSelection>(IReadOnlyList<TSelection> originalConfiguration, IEnumerable<TSelection> discoveredConfiguration, bool setSelected = false, string scheduleId = null) where TSelection : class, IDataSelectionConfiguration
    {
        ThrowHelper.ThrowIfArgumentNull(discoveredConfiguration, nameof(discoveredConfiguration));

        ModifySelectionItems(discoveredConfiguration, setSelected, scheduleId);

        return originalConfiguration.Union(discoveredConfiguration, new DataSelectionConfigurationComparer<TSelection>());
    }

    public static IEnumerable<TSelection> MergeConfigurations<TSelection>(IReadOnlyList<TSelection> originalConfiguration, IDictionary<string, TSelection> discoveredConfiguration, bool setSelected = false, string scheduleId = null) where TSelection : class, IDataSelectionConfiguration
    {
        ThrowHelper.ThrowIfArgumentNull(originalConfiguration, nameof(originalConfiguration));
        ThrowHelper.ThrowIfArgumentNull(discoveredConfiguration, nameof(discoveredConfiguration));

        ModifySelectionItems(discoveredConfiguration.Values, setSelected, scheduleId);

        foreach (var originalConfigurationItem in originalConfiguration)
        {
            discoveredConfiguration[originalConfigurationItem.StreamId] = originalConfigurationItem;
        }

        return discoveredConfiguration.Values;
    }

    public static IEnumerable<TSelection> CompareConfigurations<TSelection>(IReadOnlyList<TSelection> sourceConfiguration, IEnumerable<TSelection> targetConfiguration) where TSelection : class, IDataSelectionConfiguration
    {
        return targetConfiguration.Except(sourceConfiguration, new DataSelectionConfigurationComparer<TSelection>());
    }

    public static IEnumerable<TSelection> CompareConfigurations<TSelection>(IReadOnlyList<TSelection> sourceConfiguration, IDictionary<string, TSelection> targetConfiguration) where TSelection : class, IDataSelectionConfiguration
    {
        ThrowHelper.ThrowIfArgumentNull(sourceConfiguration, nameof(sourceConfiguration));
        ThrowHelper.ThrowIfArgumentNull(targetConfiguration, nameof(targetConfiguration));

        foreach (var sourceConfigurationItem in sourceConfiguration)
        {
            targetConfiguration.Remove(sourceConfigurationItem.StreamId);
        }

        return targetConfiguration.Values;
    }

    public static IEnumerable<TSelection> CompareConfigurations<TSelection>(IReadOnlyDictionary<string, TSelection> firstConfiguration, IDictionary<string, TSelection> secondConfiguration) where TSelection : class, IDataSelectionConfiguration
    {
        ThrowHelper.ThrowIfArgumentNull(firstConfiguration, nameof(firstConfiguration));
        ThrowHelper.ThrowIfArgumentNull(secondConfiguration, nameof(secondConfiguration));

        foreach (var firstConfigurationItem in firstConfiguration)
        {
            secondConfiguration.Remove(firstConfigurationItem.Key);
        }

        return secondConfiguration.Values;
    }

    private static void ModifySelectionItems<TSelection>(IEnumerable<TSelection> discoveredConfiguration, bool setSelected, string scheduleId) where TSelection : class, IDataSelectionConfiguration
    {
        if (setSelected || scheduleId != null)
        {
            var setScheduleId = !string.IsNullOrWhiteSpace(scheduleId) && typeof(IScanDataSelectionConfiguration).IsAssignableFrom(typeof(TSelection));

            foreach (var dataSelectionItem in discoveredConfiguration)
            {
                if (setSelected)
                {
                    dataSelectionItem.Selected = true;
                }

                if (setScheduleId)
                {
                    ((IScanDataSelectionConfiguration)dataSelectionItem).ScheduleId = scheduleId;
                }
            }
        }
    }
}
