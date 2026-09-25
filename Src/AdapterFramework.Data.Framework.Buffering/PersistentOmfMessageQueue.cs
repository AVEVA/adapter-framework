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
    // V4 appends the streaming value, asset and event counts (Int64 each) after the V3 trailer.
    private const int ResourceCountsSize = 3 * sizeof(long);
    private const int V3TrailerSize = 3;

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

        var resourceCounts = message.ResourceCounts;
        var includeResourceCounts = !resourceCounts.IsEmpty;
        var v3Size = message.GetMessageSizeInBytes();

        var dataBuffer = new byte[includeResourceCounts ? v3Size + ResourceCountsSize : v3Size];
        dataBuffer[0] = (byte)message.MessageType;

        BinaryPrimitives.WriteInt32LittleEndian(
            dataBuffer.AsSpan(SerializedOmfMessage.MessageTypeEnumSize),
            message.ItemCount);
        
        Buffer.BlockCopy(message.MessageBody, 0, dataBuffer,
            SerializedOmfMessage.MessageTypeEnumSize + SerializedOmfMessage.ValueCountSize,
            message.MessageBody.Length);

        dataBuffer[v3Size - 3] = (byte)(message.PartitionKey ?? 0);
        dataBuffer[v3Size - 2] = (byte)message.MessageAction;
        dataBuffer[v3Size - 1] = (byte)message.OmfVersion;

        if (!includeResourceCounts)
        {
            return new DataItem(DataItemVersion.V3, dataBuffer);
        }

        var countsSpan = dataBuffer.AsSpan(v3Size);
        BinaryPrimitives.WriteInt64LittleEndian(countsSpan, resourceCounts.StreamingValues);
        BinaryPrimitives.WriteInt64LittleEndian(countsSpan[sizeof(long)..], resourceCounts.Assets);
        BinaryPrimitives.WriteInt64LittleEndian(countsSpan[(2 * sizeof(long))..], resourceCounts.Events);

        return new DataItem(DataItemVersion.V4, dataBuffer);
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
        OmfResourceCounts resourceCounts = default;

        switch (dataItem.Version)
        {
            case DataItemVersion.V4:
                if (data.Length < bodyOffset + V3TrailerSize + ResourceCountsSize)
                    return new SerializedOmfMessage(messageType, Array.Empty<byte>(), messageAction);

                var countsSpan = data.AsSpan(data.Length - ResourceCountsSize);
                resourceCounts = new OmfResourceCounts(
                    BinaryPrimitives.ReadInt64LittleEndian(countsSpan),
                    BinaryPrimitives.ReadInt64LittleEndian(countsSpan[sizeof(long)..]),
                    BinaryPrimitives.ReadInt64LittleEndian(countsSpan[(2 * sizeof(long))..]));

                var v3End = data.Length - ResourceCountsSize;
                bodyLength = v3End - bodyOffset - V3TrailerSize;
                partitionKey = data[v3End - 3] == 0 ? null : (PartitionKey)data[v3End - 3];  // 0 indicates No PartitionKey.
                messageAction = (MessageAction)data[v3End - 2];
                omfVersion = (OmfVersion)data[v3End - 1];
                break;

            case DataItemVersion.V3:
                
                if (data.Length < bodyOffset + V3TrailerSize)
                    return new SerializedOmfMessage(messageType, Array.Empty<byte>(), messageAction);

                bodyLength = data.Length - bodyOffset - V3TrailerSize;
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

        return dataItem.Version is DataItemVersion.V3 or DataItemVersion.V4
            ? new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount, omfVersion, partitionKey) { ResourceCounts = resourceCounts }
            : new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount);
    }
}
