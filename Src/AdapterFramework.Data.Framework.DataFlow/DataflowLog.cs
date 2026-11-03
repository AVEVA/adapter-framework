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
/// Writes dataflow diagnostics to the injected logger from paths that must not throw, such as the worker-stopped continuation,
/// <c>Dispose</c>, and cleanup in <c>catch</c> and <c>finally</c> blocks. An exception from the logger there would make
/// <c>Dispose</c> throw, stop the remaining payloads from being released, or replace the original failure.
/// </summary>
/// <remarks>
/// A <see langword="null"/> logger writes nothing, like the <c>Logger?.</c> calls elsewhere in these blocks. The framework's
/// component logger writes through Serilog, which does not throw when a sink fails, but these public blocks accept any
/// <see cref="ILogger"/>; if that logger throws, the entry is dropped.
/// </remarks>
internal static class DataflowLog
{
    internal static void Write(ILogger logger, Action<ILogger, Exception> log, Exception exception)
    {
        if (logger == null)
        {
            return;
        }

        try
        {
            log(logger, exception);
        }
        catch (Exception)
        {
            // Nothing else can record it, and rethrowing would break the caller's no-throw guarantee.
        }
    }

    internal static void Write<T>(ILogger logger, Action<ILogger, T, Exception> log, T value, Exception exception)
    {
        if (logger == null)
        {
            return;
        }

        try
        {
            log(logger, value, exception);
        }
        catch (Exception)
        {
            // Nothing else can record it, and rethrowing would break the caller's no-throw guarantee.
        }
    }
}
