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
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Logging;

namespace AdapterFramework.Data.Framework.Tests.Helper;

/// <summary>
/// An ILogger implementation which logs messages to a collection. Useful for testing purposes.
/// </summary>
public class TestLogger : IInstrumentedLogger
{
    private readonly object _logSinkLock = new object();
    private readonly IList<LogEntry> _logSink;

    public TestLogger()
    {
        _logSink = new List<LogEntry>();
    }

    public LogLevel CurrentLogLevel { get; set; } = LogLevel.Information;

    public bool AreErrorsWarningsInLog()
    {
        lock (_logSinkLock)
        {
            return _logSink.Any(logEntry => logEntry.LogLevel == LogLevel.Warning || logEntry.LogLevel == LogLevel.Error || logEntry.LogLevel == LogLevel.Critical);
        }
    }

    public void ClearLog()
    {
        lock (_logSinkLock)
        {
            _logSink.Clear();
        }
    }

    public IList<LogEntry> GetLogMessages()
    {
        lock (_logSinkLock)
        {
            return new List<LogEntry>(_logSink);
        }
    }

    public bool ContainsMessage(string message)
    {
        lock (_logSinkLock)
        {
            return _logSink.Any(logEntry => logEntry.LogMessage.Contains(message, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    /// <summary>
    /// Returns a value indicating whether warning and errors besides Http warning and Crypto error are in the logs 
    /// </summary>
    /// <returns>true if there are NO warnings/errors/fatal besides Http warning and crypto error; false if there are other warning/errors/fatal</returns>
    public bool IsOnlyHttpUsageAndCryptoExInLog()
    {
        lock (_logSinkLock)
        {
            foreach (var logEntry in _logSink)
            {
                if (logEntry.LogLevel == LogLevel.Warning)
                {
                    if (!logEntry.LogMessage.Contains("uses HTTP transport instead of HTTPS.", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return false;
                    }
                }

                if (logEntry.LogLevel == LogLevel.Error)
                {
                    if (!logEntry.LogMessage.Contains("An error occurred during a cryptographic operation.", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return false;
                    }
                }

                if (logEntry.LogLevel == LogLevel.Critical)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        lock (_logSinkLock)
        {
            _logSink.Add(new LogEntry(logLevel, state.ToString(), null, exception));
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= CurrentLogLevel;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        throw new NotImplementedException();
    }

    public long GetAndResetErrorCount()
    {
        return 0L;
    }
}
