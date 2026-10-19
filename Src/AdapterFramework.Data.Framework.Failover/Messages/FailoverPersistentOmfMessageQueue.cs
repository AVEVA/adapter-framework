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
using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using static AdapterFramework.Data.Framework.Buffering.BufferingConstants;
using static AdapterFramework.Data.Framework.Messages.SerializedOmfMessage;

namespace AdapterFramework.Data.Framework.Failover.Messages;

public class FailoverPersistentOmfMessageQueue : PersistentOmfMessageQueueBase<IFailoverSerializedOmfMessage>
{
    public FailoverPersistentOmfMessageQueue(string targetIdentifier, IPersistentQueue persistentQueue, ILogger logger)
        : base(targetIdentifier, persistentQueue, logger)
    {
    }

    #region Protected overrides

    /// <summary>
    /// Writes the legacy V3 failover layout, appending an ID as V4 only for scoped bodies.
    /// </summary>
    protected override DataItem CreateDataItem(IFailoverSerializedOmfMessage message)
    {
        if (message == null)
        {
            return new DataItem(DataItemVersion.V3, Array.Empty<byte>());
        }

        var serializedMessageId = message.SerializedMessageId;
        var idLength = serializedMessageId.HasValue ? SerializedMessageIdSize : 0;
        var dataBuffer = new byte[MessageTypeEnumSize + ValueCountSize + message.MessageBody.Length + ProcessTimeTicksLength
            + PartitionKeyEnumSize + MessageActionEnumSize + OmfVersionEnumSize + idLength];
        dataBuffer[0] = (byte)message.MessageType;
        BinaryPrimitives.WriteInt32LittleEndian(dataBuffer.AsSpan(MessageTypeEnumSize), message.ItemCount);

        Buffer.BlockCopy(message.MessageBody,
            0,
            dataBuffer,
            MessageTypeEnumSize + ValueCountSize,
            message.MessageBody.Length);

        BinaryPrimitives.WriteInt64LittleEndian(dataBuffer.AsSpan(MessageTypeEnumSize + ValueCountSize + message.MessageBody.Length), message.ProcessTimeTicks);

        dataBuffer[^(3 + idLength)] = (byte)(message.PartitionKey ?? 0);
        dataBuffer[^(2 + idLength)] = (byte)message.MessageAction;
        dataBuffer[^(1 + idLength)] = (byte)message.OmfVersion;

        if (serializedMessageId is { } id)
        {
            id.TryWriteBytes(dataBuffer.AsSpan(dataBuffer.Length - idLength));
        }

        return new DataItem(serializedMessageId.HasValue ? DataItemVersion.V4 : DataItemVersion.V3, dataBuffer)
        {
            TrackingId = serializedMessageId,
        };
    }

    /// <summary>
    /// Reads legacy V2/V3 failover records and scoped V4 records, preserving timestamps and IDs.
    /// </summary>
    protected override IFailoverSerializedOmfMessage CreateSerializedOmfMessage(DataItem dataItem)
    {
        byte[] serializedWithType = dataItem?.Data;
        var hasOmfTrailer = dataItem?.Version is DataItemVersion.V3 or DataItemVersion.V4;
        var idLength = dataItem?.Version == DataItemVersion.V4 ? SerializedMessageIdSize : 0;
        var trailerLength = (hasOmfTrailer
            ? PartitionKeyEnumSize + MessageActionEnumSize + OmfVersionEnumSize
            : MessageActionEnumSize) + idLength;

        if (dataItem?.Version is not (DataItemVersion.V2 or DataItemVersion.V3 or DataItemVersion.V4)
            || serializedWithType == null || serializedWithType.Length < MessageTypeEnumSize + ValueCountSize + ProcessTimeTicksLength + trailerLength)
        {
            return new FailoverSerializedOmfMessage(new MessageType(), Array.Empty<byte>(), new MessageAction());
        }

        var messageType = (MessageType)serializedWithType[0];
        var valueCount = BinaryPrimitives.ReadInt32LittleEndian(serializedWithType.AsSpan(MessageTypeEnumSize));

        var messageBody = new byte[serializedWithType.Length - MessageTypeEnumSize - ValueCountSize - ProcessTimeTicksLength - trailerLength];
        Buffer.BlockCopy(serializedWithType, MessageTypeEnumSize + ValueCountSize, messageBody, 0, messageBody.Length);

        var processTimeTicks = BinaryPrimitives.ReadInt64LittleEndian(serializedWithType.AsSpan(MessageTypeEnumSize + ValueCountSize + messageBody.Length));

        var messageAction = (MessageAction)serializedWithType[^(1 + idLength)];
        var omfVersion = OmfVersion.Omf12;
        PartitionKey? partitionKey = null;
        if (hasOmfTrailer)
        {
            partitionKey = serializedWithType[^(3 + idLength)] == 0 ? null : (PartitionKey)serializedWithType[^(3 + idLength)];
            messageAction = (MessageAction)serializedWithType[^(2 + idLength)];
            omfVersion = (OmfVersion)serializedWithType[^(1 + idLength)];
        }

        var serializedOmfMessage = new FailoverSerializedOmfMessage(messageType, messageBody, messageAction, valueCount, omfVersion, partitionKey)
        {
            ProcessTimeTicks = processTimeTicks,
            SerializedMessageId = idLength > 0 ? new Guid(serializedWithType.AsSpan(serializedWithType.Length - idLength)) : null,
        };

        return serializedOmfMessage;
    }

    #endregion
}
