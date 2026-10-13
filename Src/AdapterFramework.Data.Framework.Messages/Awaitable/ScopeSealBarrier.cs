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
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// Posted to a grouping block when a scope seals. The block's queue is FIFO, so every item the scope wrote precedes it.
/// </summary>
public sealed class ScopeSealBarrier : Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeSealBarrier"/> class.
    /// </summary>
    /// <param name="scope">The sealed scope.</param>
    /// <param name="flushOnSeal">Whether the block should flush the scope's buffered items now.</param>
    /// <param name="expectedBarrierCount">How many grouping blocks received a seal barrier for the scope.</param>
    public ScopeSealBarrier(ScopeToken scope, bool flushOnSeal, int expectedBarrierCount)
    {
        Scope = scope;
        FlushOnSeal = flushOnSeal;
        ExpectedBarrierCount = expectedBarrierCount;
    }

    /// <summary>
    /// Gets a value indicating whether the block should flush the scope's buffered items now.
    /// </summary>
    public bool FlushOnSeal { get; }

    /// <summary>
    /// Gets how many grouping blocks received a seal barrier for the scope.
    /// </summary>
    public int ExpectedBarrierCount { get; }
}
