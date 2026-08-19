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
/// Provides a definition for a bulked data message.
/// </summary>
public class BulkDataMessage : Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataMessage"/> class.
    /// </summary>
    /// <param name="id">The ID of the data message, which refers to either typeid or containerid.</param>
    /// <param name="classification">OMF classification <see cref="DataModel.Classification"/> of the message.</param>
    /// <param name="instances">The values of the data message.</param>
    /// <param name="messageAction"> The <see cref="Abstractions.Messages.MessageAction"/> that will be sent with the message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    public BulkDataMessage(string id, Classification classification, IReadOnlyList<object> instances, MessageAction messageAction, PartitionKey? partitionKey = null)
    {
        Id = id;
        Classification = classification;
        Instances = instances;
        MessageAction = messageAction;
        PartitionKey = partitionKey;
    }

    /// <summary>Gets the ID of the data message, which refers to either typeid or containerid.</summary>
    /// <value>The ID of the data message, which refers to either typeid or containerid.</value>
    public string Id { get; }

    /// <summary>Gets the <see cref="DataModel.Classification"/> of the data message.</summary>
    /// <value>The <see cref="DataModel.Classification"/> of the data message.</value>
    public Classification Classification { get; }

    /// <summary>Gets the values of the data message. </summary>
    /// <value>The values of the data message.</value>
    public IReadOnlyList<object> Instances { get; }

    /// <summary>Gets the <see cref="Abstractions.Messages.MessageAction"/> value of the data message.</summary>
    /// <value>The <see cref="Abstractions.Messages.MessageAction"/> value of the data message.</value>
    public MessageAction MessageAction { get; }

    /// <summary>Gets the PartitionKey of the data message. </summary>
    public PartitionKey? PartitionKey { get; }
}
