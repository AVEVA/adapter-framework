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
namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

/// <summary>
/// The machine-readable cause in an <see cref="OmfOutcomeReason"/>.
/// </summary>
/// <remarks>
/// Codes are never renamed or reused. Later releases may add codes, so callers must handle unknown values.
/// </remarks>
public enum OmfReasonCode
{
    /// <summary>
    /// A grouping block refused the envelope.
    /// </summary>
    PostRejected = 0,

    /// <summary>
    /// Items are still buffered in a grouping block, waiting for a flush trigger.
    /// </summary>
    AwaitingFlush = 1,

    /// <summary>
    /// One item or value serializes above the body size limit and can't be split.
    /// </summary>
    ItemTooLarge = 2,

    /// <summary>
    /// Items serialized to an empty body, which isn't sent.
    /// </summary>
    EmptyBody = 3,

    /// <summary>
    /// Fewer items were serialized than admitted, with no drop reported.
    /// </summary>
    CountMismatch = 4,

    /// <summary>
    /// No endpoint writer existed when the body was dispatched.
    /// </summary>
    NoEndpoints = 5,

    /// <summary>
    /// A writer threw while accepting the body.
    /// </summary>
    EnqueueFailed = 6,

    /// <summary>
    /// The body is buffered and hasn't been attempted yet.
    /// </summary>
    Queued = 7,

    /// <summary>
    /// A configuration change removed the endpoint and deleted its buffers.
    /// </summary>
    EndpointRemoved = 8,

    /// <summary>
    /// An operator reset the data buffers.
    /// </summary>
    BuffersReset = 9,

    /// <summary>
    /// A memory or disk buffer limit evicted the body.
    /// </summary>
    BufferFull = 10,

    /// <summary>
    /// The body's record couldn't be read back from disk.
    /// </summary>
    CorruptRecord = 11,

    /// <summary>
    /// Writing the body to disk failed.
    /// </summary>
    DiskError = 12,

    /// <summary>
    /// The last attempt failed with a retryable status or a transport error.
    /// </summary>
    Retrying = 13,

    /// <summary>
    /// The endpoint returned 400, 403, 404, 409, or 501, which the writer doesn't retry.
    /// </summary>
    RejectedByEndpoint = 14,

    /// <summary>
    /// The endpoint returned a 2xx status other than 202.
    /// </summary>
    NonAcceptedSuccess = 15,

    /// <summary>
    /// This node's copy is waiting for the hot-failover peer's acceptance watermark.
    /// </summary>
    AwaitingPeerCoverage = 16,

    /// <summary>
    /// The failover buffer was deleted on a mode change or configuration removal.
    /// </summary>
    BufferDeleted = 17,
}
