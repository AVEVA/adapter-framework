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
using System.Threading.Tasks;

namespace AdapterFramework.Data.Framework.Abstractions.Diagnostics;

/// <summary>
/// Represents a type used to periodically collect process diagnostics information.
/// </summary>
public interface IEdgeDiagnosticsService : IDisposable
{
    /// <summary>
    /// Asynchronously initializes <see cref="IEdgeDiagnosticsService"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the initialize operation.</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously starts <see cref="IEdgeDiagnosticsService"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the start operation.</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously stops <see cref="IEdgeDiagnosticsService"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation cancellationToken to cancel the stop operation</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends again types, streams and links created by the service.
    /// </summary>
    void ResendTypesAndStreams();

    /// <summary>
    /// Collects or returns cached process diagnostics data in form of <see cref="EdgeDiagnosticsEvent"/> object.
    /// </summary>
    /// <returns>Current or cached diagnostics data.</returns>
    EdgeDiagnosticsEvent Collect();
}
