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

    // V3: type, count, body, ticks, partition key, action, OMF version. Legacy V2 ends with ticks, action.
    protected override DataItem CreateDataItem(IFailoverSerializedOmfMessage message)
    {
        if (message == null)
        {
            return new DataItem(DataItemVersion.V3, Array.Empty<byte>());
        }

        var dataBuffer = new byte[message.GetMessageSizeInBytes()];
        dataBuffer[0] = (byte)message.MessageType;
        var valueCount = BitConverter.GetBytes(message.ItemCount);
        Buffer.BlockCopy(valueCount,
            0,
            dataBuffer,
            MessageTypeEnumSize,
            ValueCountSize);

        Buffer.BlockCopy(message.MessageBody,
            0,
            dataBuffer,
            MessageTypeEnumSize + ValueCountSize,
            message.MessageBody.Length);

        var procesTimeTicksBytes = BitConverter.GetBytes(message.ProcessTimeTicks);
        Buffer.BlockCopy(procesTimeTicksBytes,
            0,
            dataBuffer,
            MessageTypeEnumSize + ValueCountSize + message.MessageBody.Length,
            ProcessTimeTicksLength);

        dataBuffer[^3] = (byte)(message.PartitionKey ?? 0);
        dataBuffer[^2] = (byte)message.MessageAction;
        dataBuffer[^1] = (byte)message.OmfVersion;

        return new DataItem(DataItemVersion.V3, dataBuffer);
    }

    protected override IFailoverSerializedOmfMessage CreateSerializedOmfMessage(DataItem dataItem)
    {
        byte[] serializedWithType = dataItem?.Data;
        var trailerLength = dataItem?.Version == DataItemVersion.V3
            ? PartitionKeyEnumSize + MessageActionEnumSize + OmfVersionEnumSize
            : MessageActionEnumSize;

        if (serializedWithType == null || serializedWithType.Length < MessageTypeEnumSize + ValueCountSize + ProcessTimeTicksLength + trailerLength)
        {
            return new FailoverSerializedOmfMessage(new MessageType(), Array.Empty<byte>(), new MessageAction());
        }

        var messageType = (MessageType)serializedWithType[0];
        var valueCount = BitConverter.ToInt32(serializedWithType, MessageTypeEnumSize);

        var messageBody = new byte[serializedWithType.Length - MessageTypeEnumSize - ValueCountSize - ProcessTimeTicksLength - trailerLength];
        Buffer.BlockCopy(serializedWithType, MessageTypeEnumSize + ValueCountSize, messageBody, 0, messageBody.Length);

        var processTimeTicks = BitConverter.ToInt64(serializedWithType, MessageTypeEnumSize + ValueCountSize + messageBody.Length);

        var messageAction = (MessageAction)serializedWithType[^1];
        var omfVersion = OmfVersion.Omf12;
        PartitionKey? partitionKey = null;
        if (dataItem.Version == DataItemVersion.V3)
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
