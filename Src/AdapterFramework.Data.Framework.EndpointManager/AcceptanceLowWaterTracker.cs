// Copyright 2018-2026 AVEVA Group Limited
// Licensed under the Apache License, Version 2.0 (the "License");
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;

namespace AdapterFramework.Data.Framework.EndpointManager;

/// <summary>
/// Tracks every admitted body, independently of scope IDs and queue priority. A permanent gap releases
/// the multiset and freezes the boundary, bounding memory even while the endpoint continues sending.
/// No tracker lock may be held across queue operations: loss reports can arrive under queue locks.
/// </summary>
internal sealed class AcceptanceLowWaterTracker
{
    private readonly object _lock = new();
    private readonly SortedDictionary<long, int> _pending = new();
    private long _lastAdmitted;
    private long _pendingUnknown;
    private bool _recovering;
    private long? _frozen;

    /// <summary>Registers a body before enqueueing it; pending unknown timestamps block trimming.</summary>
    /// <param name="ticks">The body's process-time ticks, or zero when unknown.</param>
    public void Register(long ticks)
    {
        lock (_lock)
        {
            if (_frozen.HasValue)
            {
                return;
            }

            if (ticks <= 0)
            {
                if (_pendingUnknown == long.MaxValue)
                {
                    FreezeCore(0);
                }
                else
                {
                    _pendingUnknown++;
                }

                return;
            }

            if (ticks > DateTime.MaxValue.Ticks)
            {
                FreezeCore(0);
                return;
            }

            _lastAdmitted = Math.Max(_lastAdmitted, ticks);
            _pending.TryGetValue(ticks, out var count);
            _pending[ticks] = count + 1;
        }
    }

    /// <summary>Releases one matching body only after exact 202 acceptance and dequeue.</summary>
    /// <param name="ticks">The accepted body's process-time ticks.</param>
    public void Accept(long ticks)
    {
        lock (_lock)
        {
            if (_frozen.HasValue)
            {
                return;
            }

            if (_recovering && ticks > 0 && ticks <= DateTime.MaxValue.Ticks)
            {
                _lastAdmitted = Math.Max(_lastAdmitted, ticks);
            }

            if (ticks <= 0 && _pendingUnknown > 0)
            {
                _pendingUnknown--;
            }
            else if (!_pending.TryGetValue(ticks, out var count))
            {
                // Recovered bodies have no registration. Timestamp collisions with new bodies
                // are harmless only while coverage is gated until BOTH queues are empty.
                if (!_recovering || ticks > DateTime.MaxValue.Ticks)
                {
                    FreezeCore(0);
                }
            }
            else if (count == 1)
            {
                _pending.Remove(ticks);
            }
            else
            {
                _pending[ticks] = count - 1;
            }
        }
    }

    /// <summary>Freezes at the current safe boundary following a loss or terminal non-202 response.</summary>
    public void Freeze()
    {
        lock (_lock)
        {
            FreezeCore(GetTicks());
        }
    }

    /// <summary>Blocks coverage until all recovered and newly admitted bodies have drained.</summary>
    public void BeginRecovery()
    {
        lock (_lock)
        {
            _recovering = true;
        }
    }

    /// <summary>
    /// Ends recovery after the sole consumer observes both queues empty under the admission lock.
    /// Clears ambiguous timestamp registrations, but never clears a rejection or loss freeze.
    /// </summary>
    public void CompleteRecovery()
    {
        lock (_lock)
        {
            if (_recovering)
            {
                _pending.Clear();
                _pendingUnknown = 0;
                _recovering = false;
            }
        }
    }

    /// <summary>Gets the inclusive safe boundary, one tick below the oldest pending timestamp.</summary>
    /// <returns>The acceptance watermark in UTC.</returns>
    public DateTime GetWatermark()
    {
        lock (_lock)
        {
            return new DateTime(GetTicks(), DateTimeKind.Utc);
        }
    }

    private long GetTicks() => _frozen ?? (_recovering || _pendingUnknown > 0 ? 0
        : _pending.Count == 0 ? _lastAdmitted : Math.Min(_lastAdmitted, _pending.First().Key - 1));

    private void FreezeCore(long ticks)
    {
        _frozen = Math.Min(_frozen ?? ticks, ticks);
        _pending.Clear();
        _pendingUnknown = 0;
    }
}