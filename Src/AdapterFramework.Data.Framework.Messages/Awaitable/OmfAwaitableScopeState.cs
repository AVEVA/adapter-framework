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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// The seal, wait, and disposal state of one awaitable scope. It is also the scope's <see cref="ScopeToken"/>.
/// </summary>
/// <remarks>
/// Framework infrastructure. Scopes that expose write methods wrap each write in <see cref="EnterWrite"/> and <see cref="ExitWrite"/>.
/// </remarks>
public sealed class OmfAwaitableScopeState : ScopeToken, IAwaitableScope
{
    private readonly OmfAwaitableCoordinator _coordinator;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _writeGate = new();
    private int _activeWrites;
    private bool _sealed;
    private bool _sealReported;
    private bool _disposed;

    internal OmfAwaitableScopeState(OmfAwaitableCoordinator coordinator, OmfAwaitableScopeOptions options)
    {
        _coordinator = coordinator;
        Options = options;
    }

    /// <summary>
    /// Gets the options the scope was created with.
    /// </summary>
    public OmfAwaitableScopeOptions Options { get; }

    /// <summary>
    /// Gets the scope state behind a token that an <see cref="OmfAwaitableCoordinator"/> issued.
    /// </summary>
    /// <param name="scope">The scope token.</param>
    /// <returns>The scope state.</returns>
    /// <exception cref="ArgumentException"><paramref name="scope"/> wasn't issued by a coordinator.</exception>
    public static OmfAwaitableScopeState FromToken(ScopeToken scope) =>
        scope as OmfAwaitableScopeState ?? throw new ArgumentException("The scope token wasn't issued by an awaitable coordinator.", nameof(scope));

    /// <summary>
    /// Marks the start of a write through the scope.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The scope is disposed.</exception>
    /// <exception cref="InvalidOperationException">The scope is sealed.</exception>
    public void EnterWrite()
    {
        lock (_writeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sealed)
            {
                throw new InvalidOperationException("The awaitable scope is sealed and can't accept more writes.");
            }

            _activeWrites++;
        }
    }

    /// <summary>
    /// Marks the end of a write that <see cref="EnterWrite"/> started.
    /// </summary>
    public void ExitWrite()
    {
        lock (_writeGate)
        {
            if (--_activeWrites == 0)
            {
                Monitor.PulseAll(_writeGate);
            }
        }
    }

    /// <inheritdoc/>
    public void Seal()
    {
        bool reportSeal;
        lock (_writeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _sealed = true;
            while (_activeWrites > 0)
            {
                Monitor.Wait(_writeGate);
            }

            reportSeal = !_sealReported;
            _sealReported = true;
        }

        if (reportSeal)
        {
            _coordinator.MarkSealed(this);
        }
    }

    /// <inheritdoc/>
    public async Task<OmfAcceptanceResult> WaitForAcceptanceAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? Options.DefaultWaitTimeout;
        if (effectiveTimeout < TimeSpan.Zero && effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), effectiveTimeout, "The timeout must be zero, positive, or infinite.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        Seal();

        var started = Stopwatch.GetTimestamp();
        if (!_completion.Task.IsCompleted)
        {
            var waitEnded = Task.WhenAny(_completion.Task, _disposedSignal.Task, _coordinator.ShutdownTask);
            try
            {
                await waitEnded.WaitAsync(effectiveTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A timed-out wait reports the current state; tracking continues.
            }
            catch (OperationCanceledException)
            {
                OmfAwaitableMetrics.WaitEnded(Stopwatch.GetElapsedTime(started).TotalSeconds, "Canceled");
                throw;
            }

            if (!_completion.Task.IsCompleted && (_disposedSignal.Task.IsCompleted || _coordinator.ShutdownTask.IsCompleted))
            {
                OmfAwaitableMetrics.WaitEnded(Stopwatch.GetElapsedTime(started).TotalSeconds, "Canceled");
                throw new OperationCanceledException("The awaitable scope was disposed or the framework is shutting down.");
            }
        }

        var result = _coordinator.GetResult(this);
        OmfAwaitableMetrics.WaitEnded(Stopwatch.GetElapsedTime(started).TotalSeconds, result.Outcome.ToString());
        return result;
    }

    /// <summary>
    /// Cancels outstanding waits and releases tracking. Delivery is never cancelled.
    /// </summary>
    /// <returns>A completed task.</returns>
    public ValueTask DisposeAsync()
    {
        lock (_writeGate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            _sealed = true;
        }

        _coordinator.Release(this);
        _disposedSignal.TrySetResult();
        return ValueTask.CompletedTask;
    }

    internal void SignalCompleted() => _completion.TrySetResult();
}
