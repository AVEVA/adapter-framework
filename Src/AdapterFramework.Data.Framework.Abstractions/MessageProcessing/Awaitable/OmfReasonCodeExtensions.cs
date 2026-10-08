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

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

/// <summary>
/// Extension methods for <see cref="OmfReasonCode"/>.
/// </summary>
public static class OmfReasonCodeExtensions
{
    /// <summary>
    /// Gets the pipeline stage the reason code belongs to.
    /// </summary>
    /// <param name="code">The reason code.</param>
    /// <returns>The stage of <paramref name="code"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="code"/> is not a known reason code.</exception>
    public static OmfPipelineStage GetStage(this OmfReasonCode code) => code switch
    {
        OmfReasonCode.PostRejected => OmfPipelineStage.Admission,
        OmfReasonCode.AwaitingFlush => OmfPipelineStage.Grouping,
        OmfReasonCode.ItemTooLarge or OmfReasonCode.EmptyBody or OmfReasonCode.CountMismatch => OmfPipelineStage.Serialization,
        OmfReasonCode.NoEndpoints or OmfReasonCode.EnqueueFailed => OmfPipelineStage.Dispatch,
        OmfReasonCode.Queued or OmfReasonCode.EndpointRemoved or OmfReasonCode.BuffersReset
            or OmfReasonCode.BufferFull or OmfReasonCode.CorruptRecord or OmfReasonCode.DiskError => OmfPipelineStage.Buffering,
        OmfReasonCode.Retrying or OmfReasonCode.RejectedByEndpoint or OmfReasonCode.NonAcceptedSuccess => OmfPipelineStage.Delivery,
        OmfReasonCode.AwaitingPeerCoverage or OmfReasonCode.BufferDeleted => OmfPipelineStage.Failover,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
