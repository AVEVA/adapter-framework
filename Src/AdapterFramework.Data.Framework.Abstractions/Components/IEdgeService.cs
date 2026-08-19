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
using System.Threading.Channels;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Events;

namespace AdapterFramework.Data.Framework.Abstractions.Components;

/// <summary>
/// Defines methods that Edge Service component must implement.
/// </summary>
public interface IEdgeService : IEdgeComponent
{
    /// <summary>
    /// Asynchronously initializes <see cref="IEdgeService"/> service instance.
    /// </summary>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Asynchronously starts <see cref="IEdgeService"/>  service instance.
    /// </summary>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StartAsync();

    /// <summary>
    /// Asynchronously stops <see cref="IEdgeService"/>  service instance.
    /// </summary>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StopAsync();

    /// <summary>
    /// Signals the <see cref="IEdgeService"/> service instance to resend its health types, streams and static data.
    /// </summary>
    void ResendHealthMetadata();

    /// <summary>
    /// Gets the channel used for publishing and receiving <see cref="IEdgeEvent"/> instances.
    /// </summary>
    Channel<IEdgeEvent> EventChannel { get; }
}
