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
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Writes dataflow diagnostics without ever throwing, falling back to <see cref="Trace"/> when there is no logger
/// or the log provider fails. Used on worker, disposal and cleanup paths where a logging failure must not mask the original problem.
/// </summary>
internal static class DataflowLog
{
    internal static void Write(ILogger logger, Action<ILogger, Exception> log, Exception exception, string fallback)
    {
        try
        {
            if (logger != null)
            {
                log(logger, exception);
            }
            else
            {
                Trace.TraceError("{0} {1}", fallback, exception);
            }
        }
        catch (Exception loggingException)
        {
            Trace.TraceError("{0} {1} Logging also failed: {2}", fallback, exception, loggingException);
        }
    }

    internal static void Write<T>(ILogger logger, Action<ILogger, T, Exception> log, T value, Exception exception, string fallback)
    {
        try
        {
            if (logger != null)
            {
                log(logger, value, exception);
            }
            else
            {
                Trace.TraceError("{0} ({1}) {2}", fallback, value, exception);
            }
        }
        catch (Exception loggingException)
        {
            Trace.TraceError("{0} ({1}) {2} Logging also failed: {3}", fallback, value, exception, loggingException);
        }
    }
}
