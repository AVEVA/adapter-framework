// Copyright 2026 AVEVA Group Limited
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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using Microsoft.Extensions.Logging;
using Moq;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;

/// <summary>
/// Shared logger test helpers. Centralizes the mock-logger creation and the (verbose, easy-to-get-wrong)
/// Moq <c>ILogger.Log</c> verification expression so every test class asserts logging the same way.
/// </summary>
internal static class MockLoggerHelpers
{
    /// <summary>
    /// Creates a mock <see cref="ILogger"/> with <see cref="ILogger.IsEnabled"/> returning true, so
    /// source-generated log methods (which gate on IsEnabled) actually emit and can be verified.
    /// </summary>
    public static Mock<ILogger> CreateLogger()
    {
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return logger;
    }

    /// <summary>
    /// Creates a mock logger (as <see cref="CreateLogger"/>) wired through a mock <see cref="ILogManager"/>,
    /// matching how the adapter resolves its logger during the lifecycle.
    /// </summary>
    public static (Mock<ILogger> Logger, Mock<ILogManager> LogManager) CreateLoggerWithManager()
    {
        var logger = CreateLogger();
        var logManager = new Mock<ILogManager>();
        logManager
            .Setup(m => m.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>()))
            .Returns(logger.Object);
        return (logger, logManager);
    }

    /// <summary>
    /// Verifies a log entry at <paramref name="level"/> whose formatted message contains
    /// <paramref name="contains"/> was written the expected number of times. An empty substring matches
    /// every entry, doubling as a "nothing was logged at this level" check.
    /// </summary>
    public static void VerifyLog(Mock<ILogger> logger, LogLevel level, string contains, Times times) =>
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString().Contains(contains)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            times);

    /// <summary>
    /// Verifies a <see cref="LogLevel.Warning"/> entry whose formatted message contains
    /// <paramref name="contains"/> was written the expected number of times.
    /// </summary>
    public static void VerifyWarning(Mock<ILogger> logger, string contains, Times times) =>
        VerifyLog(logger, LogLevel.Warning, contains, times);
}
