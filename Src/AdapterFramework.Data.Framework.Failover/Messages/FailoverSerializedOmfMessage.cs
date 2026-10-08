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
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Messages;

namespace AdapterFramework.Data.Framework.Failover.Messages;

public class FailoverSerializedOmfMessage : SerializedOmfMessage, IFailoverSerializedOmfMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FailoverSerializedOmfMessage"/> class.
    /// </summary>
    /// <param name="type">OMF message type.</param>
    /// <param name="body">Message body in byte array.</param>
    /// <param name="messageAction">The <see cref="Abstractions.Messages.MessageAction"/> that will be sent with the message.</param>
    /// <param name="valueCount">The number of values contained inside the <see paramref="body"/>.</param>
    /// <param name="omfVersion">The OMF version that should be used to send this message.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    public FailoverSerializedOmfMessage(MessageType type, byte[] body, MessageAction messageAction, int valueCount = 0, OmfVersion omfVersion = OmfVersion.Omf12, PartitionKey? partitionKey = null)
        : base(type, body, messageAction, valueCount, omfVersion, partitionKey)
    {
    }

    /// <inheritdoc/>
    public long ProcessTimeTicks { get; set; }

    public override int GetMessageSizeInBytes() =>
        base.GetMessageSizeInBytes() + BufferingConstants.ProcessTimeTicksLength;
}
