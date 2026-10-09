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
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

namespace AdapterFramework.Data.Framework.Buffering;

/// <summary>
/// Carries the serialized message IDs of persisted bodies that were lost before they were dequeued.
/// </summary>
public sealed class SerializedBodiesDiscardedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SerializedBodiesDiscardedEventArgs"/> class.
    /// </summary>
    /// <param name="serializedMessageIds">The serialized message IDs of the lost bodies.</param>
    /// <param name="reason">Why the bodies were lost.</param>
    public SerializedBodiesDiscardedEventArgs(IReadOnlyList<Guid> serializedMessageIds, OmfReasonCode reason)
    {
        SerializedMessageIds = serializedMessageIds;
        Reason = reason;
    }

    /// <summary>
    /// Gets the serialized message IDs of the lost bodies.
    /// </summary>
    public IReadOnlyList<Guid> SerializedMessageIds { get; }

    /// <summary>
    /// Gets why the bodies were lost: <see cref="OmfReasonCode.BufferFull"/>, <see cref="OmfReasonCode.CorruptRecord"/>, or <see cref="OmfReasonCode.BuffersReset"/>.
    /// </summary>
    public OmfReasonCode Reason { get; }
}
