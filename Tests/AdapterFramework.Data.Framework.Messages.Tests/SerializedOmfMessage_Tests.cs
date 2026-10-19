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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using Xunit;

namespace AdapterFramework.Data.Framework.Messages.Tests;

public class SerializedOmfMessage_Tests
{
    /// <summary>
    /// Verifies old interface implementations need not implement process timestamps or scoped IDs.
    /// </summary>
    [Fact]
    public void SerializedOmfMessage_LegacyImplementation_DefaultsToZeroTicksAndNoId()
    {
        ISerializedOmfMessage message = new LegacyMessage();

        Assert.Equal(0, message.ProcessTimeTicks);
        Assert.Null(message.SerializedMessageId);
    }

    /// <summary>
    /// Verifies mutable timestamps are visible through the interface and add exactly eight bytes only when nonzero,
    /// independently of whether a body carries a serialized ID.
    /// </summary>
    [Theory]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    [InlineData(false, long.MaxValue)]
    [InlineData(true, long.MinValue)]
    public void SerializedOmfMessage_ProcessTimeTicks_SizeIncludesOnlyPresentFields(bool scoped, long ticks)
    {
        var message = new SerializedOmfMessage(MessageType.Instance, new byte[] { 0x11, 0x22 }, MessageAction.Create)
        {
            SerializedMessageId = scoped ? Guid.NewGuid() : null,
            ProcessTimeTicks = ticks,
        };
        ISerializedOmfMessage contract = message;

        Assert.Equal(ticks, contract.ProcessTimeTicks);
        Assert.Equal(10 + (scoped ? 16 : 0) + (ticks != 0 ? 8 : 0), message.GetMessageSizeInBytes());

        message.ProcessTimeTicks = 0;
        Assert.Equal(0, contract.ProcessTimeTicks);
        Assert.Equal(10 + (scoped ? 16 : 0), message.GetMessageSizeInBytes());
    }

    private sealed class LegacyMessage : ISerializedOmfMessage
    {
        public MessageType MessageType => MessageType.Instance;

        public byte[] MessageBody => Array.Empty<byte>();

        public int ItemCount => 0;

        public MessageAction MessageAction => MessageAction.Create;

        public OmfVersion OmfVersion => OmfVersion.Omf12;

        public PartitionKey? PartitionKey => null;

        /// <inheritdoc/>
        public int GetMessageSizeInBytes() => 8;
    }
}