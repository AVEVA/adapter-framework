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
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// Instruments for awaitable scopes, published on the <see cref="OmfAwaitableCoordinator.MeterName"/> meter.
/// </summary>
internal static class OmfAwaitableMetrics
{
    private const string NoReason = "None";

    private static readonly Meter Meter = new(OmfAwaitableCoordinator.MeterName);

    private static readonly UpDownCounter<int> ActiveScopes = Meter.CreateUpDownCounter<int>(
        "omf.awaitable.scopes.active", description: "Awaitable scopes created and not yet disposed.");

    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "omf.awaitable.scope.outcomes", description: "Completed scope outcomes, by outcome and reason code.");

    private static readonly Counter<long> Dispositions = Meter.CreateCounter<long>(
        "omf.awaitable.delivery.dispositions", description: "Final delivery states, by state and reason code.");

    private static readonly Histogram<double> WaitDuration = Meter.CreateHistogram<double>(
        "omf.awaitable.wait.duration", unit: "s", description: "Duration of WaitForAcceptanceAsync calls, by result.");

    /// <summary>
    /// Records a created scope.
    /// </summary>
    public static void ScopeCreated() => ActiveScopes.Add(1);

    /// <summary>
    /// Records a released scope.
    /// </summary>
    public static void ScopeReleased() => ActiveScopes.Add(-1);

    /// <summary>
    /// Records a completed scope outcome.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <param name="reason">The outcome reason, or null for a successful outcome.</param>
    public static void OutcomeCompleted(OmfAcceptanceOutcome outcome, OmfOutcomeReason reason) =>
        Outcomes.Add(1, new KeyValuePair<string, object>("outcome", outcome.ToString()), ReasonTag(reason));

    /// <summary>
    /// Records a final delivery state.
    /// </summary>
    /// <param name="state">The final state.</param>
    /// <param name="reason">The failure reason, or null for a successful state.</param>
    public static void DeliveryCompleted(OmfDeliveryState state, OmfOutcomeReason reason) =>
        Dispositions.Add(1, new KeyValuePair<string, object>("state", state.ToString()), ReasonTag(reason));

    /// <summary>
    /// Records the duration of one wait.
    /// </summary>
    /// <param name="seconds">The wait duration in seconds.</param>
    /// <param name="result">The wait result: an outcome name, or <c>Canceled</c>.</param>
    public static void WaitEnded(double seconds, string result) =>
        WaitDuration.Record(seconds, new KeyValuePair<string, object>("result", result));

    /// <summary>
    /// Creates the reason code tag.
    /// </summary>
    /// <param name="reason">The reason, or null.</param>
    /// <returns>The tag.</returns>
    private static KeyValuePair<string, object> ReasonTag(OmfOutcomeReason reason) =>
        new("reason", reason?.Code.ToString() ?? NoReason);
}
