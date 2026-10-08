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

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

/// <summary>
/// The acceptance result of an awaitable scope.
/// </summary>
/// <param name="Outcome">The acceptance outcome.</param>
/// <param name="EndpointMode">The endpoint mode the outcome was evaluated with.</param>
/// <param name="Deliveries">Every delivery of every body that carries the scope's items.</param>
/// <param name="Reason">
/// Null for <see cref="OmfAcceptanceOutcome.Accepted"/>, <see cref="OmfAcceptanceOutcome.PeerCovered"/>, and <see cref="OmfAcceptanceOutcome.Filtered"/>.
/// For a failure, the first recorded failure that made the endpoint mode unsatisfiable. For <see cref="OmfAcceptanceOutcome.TimedOut"/>,
/// the reason of the least-advanced unfinished work.
/// </param>
/// <param name="ObservedAtUtc">When the result was observed.</param>
public sealed record OmfAcceptanceResult(
    OmfAcceptanceOutcome Outcome,
    OmfAwaitableEndpointMode EndpointMode,
    IReadOnlyList<OmfDeliveryResult> Deliveries,
    OmfOutcomeReason Reason,
    DateTimeOffset ObservedAtUtc);
