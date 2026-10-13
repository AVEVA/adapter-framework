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
using System.Text;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;

namespace AdapterFramework.Data.Framework.Buffering;

public class PersistentOmfMessageQueue : PersistentOmfMessageQueueBase<ISerializedOmfMessage>
{
    public PersistentOmfMessageQueue(string targetIdentifier, IPersistentQueue persistentQueue, ILogger logger)
        : base(targetIdentifier, persistentQueue, logger)
    {
    }

    protected override DataItem CreateDataItem(ISerializedOmfMessage message)
    {
        if (message == null)
        {
            return new DataItem(DataItemVersion.V2, Array.Empty<byte>());
        }

        var dataBuffer = new byte[message.GetMessageSizeInBytes()];
        dataBuffer[0] = (byte)message.MessageType;

        BinaryPrimitives.WriteInt32LittleEndian(
            dataBuffer.AsSpan(SerializedOmfMessage.MessageTypeEnumSize),
            message.ItemCount);
        
        Buffer.BlockCopy(message.MessageBody, 0, dataBuffer,
            SerializedOmfMessage.MessageTypeEnumSize + SerializedOmfMessage.ValueCountSize,
            message.MessageBody.Length);

        // V4 appends the serialized message ID after the V3 trailer.
        var serializedMessageId = message.SerializedMessageId;
        var idLength = serializedMessageId.HasValue ? SerializedOmfMessage.SerializedMessageIdSize : 0;

        dataBuffer[^(3 + idLength)] = (byte)(message.PartitionKey ?? 0);
        dataBuffer[^(2 + idLength)] = (byte)message.MessageAction;
        dataBuffer[^(1 + idLength)] = (byte)message.OmfVersion;

        if (serializedMessageId is not { } id)
        {
            return new DataItem(DataItemVersion.V3, dataBuffer);
        }

        id.TryWriteBytes(dataBuffer.AsSpan(dataBuffer.Length - idLength));
        return new DataItem(DataItemVersion.V4, dataBuffer) { TrackingId = id };
    }

    protected override ISerializedOmfMessage CreateSerializedOmfMessage(DataItem dataItem)
    {
        if (dataItem?.Data == null || dataItem.Data.Length < SerializedOmfMessage.MessageTypeEnumSize + SerializedOmfMessage.ValueCountSize)
        {
            return new SerializedOmfMessage(new MessageType(), Array.Empty<byte>(), new MessageAction());
        }

        var data = dataItem.Data;
        var messageType = (MessageType)data[0];
        var valueCount = BinaryPrimitives.ReadInt32LittleEndian(
            data.AsSpan(SerializedOmfMessage.MessageTypeEnumSize));
                
        var bodyOffset = SerializedOmfMessage.MessageTypeEnumSize + SerializedOmfMessage.ValueCountSize;
        int bodyLength;
        PartitionKey? partitionKey = null;
        var messageAction = MessageAction.Default;
        var omfVersion = OmfVersion.Omf12;
        Guid? serializedMessageId = null;

        switch (dataItem.Version)
        {
            case DataItemVersion.V4:
            case DataItemVersion.V3:
            {
                var idLength = dataItem.Version == DataItemVersion.V4 ? SerializedOmfMessage.SerializedMessageIdSize : 0;
                if (data.Length < bodyOffset + 3 + idLength)
                    return new SerializedOmfMessage(messageType, Array.Empty<byte>(), messageAction);

                bodyLength = data.Length - bodyOffset - 3 - idLength;
                partitionKey = data[^(3 + idLength)] == 0 ? null : (PartitionKey)data[^(3 + idLength)];  // 0 indicates No PartitionKey.
                messageAction = (MessageAction)data[^(2 + idLength)];
                omfVersion = (OmfVersion)data[^(1 + idLength)];
                if (idLength > 0)
                {
                    serializedMessageId = new Guid(data.AsSpan(data.Length - idLength));
                }

                break;
            }

            case DataItemVersion.V2:
                if (data.Length < bodyOffset + 1)
                    return new SerializedOmfMessage(messageType, Array.Empty<byte>(), messageAction);

                bodyLength = data.Length - bodyOffset - 1;
                messageAction = (MessageAction)data[^1];
                break;

            case DataItemVersion.V1:
                bodyLength = data.Length - bodyOffset;
                break;

            default:
                return new SerializedOmfMessage(messageType, Array.Empty<byte>(), messageAction);
        }

        var messageBody = new byte[bodyLength];
        Buffer.BlockCopy(data, bodyOffset, messageBody, 0, bodyLength);

        return dataItem.Version is DataItemVersion.V3 or DataItemVersion.V4
            ? new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount, omfVersion, partitionKey) { SerializedMessageId = serializedMessageId }
            : new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount);
    }
}
