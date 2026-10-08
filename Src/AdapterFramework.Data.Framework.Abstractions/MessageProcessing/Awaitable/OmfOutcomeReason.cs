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
/// Why a delivery or a scope is in its current state.
/// </summary>
/// <param name="Code">The machine-readable cause. Callers can branch on it.</param>
/// <param name="Message">Human-readable text for logs and support. It isn't stable, so callers must not parse it.</param>
public sealed record OmfOutcomeReason(OmfReasonCode Code, string Message)
{
    /// <summary>
    /// The maximum length of <see cref="Message"/>; longer messages are truncated.
    /// </summary>
    public const int MaxMessageLength = 1024;

    /// <summary>
    /// Gets the human-readable text for logs and support. It's at most <see cref="MaxMessageLength"/> characters and can be empty.
    /// </summary>
    public string Message { get; init; } = Message is null ? string.Empty
        : Message.Length > MaxMessageLength ? Message[..MaxMessageLength] : Message;

    /// <summary>
    /// Gets the pipeline stage <see cref="Code"/> belongs to.
    /// </summary>
    public OmfPipelineStage Stage => Code.GetStage();
}
