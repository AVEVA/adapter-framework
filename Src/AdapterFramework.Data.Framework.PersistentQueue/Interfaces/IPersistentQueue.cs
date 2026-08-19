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
using AdapterFramework.Data.Framework.PersistentQueue.Queue;

namespace AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

/// <summary>
/// A queue which is persisted.
///
/// An IPersistentQueue can be used by an application to buffer data. If the application shuts down the queue and all of its items can be restored.
///
/// Design assumptions:
/// - Single process usage.
/// - Single reader / writer state - as opposed to multiple readers at different positions in the queue.
/// - All methods should be thread safe.
/// - Flush is performed by the client to allow for persistence guarantees and to allow for batching to improve performance.
/// - Items dequeued will be processed in-order. That is, there is no need for handling the scenario where one item is dequeued,
///     a second item is dequeued, we finish processing on the second item but not the first, and we want to flush the second item
///     but keep the first item persisted. Put more succinctly - we do not have to keep track of which threads have dequeued which
///     items. This reduces complexity allows for a simpler design. See DiskQueue on GitHub for inspiration on handling this scenario.
/// </summary>
public interface IPersistentQueue : IDisposable
{
    /// <summary>
    /// Updates the maximum number of files retained by the queue.
    /// </summary>
    /// <param name="maxQueueFiles">The maximum number of queue files to retain. A value of 0 means unlimited.</param>
    void UpdateMaxQueueFiles(int maxQueueFiles);

    /// <summary>
    /// Add data item to the queue.
    /// </summary>
    void Enqueue(DataItem dataItem);

    /// <summary>
    /// Retrieve data item from the queue.
    /// </summary>
    /// <returns>The data item at the beginning of the Queue.</returns>
    DataItem Dequeue();

    /// <summary>
    /// Returns the data item at the beginning of the Queue without removing it.
    /// </summary>
    /// <returns>The data item at the beginning of the Queue.</returns>
    DataItem Peek();

    /// <summary>
    /// Commit enqueues. This ensures items which were enqueued will be persisted. Consider calling FlushEnqueues after a batch of Enqueue() calls for best performance.
    /// </summary>
    void FlushEnqueues();

    /// <summary>
    /// Commit dequeues. Dequeued items are not removed from persistence until FlushDequeues is called. Call FlushDequeues once you are done processing the dequeued data.
    /// </summary>
    void FlushDequeues();

    /// <summary>
    /// Deletes created buffers.
    /// </summary>
    void DeleteBuffers();
}
