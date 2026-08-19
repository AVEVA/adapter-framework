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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

/// <summary>
/// Represents a group of data messages.
/// </summary>
public class GroupedDataMessage : Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GroupedDataMessage"/> class.
    /// </summary>
    /// <param name="count">count of the data message in the group.</param>
    /// <param name="groupings">A dictionary of data messages.</param>
    /// <param name="staticGroupings">A list of static data messages with extended properties.</param>
    /// <param name="messageAction">The <see cref="Abstractions.Messages.MessageAction"/> that will be sent with the message.</param>
    /// <param name="dynamicGroupingsWithPartitionKey">A dictionary of grouped dynamic data messages with a PartitionKey.</param>
    public GroupedDataMessage(
        int count,
        Dictionary<string, (Classification Classification, List<object> Instances)> groupings,
        List<StaticDataMessage> staticGroupings,
        Dictionary<PartitionKey, Dictionary<string, List<object>>> dynamicGroupingsWithPartitionKey,
        MessageAction messageAction)
    {
        Count = count;
        Groupings = groupings;
        StaticGroupings = staticGroupings;
        DynamicGroupingsWithPartitionKey = dynamicGroupingsWithPartitionKey;
        MessageAction = messageAction;        
    }

    /// <summary>Gets the count of the grouped data message.</summary>
    /// <value>The count of the grouped data message. </value>
    public int Count { get; }

    /// <summary>Gets the grouped messages.</summary>
    /// <value>The grouped messages.</value>
    public Dictionary<string, (Classification Classification, List<object> Instances)> Groupings { get; }

    /// <summary>
    /// List of static data messages with extended properties.
    /// </summary>
    public List<StaticDataMessage> StaticGroupings { get; }

    /// <summary>
    /// The <see cref="Abstractions.Messages.MessageAction"/> value for the message.
    /// </summary>
    public MessageAction MessageAction { get; }

    /// <summary>
    /// A dictionary of grouped dynamic data messages with a PartitionKey.
    /// </summary>
    public Dictionary<PartitionKey, Dictionary<string, List<object>>> DynamicGroupingsWithPartitionKey { get; }
}
