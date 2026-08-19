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
using AdapterFramework.Data.DataModel.Enum;

namespace AdapterFramework.Data.DataModel.Extensions;

/// <summary>
/// Converts certain types to a DataType's enum's values.
/// </summary>
/// <remarks> Quality is not currently supported.</remarks>
public static class EnumValuesCreator
{
    /// <summary>
    /// Converts an enumeration to an array suitable for DataType's enum's values.
    /// </summary>
    /// <typeparam name="T">A value of type Enum.</typeparam>
    /// <returns>An array of EnumValueItem containing the enumeration</returns>
    public static IEnumerable<EnumValueItem> GetEnum<T>() where T : System.Enum
    {
        var enumValueItems = new EnumValueItem[System.Enum.GetValues(typeof(T)).Length];
        var index = 0;
        foreach (var item in System.Enum.GetValues(typeof(T)))
        {
            enumValueItems[index++] = new EnumValueItem(System.Enum.GetName(typeof(T), item), item);
        }

        return enumValueItems;
    }

    /// <summary>
    /// Converts a dictionary to an array suitable for DataType's enum's values.
    /// </summary>
    /// <param name="enumeration">The enumeration to be converted.</param>
    /// <returns>An array of EnumValueItem containing the enumeration.</returns>
    public static EnumValueItem[] GetEnum<T>(IDictionary<string, T> enumeration)
    {
        return [.. enumeration.Select(x => new EnumValueItem(x.Key, x.Value))];
    }
}
