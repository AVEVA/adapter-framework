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
using System.Collections.Generic;

namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

/// <summary>
/// Carries the tracking IDs of written items that were lost before they were dequeued.
/// </summary>
public sealed class TrackedItemsLostEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrackedItemsLostEventArgs"/> class.
    /// </summary>
    /// <param name="trackingIds">The tracking IDs of the lost items.</param>
    /// <param name="reason">Why the items were lost.</param>
    public TrackedItemsLostEventArgs(IReadOnlyList<Guid> trackingIds, TrackedItemLossReason reason)
    {
        TrackingIds = trackingIds;
        Reason = reason;
    }

    /// <summary>
    /// Gets the tracking IDs of the lost items.
    /// </summary>
    public IReadOnlyList<Guid> TrackingIds { get; }

    /// <summary>
    /// Gets why the items were lost.
    /// </summary>
    public TrackedItemLossReason Reason { get; }
}
