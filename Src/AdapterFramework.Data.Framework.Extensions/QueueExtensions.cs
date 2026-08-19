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
using System.Collections.Generic;

namespace AdapterFramework.Data.Framework.Extensions;

public static class QueueExtensions
{
    /// <summary>
    /// Attempts to remove and return the object at the beginning of the <see cref="Queue{T}"/>.
    /// </summary>
    /// <param name="queue"><see cref="Queue{T}"/> from which an object should be removed.</param>
    /// <param name="result"> When this method returns, if the operation was successful, <paramref name="result"/> contains the
    /// object removed. If no object was available to be removed, the value is unspecified.
    /// </param>
    /// <returns>True if an element was removed and returned from the beginning of the <see cref="Queue{T}"/> successfully; otherwise, false.</returns>
    public static bool TryDequeue<T>(this Queue<T> queue, out T result)
    {
        ThrowHelper.ThrowIfArgumentNull(queue, nameof(queue));

        if (queue.Count == 0)
        {
            result = default;
            return false;
        }

        result = queue.Dequeue();
        return true;
    }

    /// <summary>
    /// Attempts to return an object from the beginning of the <see cref="Queue{T}"/> without removing it.
    /// </summary>
    /// <param name="queue"><see cref="Queue{T}"/> from which an object should be removed.</param>
    /// <param name="result">When this method returns, <paramref name="result"/> contains an object from
    /// the beginning of the <see cref="Queue{T}"/> or an unspecified value if the operation failed.</param>
    /// <returns>true if and object was returned successfully; otherwise, false.</returns>
    public static bool TryPeek<T>(this Queue<T> queue, out T result)
    {
        ThrowHelper.ThrowIfArgumentNull(queue, nameof(queue));

        if (queue.Count == 0)
        {
            result = default;
            return false;
        }

        result = queue.Peek();
        return true;
    }
}
