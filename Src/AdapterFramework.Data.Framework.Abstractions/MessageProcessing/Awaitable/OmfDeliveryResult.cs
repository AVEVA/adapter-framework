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
using System.Net;

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

/// <summary>
/// The state of one body assigned to one endpoint writer.
/// </summary>
/// <param name="EndpointId">The ID of the endpoint the body was dispatched to.</param>
/// <param name="TargetUri">The endpoint URI captured when the body was dispatched.</param>
/// <param name="SerializedMessageId">The ID of the serialized body.</param>
/// <param name="State">The delivery state.</param>
/// <param name="LastStatusCode">
/// The status of the most recent HTTP attempt, or null when there was none. The writer can retry a body several times,
/// so earlier attempts may have returned different statuses.
/// </param>
/// <param name="Reason">Why the delivery is in its state; null when <see cref="OmfDeliveryState.Accepted"/> or <see cref="OmfDeliveryState.PeerCovered"/>.</param>
/// <param name="Receipt">Values parsed from the 202 response body, when available.</param>
/// <param name="CompletedAtUtc">When the delivery reached its disposition, or null while pending.</param>
public sealed record OmfDeliveryResult(
    string EndpointId,
    Uri TargetUri,
    Guid SerializedMessageId,
    OmfDeliveryState State,
    HttpStatusCode? LastStatusCode,
    OmfOutcomeReason Reason,
    OmfIngressReceipt Receipt,
    DateTimeOffset? CompletedAtUtc);
