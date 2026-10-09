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
namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

/// <summary>
/// Describes why written items with a <see cref="DataItem.TrackingId"/> were lost before they were dequeued.
/// </summary>
public enum TrackedItemLossReason
{
    /// <summary>
    /// The reader skipped the item because its record could not be read.
    /// </summary>
    Unreadable = 0,

    /// <summary>
    /// The file holding the item was deleted to stay within the file limit or to free disk space.
    /// </summary>
    Evicted = 1,

    /// <summary>
    /// The buffers were deleted.
    /// </summary>
    Cleared = 2,
}
