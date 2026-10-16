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
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Disposes the blocks of a dataflow pipeline.
/// </summary>
public static class DataflowDisposal
{
    private static readonly Action<ILogger, Exception> LogBlockDisposeFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.BlockDisposeFailed, nameof(LogBlockDisposeFailed)),
            "Disposing a dataflow block failed; continuing with the remaining blocks.");

    /// <summary>
    /// Reports that disposing one block of a pipeline failed, so the owner can continue with the remaining blocks.
    /// Never throws, even if the log provider fails.
    /// </summary>
    /// <param name="logger">The logger; may be <see langword="null"/>.</param>
    /// <param name="exception">The disposal failure.</param>
    /// <remarks>
    /// Owners call <c>Dispose()</c> on their own fields inside their own <c>try</c> block, so that CA2213 can verify it, and call this from the <c>catch</c>.
    /// </remarks>
    public static void ReportDisposeFailure(ILogger logger, Exception exception) =>
        DataflowLog.Write(logger, LogBlockDisposeFailed, exception, "Disposing a dataflow block failed; continuing with the remaining blocks.");
}
