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
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using static AdapterFramework.Data.Framework.Buffering.BufferingConstants;
using static AdapterFramework.Data.Framework.Messages.SerializedOmfMessage;

namespace AdapterFramework.Data.Framework.Failover.Messages;

public class FailoverPersistentOmfMessageQueue : PersistentOmfMessageQueueBase<IFailoverSerializedOmfMessage>
{
    private static readonly Action<ILogger, DataItemVersion, int, Exception> LogInvalidRecord =
        LoggerMessage.Define<DataItemVersion, int>(LogLevel.Warning, new EventId(5100, nameof(LogInvalidRecord)),
            "A failover buffer record could not be read. Version {RecordVersion}, byte length {ByteLength}.");
    private readonly ILogger _logger;

    public FailoverPersistentOmfMessageQueue(string targetIdentifier, IPersistentQueue persistentQueue, ILogger logger)
        : base(targetIdentifier, persistentQueue, logger)
    {
        _logger = logger;
    }

    #region Protected overrides

    protected override DataItem CreateDataItem(IFailoverSerializedOmfMessage message)
    {
        if (message == null)
        {
            return new DataItem(DataItemVersion.V2, Array.Empty<byte>());
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

        // Keep the legacy layout for messages that don't need the new metadata.
        if (message.OmfVersion == OmfVersion.Omf12 && !message.PartitionKey.HasValue)
        {
            dataBuffer[^1] = (byte)message.MessageAction;
            return new DataItem(DataItemVersion.V2, dataBuffer);
        }

        dataBuffer[^3] = (byte)(message.PartitionKey ?? 0);
        dataBuffer[^2] = (byte)message.MessageAction;
        dataBuffer[^1] = (byte)message.OmfVersion;
        return new DataItem(DataItemVersion.V3, dataBuffer);
    }

    protected override IFailoverSerializedOmfMessage CreateSerializedOmfMessage(DataItem dataItem)
    {
        var serializedWithType = dataItem?.Data;
        if (serializedWithType == null)
        {
            return new FailoverSerializedOmfMessage(new MessageType(), Array.Empty<byte>(), new MessageAction());
        }

        // Failover V3 includes process-time ticks before the trailer; regular-buffer V3 does not.
        // This queue does not write the regular buffer's V4 resource-count layout.
        var hasMetadata = dataItem.Version == DataItemVersion.V3;
        var metadataSize = hasMetadata ? OmfVersionEnumSize + PartitionKeyEnumSize : 0;
        var minimumLength = MessageTypeEnumSize + ValueCountSize + ProcessTimeTicksLength + MessageActionEnumSize + metadataSize;
        if (dataItem.Version is not (DataItemVersion.V2 or DataItemVersion.V3) || serializedWithType.Length < minimumLength)
        {
            if (_logger != null)
            {
                LogInvalidRecord(_logger, dataItem.Version, serializedWithType.Length, null);
            }

            return new FailoverSerializedOmfMessage(new MessageType(), Array.Empty<byte>(), new MessageAction());
        }

        var messageType = (MessageType)serializedWithType[0];
        var messageAction = (MessageAction)serializedWithType[hasMetadata ? ^2 : ^1];
        var omfVersion = hasMetadata ? (OmfVersion)serializedWithType[^1] : OmfVersion.Omf12;
        PartitionKey? partitionKey = hasMetadata && serializedWithType[^3] != 0 ? (PartitionKey)serializedWithType[^3] : null;
        var valueCount = BitConverter.ToInt32(serializedWithType, MessageTypeEnumSize);

        var messageBody = new byte[serializedWithType.Length - MessageTypeEnumSize - ValueCountSize - ProcessTimeTicksLength - MessageActionEnumSize - metadataSize];
        Buffer.BlockCopy(serializedWithType, MessageTypeEnumSize + ValueCountSize, messageBody, 0, messageBody.Length);

        var processTimeTicks = BitConverter.ToInt64(serializedWithType, MessageTypeEnumSize + ValueCountSize + messageBody.Length);

        var serializedOmfMessage = new FailoverSerializedOmfMessage(messageType, messageBody, messageAction, valueCount, omfVersion, partitionKey)
        {
            ProcessTimeTicks = processTimeTicks,
        };

        return serializedOmfMessage;
    }

    #endregion
}
