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
namespace AdapterFramework.Data.Framework.Abstractions.Buffering;

/// <summary>
/// Single-consumer queue whose head can be reserved while it is processed, so that completing it
/// can never remove a different message.
/// </summary>
/// <typeparam name="TMessage">Specifies the type of elements in the queue.</typeparam>
public interface ILeasableMessageQueue<TMessage>
{
    /// <summary>
    /// Reserves the head message. An abandoned lease is offered again, with a new token, before any other message.
    /// </summary>
    /// <param name="lease">The lease, if a message was available.</param>
    /// <returns>true if a message was leased; otherwise, false.</returns>
    /// <exception cref="System.InvalidOperationException">A lease is already outstanding.</exception>
    bool TryLease(out MessageLease<TMessage> lease);

    /// <summary>
    /// Removes the leased message from the queue.
    /// </summary>
    /// <param name="lease">The outstanding lease.</param>
    /// <returns>true if the leased message was removed; false if the lease is stale or the message was already discarded by retention.</returns>
    bool Complete(MessageLease<TMessage> lease);

    /// <summary>
    /// Releases the lease, keeping the message at the head of the queue.
    /// </summary>
    /// <param name="lease">The outstanding lease.</param>
    void Abandon(MessageLease<TMessage> lease);
}
