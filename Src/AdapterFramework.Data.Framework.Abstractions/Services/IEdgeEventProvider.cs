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
using System.Threading.Channels;
using AdapterFramework.Data.Framework.Abstractions.Events;

namespace AdapterFramework.Data.Framework.Abstractions.Services;

/// <summary>
/// Exposes event handler that allows to subscribe for OMF Egress HTTP events.
/// </summary>
public interface IEdgeEventProvider
{
    /// <summary>
    /// Gets the channel used to publish <see cref="IEdgeEvent"/> events
    /// related to edge operations such as egress and buffering.
    /// </summary>
    public Channel<IEdgeEvent> EdgeEventChannel { get; }
}
