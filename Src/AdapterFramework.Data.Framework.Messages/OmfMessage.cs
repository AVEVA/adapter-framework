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
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

/// <summary>
/// Provides a generic class for OMF messages.
/// </summary>
/// <typeparam name="T">OMF message type.</typeparam>
public class OmfMessage<T> : Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OmfMessage{T}"/> class.
    /// </summary>
    /// <param name="count">Message count.</param>
    /// <param name="values">An array of values of message of type <typeparamref name="T"/>.</param>
    /// <param name="messageAction">The <see cref="Abstractions.Messages.MessageAction"/> that will be sent with the message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    public OmfMessage(int count, T[] values, MessageAction messageAction, PartitionKey? partitionKey = null)
    {
        Count = count;
        Values = values;
        MessageAction = messageAction;
        PartitionKey = partitionKey;
    }

    /// <summary>Gets the value count of the OMF message.</summary>
    /// <value>The value count of the OMF message.</value>
    public int Count { get; }

    /// <summary>Gets the value array of the OMF message.</summary>
    /// <value>The value array of the OMF message.</value>
    public T[] Values { get; }

    /// <summary>Gets the <see cref="Abstractions.Messages.MessageAction"/> value of the OMF message.</summary>
    /// <value>The <see cref="Abstractions.Messages.MessageAction"/> value of the OMF message.</value>
    public MessageAction MessageAction { get; }

    /// <summary>The PartitionKey of the OMF message.</summary>
    public PartitionKey? PartitionKey { get; }
}
