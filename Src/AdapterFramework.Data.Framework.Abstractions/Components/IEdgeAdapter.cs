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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.Failover;

namespace AdapterFramework.Data.Framework.Abstractions.Components;

/// <summary>
/// Defines methods that Edge Adapter component must implement.
/// </summary>
public interface IEdgeAdapter : IEdgeComponent, IDisposable
{
    /// <summary>
    /// Get the failover mode supported by the adapter.
    /// </summary>
    public FailoverMode SupportedFailoverModes { get; }

    /// <summary>
    /// Registers <see cref="IEdgeComponent"/> configuration and administration facets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the registration operation.</param>
    void Register(CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously initializes <see cref="IEdgeAdapter"/> component.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the initialize operation.</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously starts <see cref="IEdgeAdapter"/> component. 
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the start operation.</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously stops <see cref="IEdgeAdapter"/> component. 
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the stop operation.</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Unregisters <see cref="IEdgeAdapter"/> configuration and administration facets.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the unregister operation.</param>
    void Unregister(CancellationToken cancellationToken);

    /// <summary>
    /// Signals the <see cref="IEdgeAdapter"/> component to resend its health types, streams and static data.
    /// </summary>
    void ResendHealthMetadata();

    /// <summary>
    /// Signals the <see cref="IEdgeAdapter"/> component to resend its dynamic types, streams and static data.
    /// </summary>
    void ResendDynamicMetadata();

    /// <summary>
    /// This method is called by the adapter framework when the general configuration has been updated.
    /// </summary>
    /// <param name="oldConfig">Old configuration.</param>
    /// <param name="newConfig">New configuration.</param>
    /// <returns>A Task instance.</returns>
    Task ProcessGeneralConfigurationUpdateCallbackAsync(IAdapterGeneralConfiguration oldConfig, IAdapterGeneralConfiguration newConfig);

    /// <summary>
    /// Gets the channel used for publishing and receiving <see cref="IEdgeEvent"/> instances.
    /// This channel facilitates asynchronous communication of edge events between components.
    /// </summary>
    Channel<IEdgeEvent> EdgeEventChannel { get; }
}
