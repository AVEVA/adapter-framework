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
using AdapterFramework.Data.Framework.Buffering.Messages;
using AdapterFramework.Data.Framework.Messages;
using Xunit;

namespace AdapterFramework.Data.Framework.Buffering.Tests.Messages;

public class TimestampedOmfMessage_Tests
{
    [Fact]
    public void TimestampedOmfMessage_Constructor_Test()
    {
        var serializedOmfMessage = new SerializedOmfMessage(MessageType.Data, new byte[] { 0x20 }, MessageAction.Default);
        var timestampedMessage = new TimestampedOmfMessage<ISerializedOmfMessage>(serializedOmfMessage);
        var now = DateTime.UtcNow;

        Assert.True(now >= timestampedMessage.Timestamp);
        Assert.True(now.AddSeconds(-1) < timestampedMessage.Timestamp);
        Assert.Equal(serializedOmfMessage.MessageType, timestampedMessage.SerializedOmfMessage.MessageType);
        Assert.Equal(serializedOmfMessage.MessageBody, timestampedMessage.SerializedOmfMessage.MessageBody);
    }
}
