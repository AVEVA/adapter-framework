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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Messages;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Base class for blocks that own type/stream or schema definitions and can therefore accept a
/// <see cref="FlushThenForwardMessage"/>. It owns the lifetime of every accepted envelope.
/// </summary>
/// <remarks>
/// <para>
/// Ordering: a producer (<see cref="DataGroupingBlock"/> or <see cref="InstanceGroupingBlock"/>) hands each batch to this block
/// in a <see cref="FlushThenForwardMessage"/>. This block flushes the definitions it has buffered and then forwards the batch
/// from its own worker, so data is never sent before its definitions. Ordering applies to definitions written to this block
/// before the data that uses them is posted to the producer (same thread, or otherwise synchronized).
/// </para>
/// <para>
/// Ownership: posting an envelope transfers its lifetime to this block, including cleanup on admission rejection, worker
/// failure or cancellation. The producer's terminal callback runs on this block's worker and must accept ownership and return,
/// or throw without retaining the payload; use <see cref="BaseBlockExtensions.PostOrThrow"/> to check downstream admission.
/// A failed forward is not retried; the payload is released and the failure is logged (at most once a minute while it persists).
/// </para>
/// <para>
/// Fail-stop: if this block rejects a batch (for example because it faulted), the producer faults too and stops all data,
/// because data must never be forwarded without its definitions. Rejected messages are reported by <see cref="BaseBlock{T}.Post"/>.
/// The pipeline must be recreated to recover. Dispose producers before this block and the blocks downstream of it.
/// </para>
/// </remarks>
public abstract class DefinitionsBlock : BaseBlock<Message>
{
    private static readonly Action<ILogger, Exception> LogDiscardedForward =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.DiscardedForward, nameof(LogDiscardedForward)),
            "The dataflow block stopped before forwarding an accepted batch; releasing its payload.");
    private static readonly Action<ILogger, Exception> LogReleaseForwardFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.ReleaseForwardFailed, nameof(LogReleaseForwardFailed)),
            "Releasing the payload of a discarded batch failed; continuing with the remaining batches.");
    private static readonly Action<ILogger, long, Exception> LogForwardFailed =
        LoggerMessage.Define<long>(LogLevel.Error, new EventId(DataflowEventIds.ForwardFailed, nameof(LogForwardFailed)),
            "Batch forwarding failed {FailedCount} time(s) since the last report; the untransferred payloads were released and will not be retried.");

    // Envelopes are tracked by identity, independent of any Equals override on Message.
    private readonly ConcurrentDictionary<FlushThenForwardMessage, byte> _pendingForwards = new(ReferenceEqualityComparer.Instance);
    private readonly RateLimitedCounter _forwardFailures = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionsBlock"/> class.
    /// </summary>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="capacity">The max number of messages that may be buffered by the block.</param>
    /// <param name="asyncFlush">Indicate whether the action to invoke with each data element received should be done asynchronously.</param>
    /// <param name="singleProducer">Indicate whether code using the dataFlow block is constrained to one producer at a time.</param>
    /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
    private protected DefinitionsBlock(ILogger logger, int capacity, bool asyncFlush, bool singleProducer, CancellationToken token)
        : base(logger, capacity, asyncFlush, singleProducer, token)
    {
    }

    /// <inheritdoc cref="BaseBlock{T}.Post"/>
    /// <remarks>
    /// Posting a <see cref="FlushThenForwardMessage"/> transfers its lifetime to this block, including cleanup on
    /// admission rejection, worker failure, or cancellation. Other message types retain their existing ownership rules.
    /// </remarks>
    public override bool Post(Message message)
    {
        if (message is not FlushThenForwardMessage forward)
        {
            return PostCore(message);
        }

        TrackForward(forward);
        var accepted = false;
        try
        {
            accepted = PostCore(message);
            if (accepted)
            {
                forward.MarkAccepted();
            }

            return accepted;
        }
        finally
        {
            if (!accepted)
            {
                ReleaseForward(forward);
            }
        }
    }

    /// <inheritdoc cref="BaseBlock{T}.PostAsync"/>
    /// <remarks>Flush-then-forward envelopes have the same ownership rules as <see cref="Post"/>.</remarks>
    public override Task<bool> PostAsync(Message message)
    {
        return message is FlushThenForwardMessage forward
            ? PostForwardAsync(forward)
            : SendCoreAsync(message);
    }

    /// <inheritdoc/>
    private protected override void OnHandled(Message message)
    {
        if (message is FlushThenForwardMessage forward)
        {
            ReleaseForward(forward);
        }
    }

    /// <summary>
    /// Delivers the payload of <paramref name="forward"/>. Call from the worker after the buffered definitions have been flushed.
    /// </summary>
    /// <param name="forward">The envelope to deliver.</param>
    /// <remarks>
    /// Only the payload callback is isolated; a definitions flush that throws before this call still faults the block.
    /// </remarks>
    private protected void Forward(FlushThenForwardMessage forward)
    {
        try
        {
            forward.Deliver();
        }
        catch (Exception exception)
        {
            // Downstream may be permanently stopped, so report at most once a minute instead of once per batch.
            if (_forwardFailures.Increment(out var failedCount))
            {
                DataflowLog.Write(Logger, LogForwardFailed, failedCount, exception, "Batch forwarding failed; the untransferred payload was released and will not be retried.");
            }
        }
    }

    /// <inheritdoc/>
    private protected override void OnWorkerStopped()
    {
        foreach (var forward in _pendingForwards.Keys)
        {
            if (_pendingForwards.TryRemove(forward, out _))
            {
                Log(LogDiscardedForward, null, "The dataflow block stopped before forwarding an accepted batch; releasing its payload.");
                try
                {
                    forward.Dispose();
                }
                catch (Exception exception)
                {
                    // Report and continue so one failing payload does not leave the remaining batches unreleased.
                    Log(LogReleaseForwardFailed, exception, "Releasing the payload of a discarded batch failed; continuing with the remaining batches.");
                }
            }
        }
    }

    private async Task<bool> PostForwardAsync(FlushThenForwardMessage forward)
    {
        TrackForward(forward);
        var accepted = false;
        try
        {
            accepted = await SendCoreAsync(forward).ConfigureAwait(false);
            if (accepted)
            {
                forward.MarkAccepted();
            }

            return accepted;
        }
        finally
        {
            if (!accepted)
            {
                ReleaseForward(forward);
            }
        }
    }

    private void TrackForward(FlushThenForwardMessage forward)
    {
        if (!_pendingForwards.TryAdd(forward, 0))
        {
            throw new InvalidOperationException("The forwarding envelope is already queued.");
        }
    }

    private void ReleaseForward(FlushThenForwardMessage forward)
    {
        if (_pendingForwards.TryRemove(forward, out _))
        {
            try
            {
                forward.Dispose();
            }
            catch (Exception exception)
            {
                // Report rather than throw so a cleanup failure cannot mask a definitions failure being propagated.
                Log(LogReleaseForwardFailed, exception, "Releasing the payload of a discarded batch failed; continuing with the remaining batches.");
            }
        }
    }

    private void Log(Action<ILogger, Exception> log, Exception exception, string fallback) =>
        DataflowLog.Write(Logger, log, exception, fallback);
}
