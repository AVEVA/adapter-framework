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
    private readonly ILogger _logger;
    private readonly string _targetIdentifier;

    public FailoverPersistentOmfMessageQueue(string targetIdentifier, IPersistentQueue persistentQueue, ILogger logger)
        : base(targetIdentifier, persistentQueue, logger)
    {
        _logger = logger;
        _targetIdentifier = targetIdentifier;
    }

    #region Protected overrides

    /// <summary>
    /// Writes the V6 failover layout, which adds the partition key and OMF version to the V2 layout.
    /// </summary>
    /// <remarks>
    /// V6 uses a marker earlier releases don't recognize, so they skip these records as corrupt instead of misreading
    /// them with the V2 layout. Known rollback limitation, accepted by design: buffered bodies are lost, so the release
    /// notes require failover buffers to drain fully before rolling back to an earlier adapter version.
    /// </remarks>
    protected override DataItem CreateDataItem(IFailoverSerializedOmfMessage message)
    {
        if (message == null)
        {
            return new DataItem(DataItemVersion.V6, Array.Empty<byte>());
        }

        var dataBuffer = new byte[FailoverSerializedOmfMessage.GetRecordSizeInBytes(message.MessageBody.Length)];
        dataBuffer[0] = (byte)message.MessageType;
        BinaryPrimitives.WriteInt32LittleEndian(dataBuffer.AsSpan(MessageTypeEnumSize), message.ItemCount);

        Buffer.BlockCopy(message.MessageBody,
            0,
            dataBuffer,
            MessageTypeEnumSize + ValueCountSize,
            message.MessageBody.Length);

        BinaryPrimitives.WriteInt64LittleEndian(dataBuffer.AsSpan(MessageTypeEnumSize + ValueCountSize + message.MessageBody.Length), message.ProcessTimeTicks);

        dataBuffer[^3] = (byte)(message.PartitionKey ?? 0);
        dataBuffer[^2] = (byte)message.MessageAction;
        dataBuffer[^1] = (byte)message.OmfVersion;

        return new DataItem(DataItemVersion.V6, dataBuffer);
    }

    /// <summary>
    /// Reads V2 and V6 failover records, preserving timestamps.
    /// </summary>
    protected override IFailoverSerializedOmfMessage CreateSerializedOmfMessage(DataItem dataItem)
    {
        byte[] serializedWithType = dataItem?.Data;
        var hasOmfTrailer = dataItem?.Version == DataItemVersion.V6;
        var trailerLength = hasOmfTrailer
            ? PartitionKeyEnumSize + MessageActionEnumSize + OmfVersionEnumSize
            : MessageActionEnumSize;

        if (dataItem?.Version is not (DataItemVersion.V2 or DataItemVersion.V6)
            || serializedWithType == null || serializedWithType.Length < MessageTypeEnumSize + ValueCountSize + ProcessTimeTicksLength + trailerLength)
        {
            if (serializedWithType is { Length: > 0 })
            {
                _logger?.LogWarning("Dropped a failover buffer record with unsupported version {Version} or length {Length} in buffer for {TargetIdentifier}.",
                    dataItem.Version, serializedWithType.Length, _targetIdentifier);
            }

            return new FailoverSerializedOmfMessage(new MessageType(), Array.Empty<byte>(), new MessageAction());
        }

        var messageType = (MessageType)serializedWithType[0];
        var valueCount = BinaryPrimitives.ReadInt32LittleEndian(serializedWithType.AsSpan(MessageTypeEnumSize));

        var messageBody = new byte[serializedWithType.Length - MessageTypeEnumSize - ValueCountSize - ProcessTimeTicksLength - trailerLength];
        Buffer.BlockCopy(serializedWithType, MessageTypeEnumSize + ValueCountSize, messageBody, 0, messageBody.Length);

        var processTimeTicks = BinaryPrimitives.ReadInt64LittleEndian(serializedWithType.AsSpan(MessageTypeEnumSize + ValueCountSize + messageBody.Length));

        var messageAction = (MessageAction)serializedWithType[^1];
        var omfVersion = OmfVersion.Omf12;
        PartitionKey? partitionKey = null;
        if (hasOmfTrailer)
        {
            partitionKey = serializedWithType[^3] == 0 ? null : (PartitionKey)serializedWithType[^3];
            messageAction = (MessageAction)serializedWithType[^2];
            omfVersion = (OmfVersion)serializedWithType[^1];
        }

        var serializedOmfMessage = new FailoverSerializedOmfMessage(messageType, messageBody, messageAction, valueCount, omfVersion, partitionKey)
        {
            ProcessTimeTicks = processTimeTicks,
        };

        return serializedOmfMessage;
    }

    #endregion
}
