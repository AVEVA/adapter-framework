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
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Abstractions.DataFlow;

/// <inheritdoc />
/// <summary>
/// Represents a generic client that sends OMF messages to an endpoint.
/// </summary>
public interface IClient : IDisposable
{
    /// <summary>
    /// Gets the Uri of the OMF ingress endpoint.
    /// </summary>
    Uri Uri { get; }

    /// <summary>
    /// Sends a message to the OMF endpoint.
    /// </summary>
    /// <param name="messageType">An enum that represents the type of the message.</param>
    /// <param name="messageBody">The byte representation of the OMF payload.</param>
    /// <param name="messageAction">The <see cref="MessageAction"/> that will be sent with the message.</param>
    /// <param name="omfVersion">The OMF version.</param>    
    /// <param name="token">Propagates notification that operations should be canceled.</param>
    /// <param name="partitionKey">The PartitionKey that will be sent with the message to the OMFIngress Service.</param>
    /// <returns>An EndpointResponse, which contains information about the status of the message sent.</returns>
    Task<EndpointResponse> SendMessageAsync(MessageType messageType, byte[] messageBody, MessageAction messageAction, OmfVersion omfVersion, CancellationToken token, PartitionKey? partitionKey = null);
}
