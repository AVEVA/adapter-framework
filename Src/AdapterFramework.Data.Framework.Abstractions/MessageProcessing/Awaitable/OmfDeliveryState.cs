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
/// The state of one body assigned to one endpoint writer.
/// </summary>
public enum OmfDeliveryState
{
    /// <summary>
    /// The delivery has no disposition yet.
    /// </summary>
    Pending,

    /// <summary>
    /// The endpoint returned exact 202 Accepted.
    /// </summary>
    Accepted,

    /// <summary>
    /// The endpoint returned a terminal non-202 response.
    /// </summary>
    Rejected,

    /// <summary>
    /// The body was dropped without a response.
    /// </summary>
    Discarded,

    /// <summary>
    /// The hot-failover peer's acceptance watermark covers the body.
    /// </summary>
    PeerCovered,
}
