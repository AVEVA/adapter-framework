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
    // V4 appends the stream value, asset and event counts (Int64 each) after the V3 trailer; V5 also appends the serialized message ID.
    private const int ResourceCountsSize = 3 * sizeof(long);
    private const int V3TrailerSize = 3;

    public PersistentOmfMessageQueue(string targetIdentifier, IPersistentQueue persistentQueue, ILogger logger)
        : base(targetIdentifier, persistentQueue, logger)
    {
    }

    /// <summary>
    /// Creates a V3 record, a V4 record that appends resource counts, or a V5 record that appends resource counts and the
    /// serialized message ID of a scoped body.
    /// </summary>
    /// <remarks>
    /// Known rollback limitation, accepted by design: earlier releases don't recognize V5 records and skip them as corrupt,
    /// so scoped bodies still buffered are lost. The release notes require buffers to drain fully before rolling back to an
    /// earlier adapter version.
    /// </remarks>
    protected override DataItem CreateDataItem(ISerializedOmfMessage message)
    {
        if (message == null)
        {
            return new DataItem(DataItemVersion.V2, Array.Empty<byte>());
        }

        var serializedMessageId = message.SerializedMessageId;
        var resourceCounts = message.ResourceCounts;
        var version = serializedMessageId.HasValue ? DataItemVersion.V5
            : resourceCounts.IsEmpty ? DataItemVersion.V3
            : DataItemVersion.V4;
        var v3Size = SerializedOmfMessage.MessageTypeEnumSize + SerializedOmfMessage.ValueCountSize + message.MessageBody.Length + V3TrailerSize;

        var dataBuffer = new byte[version switch
        {
            DataItemVersion.V5 => v3Size + ResourceCountsSize + SerializedOmfMessage.SerializedMessageIdSize,
            DataItemVersion.V4 => v3Size + ResourceCountsSize,
            _ => v3Size,
        }];
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

        if (version != DataItemVersion.V3)
        {
            var countsSpan = dataBuffer.AsSpan(v3Size, ResourceCountsSize);
            BinaryPrimitives.WriteInt64LittleEndian(countsSpan, resourceCounts.StreamValues);
            BinaryPrimitives.WriteInt64LittleEndian(countsSpan[sizeof(long)..], resourceCounts.Assets);
            BinaryPrimitives.WriteInt64LittleEndian(countsSpan[(2 * sizeof(long))..], resourceCounts.Events);
        }

        if (serializedMessageId is { } id)
        {
            id.TryWriteBytes(dataBuffer.AsSpan(v3Size + ResourceCountsSize));
        }

        return new DataItem(version, dataBuffer)
        {
            TrackingId = serializedMessageId,
        };
    }

    /// <summary>
    /// Reads V1 through V5 writer records.
    /// </summary>
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
        OmfResourceCounts resourceCounts = default;

        switch (dataItem.Version)
        {
            case DataItemVersion.V5:
            case DataItemVersion.V4:
            case DataItemVersion.V3:
            {
                // V3 records have no resource counts, so messages buffered before the upgrade still count toward IORate
                // when delivered, but not toward StreamIORate, AssetIORate or EventIORate.
                var countsLength = dataItem.Version == DataItemVersion.V3 ? 0 : ResourceCountsSize;
                var idLength = dataItem.Version == DataItemVersion.V5 ? SerializedOmfMessage.SerializedMessageIdSize : 0;
                if (data.Length < bodyOffset + V3TrailerSize + countsLength + idLength)
                    return new SerializedOmfMessage(messageType, Array.Empty<byte>(), messageAction);

                var v3End = data.Length - countsLength - idLength;
                if (countsLength > 0)
                {
                    var countsSpan = data.AsSpan(v3End, ResourceCountsSize);
                    resourceCounts = new OmfResourceCounts(
                        BinaryPrimitives.ReadInt64LittleEndian(countsSpan),
                        BinaryPrimitives.ReadInt64LittleEndian(countsSpan[sizeof(long)..]),
                        BinaryPrimitives.ReadInt64LittleEndian(countsSpan[(2 * sizeof(long))..]));
                }

                if (idLength > 0)
                {
                    serializedMessageId = new Guid(data.AsSpan(data.Length - idLength));
                }

                bodyLength = v3End - bodyOffset - V3TrailerSize;
                partitionKey = data[v3End - 3] == 0 ? null : (PartitionKey)data[v3End - 3];  // 0 indicates No PartitionKey.
                messageAction = (MessageAction)data[v3End - 2];
                omfVersion = (OmfVersion)data[v3End - 1];
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

        return dataItem.Version is DataItemVersion.V3 or DataItemVersion.V4 or DataItemVersion.V5
            ? new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount, omfVersion, partitionKey) { ResourceCounts = resourceCounts, SerializedMessageId = serializedMessageId }
            : new SerializedOmfMessage(messageType, messageBody, messageAction, valueCount);
    }
}
