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
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Helpers for flush-then-forward ordering between producer blocks and definitions blocks.
/// </summary>
public static class BaseBlockExtensions
{
    private static readonly Action<ILogger, Exception> LogForwardFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.ForwardFailed, nameof(LogForwardFailed)),
            "Batch forwarding failed; the untransferred payload was released and will not be retried.");
    private static readonly Action<ILogger, Exception> LogMisroutedEnvelope =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.MisroutedEnvelope, nameof(LogMisroutedEnvelope)),
            "A flush-then-forward envelope was posted to a block that does not own definitions; its payload was released.");

    /// <summary>
    /// Posts <paramref name="message"/> to <paramref name="block"/>, throwing if admission is rejected.
    /// A rejected flush-then-forward envelope is released; its payload is never forwarded out of order.
    /// </summary>
    /// <param name="block">The block that owns the definitions.</param>
    /// <param name="message">The message to post.</param>
    /// <exception cref="InvalidOperationException">The block no longer accepts messages.</exception>
    /// <remarks>Dispose producers before definitions and downstream blocks. Rejection is not a successful drain. The caller is responsible for logging the exception.</remarks>
    public static void PostOrThrow(this BaseBlock<Message> block, Message message)
    {
        ThrowHelper.ThrowIfArgumentNull(block, nameof(block));
        ThrowHelper.ThrowIfArgumentNull(message, nameof(message));

        if (!block.Post(message))
        {
            throw new InvalidOperationException("The dataflow block no longer accepts messages.");
        }
    }

    internal static void ForwardSafely(this FlushThenForwardMessage message, ILogger logger)
    {
        try
        {
            message.Deliver();
        }
        catch (Exception exception)
        {
            // Only the payload callback is isolated; definition-flush failures still fault the block.
            Log(logger, LogForwardFailed, exception, "Batch forwarding failed; the untransferred payload was released and will not be retried.");
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "The definitions callback takes ownership on success; on failure the envelope is disposed here.")]
    internal static void PostForOrderedFlush(Message payload, Action<Message> postDefinitions, Action<Message> forward)
    {
        var envelope = new FlushThenForwardMessage(payload, forward);
        try
        {
            postDefinitions(envelope);
            if (!envelope.IsHandedOff)
            {
                throw new InvalidOperationException("Handing a batch to the definitions block failed: the postDefinitions callback must post the FlushThenForwardMessage to a definitions block that accepts it, or deliver it. The batch was released and not forwarded.");
            }
        }
        catch
        {
            // Not logged here: the exception faults the producer's worker (logged once when the fault happens)
            // or fails the final flush in Dispose (logged once there).
            envelope.Dispose();
            throw;
        }
    }

    internal static void ReleaseMisrouted(this FlushThenForwardMessage message, ILogger logger)
    {
        message.Dispose();
        Log(logger, LogMisroutedEnvelope, null, "A flush-then-forward envelope was posted to a block that does not own definitions; its payload was released.");
    }

    internal static Action<Message> PostTo(DefinitionsBlock definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return message => definitions.PostOrThrow(message);
    }

    private static void Log(ILogger logger, Action<ILogger, Exception> log, Exception exception, string fallback)
    {
        try
        {
            if (logger != null)
            {
                log(logger, exception);
            }
            else
            {
                Trace.TraceError("{0} {1}", fallback, exception);
            }
        }
        catch (Exception loggingException)
        {
            Trace.TraceError("Logging failed. {0}", loggingException);
        }
    }
}
