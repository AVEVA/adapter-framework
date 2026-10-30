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
using System.Text.Json.Serialization;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// A bounded time interval of streaming data to delete using OMF 2.0.
/// </summary>
public sealed class StreamingDataRange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StreamingDataRange"/> class.
    /// </summary>
    /// <param name="startTime">The start of the interval.</param>
    /// <param name="endTime">The end of the interval, at or after the start.</param>
    public StreamingDataRange(DateTimeOffset startTime, DateTimeOffset endTime)
    {
        if (endTime < startTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), "The end time must be at or after the start time.");
        }

        StartTime = startTime.ToUniversalTime();
        EndTime = endTime.ToUniversalTime();
    }

    /// <summary>Gets the start of the interval in UTC.</summary>
    [JsonPropertyName("starttime")]
    public DateTimeOffset StartTime { get; }

    /// <summary>Gets the end of the interval in UTC.</summary>
    [JsonPropertyName("endtime")]
    public DateTimeOffset EndTime { get; }
}
