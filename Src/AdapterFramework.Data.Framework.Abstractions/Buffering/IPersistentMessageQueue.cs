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

namespace AdapterFramework.Data.Framework.Abstractions.Buffering;

/// <summary>
/// Represents first-in, first-out persistent collection of <see typeparamref="TMessage"/> objects.
/// </summary>
/// <typeparam name="TMessage">Specifies the type of elements in the queue.</typeparam>
public interface IPersistentMessageQueue<TMessage> : IMessageQueue<TMessage>, IDisposable
{
    /// <summary>
    /// Gets a value that changes whenever retention discards unread messages from the head of the queue.
    /// </summary>
    /// <remarks>
    /// The default implementation always returns 0, so <see cref="TryDequeueIfEpoch"/> provides no fencing.
    /// Implementations that can discard unread messages should override this to keep leases safe.
    /// </remarks>
    long ReaderEpoch => 0;

    /// <summary>
    /// Removes all persisted messages from the <see cref="IPersistentMessageQueue{TMessage}"/>.
    /// </summary>
    void Clear();

    /// <summary>
    /// Removes the head message only if <see cref="ReaderEpoch"/> still equals <paramref name="expectedEpoch"/>.
    /// </summary>
    /// <param name="expectedEpoch">The <see cref="ReaderEpoch"/> observed before the head message was read.</param>
    /// <returns>true if the head message was removed; otherwise, false.</returns>
    bool TryDequeueIfEpoch(long expectedEpoch) => ReaderEpoch == expectedEpoch && TryDequeue(out _);
}
