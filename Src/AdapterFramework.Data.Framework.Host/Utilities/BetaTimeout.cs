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

namespace AdapterFramework.Data.Framework.Host.Utilities;

/// <summary>
/// Provides properties and functions related to setting the adapter beta timeout.
/// </summary>
public static class BetaTimeout
{
    // Should be the next first Wednesday in either June or December
    private static readonly DateTime _timeoutDate = new(2026, 12, 2);

    /// <summary>
    /// Returns true if expired, false if valid.
    /// Logs a message saying the Edge System beta has expired if expired.
    /// Logs a message saying when the Edge System beta will expire, if it hasn't expired.
    /// </summary>
    /// <param name="betaTimeoutDate">Beta timeout date supplied by Adapter developer.</param>
    /// <param name="logger">Edge logger.</param>
    /// <returns>True if expired, false if valid.</returns>
    public static bool HasExpiredAndLogTimeoutMessage(DateTime? betaTimeoutDate, ILogger logger = null)
    {
        var timeoutDate = _timeoutDate;
        if (betaTimeoutDate != null)
        {
            timeoutDate = betaTimeoutDate.Value;
        }

        var expired = DateTime.Now > timeoutDate;
        if (logger != null)
        {
            logger.Log(expired ? LogLevel.Critical : LogLevel.Warning, GetTimeoutMessage(timeoutDate, expired));
        }
        else
        {
            Console.WriteLine(GetTimeoutMessage(timeoutDate, expired));
        }

        return expired;
    }

    /// <summary>
    /// Returns timeout date.
    /// </summary>
    /// <returns>The beta timeout date.</returns>
    public static DateTime GetTimeoutDate()
    {
        return _timeoutDate;
    }

    private static string GetTimeoutMessage(DateTime expirationDate, bool expired)
    {
        var state = expired ? "expired" : "expires";
        return $"Beta license {state} on {expirationDate.DayOfWeek}, {expirationDate:yyyy MMMM dd}. " +
               "Please upgrade to a released version.";
    }
}
