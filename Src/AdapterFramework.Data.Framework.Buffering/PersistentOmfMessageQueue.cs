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

        dataBuffer[^3] = (byte)(message.PartitionKey ?? 0);
        dataBuffer[^2] = (byte)message.MessageAction;
        dataBuffer[^1] = (byte)message.OmfVersion;

        return new DataItem(DataItemVersion.V3, dataBuffer);
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

        switch (dataItem.Version)
        {
            case DataItemVersion.V3:
                
                if (data.Length < bodyOffset + 3)
                    return new SerializedOmfMessage(messageType, Array.Empty<byte>(), messageAction);

                bodyLength = data.Length - bodyOffset - 3;
                partitionKey = data[^3] == 0 ? null : (PartitionKey)data[^3];  // 0 indicates No PartitionKey.
                messageAction = (MessageAction)data[^2];
                omfVersion = (OmfVersion)data[^1];
                break;

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

        return dataItem.Version == DataItemVersion.V3
            ? new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount, omfVersion, partitionKey)
            : new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount);
    }
}
