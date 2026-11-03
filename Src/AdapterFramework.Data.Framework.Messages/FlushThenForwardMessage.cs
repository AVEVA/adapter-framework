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

namespace AdapterFramework.Data.Framework.Messages;

/// <summary>
/// Asks a grouping block to flush what it has buffered, then hand <see cref="Payload"/> downstream
/// from its own thread (see <see cref="Deliver"/>), so the payload cannot overtake the flushed items.
/// </summary>
/// <remarks>
/// This is an in-process control message, not a serialized OMF message. The envelope owns the payload until
/// the callback returns successfully. A callback must either accept ownership and return, or throw without
/// retaining the payload. It must not transfer ownership and then throw. Failed callbacks are not retried.
/// Dispose an unprocessed envelope to release an untransferred disposable payload.
/// </remarks>
public sealed class FlushThenForwardMessage : Message, IDisposable
{
    private const int Pending = 0;
    private const int Forwarding = 1;
    private const int Handled = 2;

    private readonly Action<Message> _forward;
    private int _ownershipState;
    private int _accepted;
    private int _delivered;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlushThenForwardMessage"/> class.
    /// </summary>
    /// <param name="payload">The message to forward after the flush.</param>
    /// <param name="forward">The action that delivers <paramref name="payload"/> downstream.</param>
    public FlushThenForwardMessage(Message payload, Action<Message> forward)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(forward);

        Payload = payload;
        _forward = forward;
    }

    /// <summary>
    /// Gets the message to forward after the flush.
    /// </summary>
    public Message Payload { get; }

    /// <summary>
    /// Gets a value indicating whether the envelope was accepted by a definitions block or delivered.
    /// </summary>
    /// <remarks>
    /// Being released (for example by a block that does not own definitions) is deliberately not a handoff,
    /// so a misrouted or rejected envelope is always detected.
    /// </remarks>
    internal bool IsHandedOff => Volatile.Read(ref _accepted) != 0 || Volatile.Read(ref _delivered) != 0;

    /// <summary>
    /// Delivers <see cref="Payload"/> downstream. Invoke at most once, after definitions have been flushed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The payload has already been forwarded or released.</exception>
    public void Deliver()
    {
        if (Interlocked.CompareExchange(ref _ownershipState, Forwarding, Pending) != Pending)
        {
            throw new InvalidOperationException("The payload has already been forwarded or released.");
        }

        Volatile.Write(ref _delivered, 1);
        try
        {
            _forward(Payload);
        }
        catch (Exception forwardFailure)
        {
            Interlocked.Exchange(ref _ownershipState, Handled);
            try
            {
                (Payload as IDisposable)?.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                // Preserve the forwarding failure as the primary cause and include the cleanup failure.
                throw new AggregateException(forwardFailure, cleanupFailure);
            }

            throw;
        }

        Interlocked.Exchange(ref _ownershipState, Handled);
    }

    /// <summary>
    /// Releases the payload if it has not been forwarded.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _ownershipState, Handled, Pending) == Pending)
        {
            (Payload as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Records that a definitions block accepted this envelope and now owns it.
    /// </summary>
    internal void MarkAccepted() => Volatile.Write(ref _accepted, 1);
}
