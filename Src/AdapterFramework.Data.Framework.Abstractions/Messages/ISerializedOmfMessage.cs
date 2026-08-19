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
using System.Diagnostics.CodeAnalysis;
using AdapterFramework.Data.DataModel;

namespace AdapterFramework.Data.Framework.Abstractions.Messages;

/// <summary>
/// Defines the properties of a serialized OMF message.
/// </summary>
public interface ISerializedOmfMessage
{
    /// <summary>
    /// Gets the type of the serialized OMF message.
    /// </summary>
    MessageType MessageType { get; }

    /// <summary>Gets the body of the serialized OMF messages in the format of bytes.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Serialized payload is exposed as byte array for transport and compatibility.")]
    byte[] MessageBody { get; }

    /// <summary>
    /// Gets the count of items in the <see cref="MessageBody"/>.
    /// </summary>
    int ItemCount { get; }

    /// <summary>
    /// Gets the <see cref="Messages.MessageAction"/> value of the serialized OMF message.
    /// </summary>
    MessageAction MessageAction { get; }

    /// <summary>
    /// Gets the OMF version of the serialized OMF message.
    /// </summary>
    public OmfVersion OmfVersion { get; }

    /// <summary>
    /// Gets the PartitionKey of the serialized OMF message.
    /// </summary>
    PartitionKey? PartitionKey { get; }

    /// <summary>
    /// Get the size of the serialized OMF message in bytes. 
    /// </summary>
    /// <returns>The message size in bytes.</returns>
    int GetMessageSizeInBytes();
}
