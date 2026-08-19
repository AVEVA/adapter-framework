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

namespace AdapterFramework.Data.Framework.Abstractions.Components;

public interface IFailoverServiceProvider : IEdgeComponent, IDisposable
{
    /// <summary>
    /// Asynchronously starts <see cref="IFailoverServiceProvider"/> component.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Asynchronously stops <see cref="IFailoverServiceProvider"/> component.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Signals the <see cref="IFailoverServiceProvider"/> component to resend its health types, streams and static data.
    /// </summary>
    void ResendHealthMetadata();
}
