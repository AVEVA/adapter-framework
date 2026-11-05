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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

public class RelationshipMessage : Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RelationshipMessage"/> class.
    /// </summary>
    /// <param name="relationship">The OMF Relationship (Link) definition.</param>
    /// <param name="messageAction">The <see cref="Abstractions.Messages.MessageAction"/> that describes the intent for this relationship.</param>
    public RelationshipMessage(Link relationship, MessageAction messageAction)
        : this(relationship, messageAction, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RelationshipMessage"/> class with a partition key.
    /// </summary>
    /// <param name="relationship">The OMF Relationship (Link) definition.</param>
    /// <param name="messageAction">The <see cref="Abstractions.Messages.MessageAction"/> that describes the intent for this relationship.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the relationship to the OMFIngress Service.</param>
    public RelationshipMessage(Link relationship, MessageAction messageAction, PartitionKey? partitionKey)
    {
        Relationship = relationship;
        MessageAction = messageAction;
        PartitionKey = partitionKey;
    }

    public Link Relationship { get; }

    /// <summary>Gets the <see cref="Abstractions.Messages.MessageAction"/> describing the schema operation.</summary>
    public MessageAction MessageAction { get; }

    /// <summary>Gets the PartitionKey that will be sent with the relationship to the OMFIngress Service.</summary>
    public PartitionKey? PartitionKey { get; }
}
