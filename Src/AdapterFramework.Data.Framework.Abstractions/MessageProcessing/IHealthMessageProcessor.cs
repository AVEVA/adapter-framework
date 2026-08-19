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
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing;

/// <summary>
/// Represents a type used to process Health messages.
/// </summary>
public interface IHealthMessageProcessor
{
    /// <summary>
    /// Property determining what stream metadata verbosity level is enabled - <see cref="MetadataInfo"/>.
    /// </summary>
    MetadataInfo StreamMetadataLevel { get; set; }

    /// <summary>
    /// Writes an array of <see cref="DataType"/> messages.
    /// </summary>
    /// <param name="dataTypes">An array of <see cref="DataType"/> instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteHealthTypes(DataType[] dataTypes, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes an array of <see cref="DataStream"/> messages.
    /// </summary>
    /// <param name="dataStreams">An array of <see cref="DataStream"/> instances.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteHealthStreams(DataStream[] dataStreams, MessageAction messageAction = MessageAction.Default);

    /// <summary>
    /// Writes a single instance of health value.
    /// </summary>
    /// <param name="id">The ID of the data message, which refers to type ID for static data messages and stream ID for dynamic data message.</param>
    /// <param name="classification">The classification enumeration of the data messages, either Static or Dynamic.</param>
    /// <param name="instance">The actual instance of the Health data message.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    void WriteHealthValue<T>(string id, Classification classification, T instance, MessageAction messageAction = MessageAction.Default);
}
