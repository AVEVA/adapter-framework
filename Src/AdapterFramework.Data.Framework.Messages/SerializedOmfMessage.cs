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
using System.Collections.Concurrent;
using System.Text;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

/// <summary>
/// Represents a serialized omf message that implements the ISerializedOmfMessage interface.
/// </summary>
public class SerializedOmfMessage : Message, ISerializedOmfMessage
{
    public const int MessageTypeEnumSize = 1;
    public const int ValueCountSize = 4;
    public const int MessageActionEnumSize = 1;
    public const int OmfVersionEnumSize = 1;
    public const int PartitionKeyEnumSize = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerializedOmfMessage"/> class.
    /// </summary>
    /// <param name="type">OMF message type.</param>
    /// <param name="body">Message body in byte array.</param>
    /// <param name="messageAction">The <see cref="Abstractions.Messages.MessageAction"/> that will be sent with the message.</param>
    /// <param name="itemCount">The number of values contained inside the <see paramref="body"/>.</param>
    /// <param name="omfVersion">The OMF version that should be used to send this message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    public SerializedOmfMessage(MessageType type, byte[] body, MessageAction messageAction, int itemCount = 0, OmfVersion omfVersion = OmfVersion.Omf12, PartitionKey? partitionKey = null)
    {
        MessageType = type;
        MessageBody = body;
        ItemCount = itemCount;
        MessageAction = messageAction;
        OmfVersion = omfVersion;
        PartitionKey = partitionKey;
    }

    /// <inheritdoc/>
    public MessageType MessageType { get; }

    /// <inheritdoc/>
    public byte[] MessageBody { get; }

    /// <inheritdoc/>
    public int ItemCount { get; }

    /// <inheritdoc/>
    public MessageAction MessageAction { get; }

    /// <inheritdoc/>
    public OmfVersion OmfVersion { get; }

    /// <inheritdoc/>
    public PartitionKey? PartitionKey { get; }

    /// <inheritdoc/>
    public virtual int GetMessageSizeInBytes()
    {
        return MessageBody.Length + MessageTypeEnumSize + ValueCountSize + MessageActionEnumSize + OmfVersionEnumSize + PartitionKeyEnumSize;
    }
}
