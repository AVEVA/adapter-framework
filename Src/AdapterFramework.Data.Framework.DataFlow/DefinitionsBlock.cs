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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Messages;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Base class for blocks that own type/stream or schema definitions and can therefore accept a
/// <see cref="FlushThenForwardMessage"/>. It owns the lifetime of every accepted envelope.
/// </summary>
public abstract class DefinitionsBlock : BaseBlock<Message>
{
    private static readonly Action<ILogger, Exception> LogDiscardedForward =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.DiscardedForward, nameof(LogDiscardedForward)),
            "The dataflow block stopped before forwarding an accepted batch; releasing its payload.");
    private static readonly Action<ILogger, Exception> LogReleaseForwardFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.ReleaseForwardFailed, nameof(LogReleaseForwardFailed)),
            "Releasing the payload of a discarded batch failed; continuing with the remaining batches.");

    // Envelopes are tracked by identity, independent of any Equals override on Message.
    private readonly ConcurrentDictionary<FlushThenForwardMessage, byte> _pendingForwards = new(ReferenceEqualityComparer.Instance);

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
            forward.Dispose();
        }
    }

    private void Log(Action<ILogger, Exception> log, Exception exception, string fallback)
    {
        try
        {
            if (Logger != null)
            {
                log(Logger, exception);
            }
            else
            {
                Trace.TraceError("{0} {1}", fallback, exception);
            }
        }
        catch (Exception loggingException)
        {
            Trace.TraceError("Logging a discarded batch failed. {0}", loggingException);
        }
    }
}
