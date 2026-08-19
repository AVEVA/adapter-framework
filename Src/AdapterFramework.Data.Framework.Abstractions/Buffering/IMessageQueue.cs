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
/// Represents first-in, first-out in-memory collection of <see typeparamref="TMessage"/> objects.
/// </summary>
/// <typeparam name="TMessage">Specifies the type of elements in the queue.</typeparam>
public interface IMessageQueue<TMessage>
{
    /// <summary>
    /// Adds an object to the end of the <see cref="IMessageQueue{TMessage}"/>.
    /// </summary>
    /// <param name="message">The object to add to the end of the <see cref="IMessageQueue{TMessage}"/>.</param>
    void Enqueue(TMessage message);

    /// <summary>
    /// Attempts to remove and return the object at the beginning of the <see cref="IMessageQueue{TMessage}"/>.
    /// </summary>
    /// <param name="message">When this method returns, if the operation was successful, <paramref name="message"/> contains the
    /// object removed. If no object was available to be removed, the value is unspecified.</param>
    /// <returns>true if an element was removed and returned from the beginning of the <see cref="IMessageQueue{TMessage}"/> successfully; otherwise, false.</returns>
    bool TryDequeue(out TMessage message);

    /// <summary>
    /// Attempts to return an object from the beginning of the <see cref="IMessageQueue{TMessage}"/>
    /// without removing it.
    /// </summary>
    /// <param name="message">When this method returns, <paramref name="message"/> contains an object from
    /// the beginning of the <see cref="IMessageQueue{TMessage}"/> or an unspecified value if the operation failed.</param>
    /// <returns>true if and object was returned successfully; otherwise, false.</returns>
    bool TryPeek(out TMessage message);
}
