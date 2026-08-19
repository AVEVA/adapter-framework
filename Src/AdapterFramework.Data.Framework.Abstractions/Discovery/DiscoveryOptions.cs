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
namespace AdapterFramework.Data.Framework.Abstractions.Discovery;

/// <summary>
/// Contains properties to argument calls from MVC down to <see cref="IDataSourceDiscoveryManager"/>
/// </summary>
public class DiscoveryOptions
{
    public DiscoveryOptions(string scheduleId)
    {
        ScheduleId = scheduleId;
    }

    public DiscoveryOptions(int count, int skip = 0)
    {
        Count = count;
        Skip = skip;
    }

    /// <summary>
    /// Schedule ID to be assigned to newly discovered items when AutoSelect property is set to True.
    /// </summary>
    public string ScheduleId { get; }

    /// <summary>
    /// Number of items to skip and an Enumerable
    /// </summary>
    public int Skip { get; }

    /// <summary>
    /// Number of items to return from an Enumerable
    /// </summary>
    public int Count { get; }
}
