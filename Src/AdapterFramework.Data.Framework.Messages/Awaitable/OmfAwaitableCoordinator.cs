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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.Messages.Tests")]
namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// Tracks awaitable scopes, the bodies that carry their items, and the deliveries of those bodies, and completes scope outcomes.
/// </summary>
/// <remarks>
/// Framework infrastructure. Pipeline stages report admission, serialization, dispatch, attempts, and dispositions.
/// Reports for unknown scopes and bodies are ignored, so unscoped writes need no tracking.
/// Continuations never run under the coordinator lock.
/// </remarks>
public sealed class OmfAwaitableCoordinator : IDisposable
{
    /// <summary>
    /// The default maximum number of scopes that can be active at once.
    /// </summary>
    public const int DefaultMaxActiveScopes = 1000;

    private readonly object _lock = new();
    private readonly Dictionary<OmfAwaitableScopeState, ScopeTracking> _scopes = new();
    private readonly Dictionary<Guid, BodyTracking> _bodies = new();
    private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private OmfVersion? _omfVersion;
    private FailoverMode _failoverMode;
    private Action<OmfAwaitableScopeState> _sealHandler;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OmfAwaitableCoordinator"/> class.
    /// </summary>
    /// <param name="maxActiveScopes">The maximum number of scopes that can be active at once.</param>
    public OmfAwaitableCoordinator(int maxActiveScopes = DefaultMaxActiveScopes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxActiveScopes, 1);
        MaxActiveScopes = maxActiveScopes;
    }

    /// <summary>
    /// Gets the maximum number of scopes that can be active at once.
    /// </summary>
    public int MaxActiveScopes { get; }

    /// <summary>
    /// Gets the number of scopes that are created and not yet disposed.
    /// </summary>
    public int ActiveScopeCount
    {
        get
        {
            lock (_lock)
            {
                return _scopes.Count;
            }
        }
    }

    internal Task ShutdownTask => _shutdown.Task;

    internal int TrackedBodyCount
    {
        get
        {
            lock (_lock)
            {
                return _bodies.Count;
            }
        }
    }

    /// <summary>
    /// Sets the OMF version of the data pipeline. Scopes require OMF 2.0.
    /// </summary>
    /// <param name="omfVersion">The configured OMF version.</param>
    public void SetOmfVersion(OmfVersion omfVersion)
    {
        lock (_lock)
        {
            _omfVersion = omfVersion;
        }
    }

    /// <summary>
    /// Sets the current failover mode. Scopes aren't supported in hot failover mode.
    /// </summary>
    /// <param name="failoverMode">The current failover mode.</param>
    public void SetFailoverMode(FailoverMode failoverMode)
    {
        lock (_lock)
        {
            _failoverMode = failoverMode;
        }
    }

    /// <summary>
    /// Sets the action that starts materialization when a scope with admitted items is sealed, for example by posting seal barriers.
    /// </summary>
    /// <param name="sealHandler">The action. It runs outside the coordinator lock.</param>
    public void SetSealHandler(Action<OmfAwaitableScopeState> sealHandler)
    {
        lock (_lock)
        {
            _sealHandler = sealHandler;
        }
    }

    /// <summary>
    /// Creates a scope unless the active-scope limit is reached.
    /// </summary>
    /// <param name="options">The scope options.</param>
    /// <param name="scope">The created scope, or null when the limit is reached.</param>
    /// <returns><c>true</c> if the scope was created; otherwise <c>false</c>.</returns>
    /// <exception cref="NotSupportedException">The pipeline isn't OMF 2.0, or hot failover is configured.</exception>
    /// <exception cref="ObjectDisposedException">The coordinator is shut down.</exception>
    public bool TryCreateScope(OmfAwaitableScopeOptions options, out OmfAwaitableScopeState scope)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.DefaultWaitTimeout <= TimeSpan.Zero && options.DefaultWaitTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.DefaultWaitTimeout, "The default wait timeout must be positive or infinite.");
        }

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_omfVersion != OmfVersion.Omf20)
            {
                throw new NotSupportedException("Awaitable scopes require an OMF 2.0 data pipeline.");
            }

            if (_failoverMode.HasFlag(FailoverMode.Hot))
            {
                throw new NotSupportedException("Awaitable scopes aren't supported when hot failover is configured.");
            }

            if (_scopes.Count >= MaxActiveScopes)
            {
                scope = null;
                return false;
            }

            scope = new OmfAwaitableScopeState(this, options);
            _scopes.Add(scope, new ScopeTracking(scope));
            return true;
        }
    }

    /// <summary>
    /// Records items that a scoped write is about to post to a grouping block.
    /// </summary>
    /// <param name="scope">The scope that wrote the items.</param>
    /// <param name="itemCount">The number of items.</param>
    public void RecordAdmitted(ScopeToken scope, int itemCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        lock (_lock)
        {
            if (TryGetTracking(scope, out var tracking))
            {
                tracking.Admitted += itemCount;
            }
        }
    }

    /// <summary>
    /// Records admitted items that were dropped before they reached a body.
    /// </summary>
    /// <param name="scope">The scope that wrote the items.</param>
    /// <param name="itemCount">The number of dropped items.</param>
    /// <param name="reason">Why the items were dropped.</param>
    public void RecordItemsDiscarded(ScopeToken scope, int itemCount, OmfOutcomeReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemCount);
        ArgumentNullException.ThrowIfNull(reason);
        List<OmfAwaitableScopeState> completed = null;
        lock (_lock)
        {
            if (TryGetTracking(scope, out var tracking))
            {
                tracking.Discarded += itemCount;
                Evaluate(tracking, OmfAcceptanceOutcome.Discarded, reason, ref completed);
            }
        }

        SignalCompleted(completed);
    }

    /// <summary>
    /// Registers a body and the number of items it carries for each scope, before the body is dispatched.
    /// </summary>
    /// <param name="serializedMessageId">The ID of the serialized body.</param>
    /// <param name="membership">The number of items the body carries per scope.</param>
    /// <exception cref="ArgumentException">The body is already registered.</exception>
    public void RegisterBody(Guid serializedMessageId, IReadOnlyDictionary<ScopeToken, int> membership)
    {
        ArgumentNullException.ThrowIfNull(membership);
        lock (_lock)
        {
            if (_bodies.ContainsKey(serializedMessageId))
            {
                throw new ArgumentException("The body is already registered.", nameof(serializedMessageId));
            }

            BodyTracking body = null;
            foreach (var (token, itemCount) in membership)
            {
                if (itemCount <= 0 || !TryGetTracking(token, out var tracking))
                {
                    continue;
                }

                body ??= new BodyTracking(serializedMessageId);
                body.Membership[tracking] = itemCount;
                tracking.Serialized += itemCount;
                tracking.Bodies.Add(body);
            }

            if (body is not null)
            {
                _bodies.Add(serializedMessageId, body);
            }
        }
    }

    /// <summary>
    /// Records that every body carrying the scope's items is registered, and checks that no admitted item is unaccounted for.
    /// </summary>
    /// <param name="scope">The materialized scope.</param>
    public void CloseMaterialization(ScopeToken scope)
    {
        List<OmfAwaitableScopeState> completed = null;
        lock (_lock)
        {
            if (!TryGetTracking(scope, out var tracking) || tracking.Materialized)
            {
                return;
            }

            tracking.Materialized = true;
            var shortfall = tracking.Admitted - tracking.Serialized - tracking.Discarded;
            if (shortfall > 0)
            {
                var reason = new OmfOutcomeReason(
                    OmfReasonCode.CountMismatch,
                    string.Create(CultureInfo.InvariantCulture, $"Admitted {tracking.Admitted} items but serialized {tracking.Serialized} and discarded {tracking.Discarded}."));
                tracking.Discarded += shortfall;
                Evaluate(tracking, OmfAcceptanceOutcome.Discarded, reason, ref completed);
            }
            else
            {
                Evaluate(tracking, null, null, ref completed);
            }
        }

        SignalCompleted(completed);
    }

    /// <summary>
    /// Registers one delivery per endpoint writer in the snapshot captured for the body, before any writer sends it.
    /// An empty snapshot discards the body with <see cref="OmfReasonCode.NoEndpoints"/>.
    /// </summary>
    /// <param name="serializedMessageId">The ID of the serialized body.</param>
    /// <param name="targets">The endpoint writer snapshot.</param>
    /// <exception cref="InvalidOperationException">The body's deliveries are already registered.</exception>
    public void RegisterDeliveries(Guid serializedMessageId, IReadOnlyList<OmfDeliveryTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        List<OmfAwaitableScopeState> completed = null;
        lock (_lock)
        {
            if (!_bodies.TryGetValue(serializedMessageId, out var body))
            {
                return;
            }

            if (body.Deliveries is not null)
            {
                throw new InvalidOperationException("The body's deliveries are already registered.");
            }

            body.Deliveries = targets
                .Select(target => new DeliveryTracking(target.EndpointId, target.TargetUri))
                .ToList();

            if (body.Deliveries.Count == 0)
            {
                body.DiscardReason = new OmfOutcomeReason(OmfReasonCode.NoEndpoints, string.Empty);
            }

            // In AnyCompleteEndpoint mode, a body sent to a disjoint snapshot leaves no endpoint that can complete the scope.
            var reason = body.DiscardReason
                ?? new OmfOutcomeReason(OmfReasonCode.NoEndpoints, "No single endpoint received every body in the scope.");
            foreach (var tracking in body.Membership.Keys)
            {
                Evaluate(tracking, OmfAcceptanceOutcome.Discarded, reason, ref completed);
            }
        }

        SignalCompleted(completed);
    }

    /// <summary>
    /// Records a non-final HTTP attempt for a pending delivery, such as a retryable status or a transport error.
    /// </summary>
    /// <param name="serializedMessageId">The ID of the serialized body.</param>
    /// <param name="endpointId">The endpoint ID.</param>
    /// <param name="statusCode">The response status, or null for a transport error.</param>
    /// <param name="reason">What the delivery is waiting on, typically <see cref="OmfReasonCode.Retrying"/>.</param>
    public void RecordAttempt(Guid serializedMessageId, string endpointId, HttpStatusCode? statusCode, OmfOutcomeReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        lock (_lock)
        {
            if (TryGetPendingDelivery(serializedMessageId, endpointId, out _, out var delivery))
            {
                delivery.LastStatusCode = statusCode ?? delivery.LastStatusCode;
                delivery.Reason = reason;
            }
        }
    }

    /// <summary>
    /// Records the final decision for a pending delivery. Later dispositions for the same delivery are ignored.
    /// </summary>
    /// <param name="serializedMessageId">The ID of the serialized body.</param>
    /// <param name="endpointId">The endpoint ID.</param>
    /// <param name="state">The final delivery state.</param>
    /// <param name="statusCode">The last response status, or null when no response applies.</param>
    /// <param name="reason">Why the delivery failed; required unless <paramref name="state"/> is accepted or peer-covered.</param>
    /// <param name="receipt">Values parsed from a 202 response body, when available.</param>
    public void RecordDisposition(
        Guid serializedMessageId,
        string endpointId,
        OmfDeliveryState state,
        HttpStatusCode? statusCode,
        OmfOutcomeReason reason,
        OmfIngressReceipt receipt = null)
    {
        if (state == OmfDeliveryState.Pending)
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A disposition must be final.");
        }

        var succeeded = state is OmfDeliveryState.Accepted or OmfDeliveryState.PeerCovered;
        if (!succeeded)
        {
            ArgumentNullException.ThrowIfNull(reason);
        }

        List<OmfAwaitableScopeState> completed = null;
        lock (_lock)
        {
            if (!TryGetPendingDelivery(serializedMessageId, endpointId, out var body, out var delivery))
            {
                return;
            }

            CompleteDelivery(delivery, state, statusCode, succeeded ? null : reason, receipt);
            OmfAcceptanceOutcome? failureOutcome = succeeded ? null
                : state == OmfDeliveryState.Rejected ? OmfAcceptanceOutcome.Rejected : OmfAcceptanceOutcome.Discarded;
            foreach (var tracking in body.Membership.Keys)
            {
                Evaluate(tracking, failureOutcome, reason, ref completed);
            }
        }

        SignalCompleted(completed);
    }

    /// <summary>
    /// Discards every pending delivery for an endpoint, for example when its writer is removed.
    /// </summary>
    /// <param name="endpointId">The endpoint ID.</param>
    /// <param name="reason">Why the deliveries were discarded.</param>
    public void RecordEndpointDiscarded(string endpointId, OmfOutcomeReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        List<OmfAwaitableScopeState> completed = null;
        lock (_lock)
        {
            foreach (var body in _bodies.Values)
            {
                var discarded = false;
                foreach (var delivery in body.Deliveries ?? Enumerable.Empty<DeliveryTracking>())
                {
                    if (delivery.State == OmfDeliveryState.Pending && string.Equals(delivery.EndpointId, endpointId, StringComparison.Ordinal))
                    {
                        CompleteDelivery(delivery, OmfDeliveryState.Discarded, null, reason, null);
                        discarded = true;
                    }
                }

                if (discarded)
                {
                    foreach (var tracking in body.Membership.Keys)
                    {
                        Evaluate(tracking, OmfAcceptanceOutcome.Discarded, reason, ref completed);
                    }
                }
            }
        }

        SignalCompleted(completed);
    }

    /// <summary>
    /// Shuts the coordinator down. Outstanding and later waits are cancelled, and no more scopes can be created.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }

        _shutdown.TrySetResult();
    }

    internal void MarkSealed(OmfAwaitableScopeState scope)
    {
        List<OmfAwaitableScopeState> completed = null;
        Action<OmfAwaitableScopeState> sealHandler = null;
        lock (_lock)
        {
            if (_scopes.TryGetValue(scope, out var tracking))
            {
                tracking.Sealed = true;
                Evaluate(tracking, null, null, ref completed);
                if (tracking.Outcome is null && !tracking.Materialized && tracking.Admitted > 0)
                {
                    sealHandler = _sealHandler;
                }
            }
        }

        SignalCompleted(completed);
        sealHandler?.Invoke(scope);
    }

    internal void Release(OmfAwaitableScopeState scope)
    {
        lock (_lock)
        {
            if (!_scopes.Remove(scope, out var tracking))
            {
                return;
            }

            foreach (var body in tracking.Bodies)
            {
                body.Membership.Remove(tracking);
                if (body.Membership.Count == 0)
                {
                    _bodies.Remove(body.Id);
                }
            }
        }
    }

    internal OmfAcceptanceResult GetResult(OmfAwaitableScopeState scope)
    {
        lock (_lock)
        {
            if (!_scopes.TryGetValue(scope, out var tracking))
            {
                throw new OperationCanceledException("The awaitable scope was disposed.");
            }

            var deliveries = tracking.Bodies
                .SelectMany(body => (body.Deliveries ?? Enumerable.Empty<DeliveryTracking>()).Select(delivery => delivery.ToResult(body.Id)))
                .ToList();

            return tracking.Outcome is { } outcome
                ? new OmfAcceptanceResult(outcome, scope.Options.EndpointMode, deliveries, tracking.OutcomeReason, DateTimeOffset.UtcNow)
                : new OmfAcceptanceResult(OmfAcceptanceOutcome.TimedOut, scope.Options.EndpointMode, deliveries, GetLeastAdvancedReason(tracking), DateTimeOffset.UtcNow);
        }
    }

    private static void SignalCompleted(List<OmfAwaitableScopeState> completed)
    {
        if (completed is null)
        {
            return;
        }

        foreach (var scope in completed)
        {
            scope.SignalCompleted();
        }
    }

    private static void CompleteDelivery(DeliveryTracking delivery, OmfDeliveryState state, HttpStatusCode? statusCode, OmfOutcomeReason reason, OmfIngressReceipt receipt)
    {
        delivery.State = state;
        delivery.LastStatusCode = statusCode ?? delivery.LastStatusCode;
        delivery.Reason = reason;
        delivery.Receipt = receipt ?? delivery.Receipt;
        delivery.CompletedAtUtc = DateTimeOffset.UtcNow;
    }

    private static void Evaluate(ScopeTracking tracking, OmfAcceptanceOutcome? failureOutcome, OmfOutcomeReason failureReason, ref List<OmfAwaitableScopeState> completed)
    {
        if (tracking.Outcome is not null)
        {
            return;
        }

        // Failures are final as soon as they make the endpoint mode unsatisfiable; the event that did so supplies the reason.
        if (failureOutcome is not null && IsUnsatisfiable(tracking))
        {
            Complete(tracking, failureOutcome.Value, failureReason, ref completed);
            return;
        }

        if (!tracking.Sealed)
        {
            return;
        }

        if (tracking.Admitted == 0)
        {
            Complete(tracking, OmfAcceptanceOutcome.Filtered, null, ref completed);
        }
        else if (tracking.Materialized && IsSatisfied(tracking, out var peerCovered))
        {
            Complete(tracking, peerCovered ? OmfAcceptanceOutcome.PeerCovered : OmfAcceptanceOutcome.Accepted, null, ref completed);
        }
    }

    private static void Complete(ScopeTracking tracking, OmfAcceptanceOutcome outcome, OmfOutcomeReason reason, ref List<OmfAwaitableScopeState> completed)
    {
        tracking.Outcome = outcome;
        tracking.OutcomeReason = reason;
        (completed ??= new List<OmfAwaitableScopeState>()).Add(tracking.Scope);
    }

    private static bool IsUnsatisfiable(ScopeTracking tracking)
    {
        if (tracking.Discarded > 0)
        {
            return true;
        }

        var anyEndpoint = tracking.Scope.Options.EndpointMode == OmfAwaitableEndpointMode.AnyCompleteEndpoint;
        HashSet<string> viableEndpoints = null;
        foreach (var body in tracking.Bodies)
        {
            if (body.DiscardReason is not null)
            {
                return true;
            }

            if (body.Deliveries is null || body.IsPeerCovered)
            {
                continue;
            }

            if (!anyEndpoint)
            {
                if (body.Deliveries.Exists(delivery => delivery.IsFailed))
                {
                    return true;
                }

                continue;
            }

            var bodyEndpoints = body.Deliveries.Where(delivery => !delivery.IsFailed).Select(delivery => delivery.EndpointId);
            if (viableEndpoints is null)
            {
                viableEndpoints = new HashSet<string>(bodyEndpoints, StringComparer.Ordinal);
            }
            else
            {
                viableEndpoints.IntersectWith(bodyEndpoints);
            }

            if (viableEndpoints.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSatisfied(ScopeTracking tracking, out bool peerCovered)
    {
        peerCovered = false;
        var anyEndpoint = tracking.Scope.Options.EndpointMode == OmfAwaitableEndpointMode.AnyCompleteEndpoint;
        HashSet<string> completeEndpoints = null;
        foreach (var body in tracking.Bodies)
        {
            if (body.Deliveries is null)
            {
                return false;
            }

            if (body.IsPeerCovered)
            {
                peerCovered = true;
                continue;
            }

            if (!anyEndpoint)
            {
                if (!body.Deliveries.TrueForAll(delivery => delivery.State == OmfDeliveryState.Accepted))
                {
                    return false;
                }

                continue;
            }

            var acceptedEndpoints = body.Deliveries.Where(delivery => delivery.State == OmfDeliveryState.Accepted).Select(delivery => delivery.EndpointId);
            if (completeEndpoints is null)
            {
                completeEndpoints = new HashSet<string>(acceptedEndpoints, StringComparer.Ordinal);
            }
            else
            {
                completeEndpoints.IntersectWith(acceptedEndpoints);
            }

            if (completeEndpoints.Count == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static OmfOutcomeReason GetLeastAdvancedReason(ScopeTracking tracking)
    {
        if (!tracking.Materialized)
        {
            var buffered = Math.Max(tracking.Admitted - tracking.Serialized - tracking.Discarded, 0);
            return new OmfOutcomeReason(
                OmfReasonCode.AwaitingFlush,
                string.Create(CultureInfo.InvariantCulture, $"{buffered} items are waiting for a grouping block flush."));
        }

        OmfOutcomeReason leastAdvanced = null;
        foreach (var body in tracking.Bodies)
        {
            if (body.Deliveries is null)
            {
                return new OmfOutcomeReason(OmfReasonCode.Queued, "A body is waiting for dispatch.");
            }

            if (body.IsPeerCovered)
            {
                continue;
            }

            foreach (var delivery in body.Deliveries)
            {
                if (delivery.State == OmfDeliveryState.Pending && (leastAdvanced is null || delivery.Reason.Stage < leastAdvanced.Stage))
                {
                    leastAdvanced = delivery.Reason;
                }
            }
        }

        return leastAdvanced ?? new OmfOutcomeReason(OmfReasonCode.Queued, string.Empty);
    }

    private bool TryGetTracking(ScopeToken token, out ScopeTracking tracking)
    {
        tracking = null;
        return token is OmfAwaitableScopeState scope && _scopes.TryGetValue(scope, out tracking);
    }

    private bool TryGetPendingDelivery(Guid serializedMessageId, string endpointId, out BodyTracking body, out DeliveryTracking delivery)
    {
        delivery = null;
        if (!_bodies.TryGetValue(serializedMessageId, out body) || body.Deliveries is null)
        {
            return false;
        }

        delivery = body.Deliveries.Find(candidate =>
            candidate.State == OmfDeliveryState.Pending && string.Equals(candidate.EndpointId, endpointId, StringComparison.Ordinal));
        return delivery is not null;
    }

    private sealed class ScopeTracking
    {
        public ScopeTracking(OmfAwaitableScopeState scope)
        {
            Scope = scope;
        }

        public OmfAwaitableScopeState Scope { get; }

        public int Admitted { get; set; }

        public int Serialized { get; set; }

        public int Discarded { get; set; }

        public bool Sealed { get; set; }

        public bool Materialized { get; set; }

        public List<BodyTracking> Bodies { get; } = new();

        public OmfAcceptanceOutcome? Outcome { get; set; }

        public OmfOutcomeReason OutcomeReason { get; set; }
    }

    private sealed class BodyTracking
    {
        public BodyTracking(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }

        public Dictionary<ScopeTracking, int> Membership { get; } = new();

        public List<DeliveryTracking> Deliveries { get; set; }

        public OmfOutcomeReason DiscardReason { get; set; }

        public bool IsPeerCovered => Deliveries?.Exists(delivery => delivery.State == OmfDeliveryState.PeerCovered) == true;
    }

    private sealed class DeliveryTracking
    {
        public DeliveryTracking(string endpointId, Uri targetUri)
        {
            EndpointId = endpointId;
            TargetUri = targetUri;
            Reason = new OmfOutcomeReason(OmfReasonCode.Queued, $"Queued for endpoint '{endpointId}'.");
        }

        public string EndpointId { get; }

        public Uri TargetUri { get; }

        public OmfDeliveryState State { get; set; }

        public HttpStatusCode? LastStatusCode { get; set; }

        public OmfOutcomeReason Reason { get; set; }

        public OmfIngressReceipt Receipt { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public bool IsFailed => State is OmfDeliveryState.Rejected or OmfDeliveryState.Discarded;

        public OmfDeliveryResult ToResult(Guid serializedMessageId) =>
            new(EndpointId, TargetUri, serializedMessageId, State, LastStatusCode, Reason, Receipt, CompletedAtUtc);
    }
}
