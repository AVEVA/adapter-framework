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

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

/// <summary>
/// A framework-issued unit of work that groups OMF 2.0 writes and reports when they are accepted by every required endpoint.
/// </summary>
/// <remarks>
/// Waiting, timing out, or disposing a scope never cancels delivery. Scopes are in-process and don't survive restart or failover.
/// </remarks>
public interface IAwaitableScope : IAsyncDisposable
{
    /// <summary>
    /// Seals the scope after in-flight writes on other threads finish. Later writes throw <see cref="InvalidOperationException"/>.
    /// </summary>
    void Seal();

    /// <summary>
    /// Seals the scope if needed and waits until every required delivery is accepted, or the outcome can no longer change.
    /// </summary>
    /// <param name="timeout">Maximum time to wait. Uses <see cref="OmfAwaitableScopeOptions.DefaultWaitTimeout"/> when null.</param>
    /// <param name="cancellationToken">Cancels this wait without affecting delivery.</param>
    /// <returns>The acceptance result; <see cref="OmfAcceptanceOutcome.TimedOut"/> when the wait ends first. Waits can be repeated.</returns>
    /// <exception cref="OperationCanceledException">The token, scope disposal, or framework shutdown cancelled the wait.</exception>
    /// <exception cref="ObjectDisposedException">The scope was already disposed.</exception>
    Task<OmfAcceptanceResult> WaitForAcceptanceAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
