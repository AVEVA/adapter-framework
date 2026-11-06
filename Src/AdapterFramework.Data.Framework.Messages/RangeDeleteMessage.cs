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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

/// <summary>
/// An OMF 2.0 streaming data range delete, kept separate from value batches.
/// </summary>
public sealed class RangeDeleteMessage : Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RangeDeleteMessage"/> class.
    /// </summary>
    /// <param name="id">The stream identifier.</param>
    /// <param name="range">The time interval to delete.</param>
    /// <param name="partitionKey">The partition used for delivery.</param>
    public RangeDeleteMessage(string id, StreamingDataRange range, PartitionKey? partitionKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(range);
        if (partitionKey.HasValue && !Enum.IsDefined(partitionKey.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(partitionKey));
        }

        Id = id;
        Range = range;
        PartitionKey = partitionKey;
    }

    /// <summary>Gets the stream identifier.</summary>
    [JsonPropertyName("id")]
    public string Id { get; }

    /// <summary>Gets the time interval to delete.</summary>
    [JsonPropertyName("range")]
    public StreamingDataRange Range { get; }

    /// <summary>Gets the partition used for delivery.</summary>
    [JsonIgnore]
    public PartitionKey? PartitionKey { get; }
}
