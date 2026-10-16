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
namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Event IDs of the log messages written by the dataflow blocks and their owners. Keep every ID unique.
/// </summary>
/// <remarks>
/// The dataflow blocks log through the owning component's logger, so their IDs use a dedicated range (5000-5099)
/// to avoid colliding with IDs used elsewhere in that component.
/// </remarks>
internal static class DataflowEventIds
{
    // Released before the dedicated range existed; kept so existing log filters still match.
    public const int BatchSizeChanged = 1;

    public const int DrainTimeout = 5001;
    public const int DrainFaulted = 5002;
    public const int DrainCanceled = 5003;
    public const int FinalFlushFailed = 5004;
    public const int DiscardedForward = 5005;
    public const int ForwardFailed = 5006;
    public const int MisroutedEnvelope = 5007;
    public const int WorkerFaulted = 5008;
    public const int BlockDisposeFailed = 5009;
    public const int ReleaseForwardFailed = 5010;
    public const int MessagesRejected = 5011;
}
