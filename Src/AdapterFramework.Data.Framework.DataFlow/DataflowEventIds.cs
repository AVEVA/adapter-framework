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
internal static class DataflowEventIds
{
    public const int BatchSizeChanged = 1;
    public const int DrainTimeout = 2;
    public const int DrainFaulted = 3;
    public const int DrainCanceled = 4;
    public const int FinalFlushFailed = 5;
    public const int DiscardedForward = 6;
    public const int ForwardFailed = 7;

    // 8 is intentionally unassigned.
    public const int MisroutedEnvelope = 9;
    public const int WorkerFaulted = 10;
    public const int BlockDisposeFailed = 11;
}
