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

namespace AdapterFramework.Data.Framework.Abstractions.Events;

    /// <summary>
    /// Represents an event that occurs within the edge system, such as egress or buffering.
    /// </summary>
    public interface IEdgeEvent
{
    /// <summary>
    /// Gets the type of edge event, indicating the source of the event (e.g., egress or buffering).
    /// </summary>
    EdgeEventType EventType { get; }

    /// <summary>
    /// Gets the UTC timestamp when the event occurred.
    /// </summary>
    DateTime Timestamp { get; init; }
}
