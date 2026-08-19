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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Extensions;
using Xunit;

namespace AdapterFramework.Data.Framework.Logger.Tests;

[Collection("Sequential")]
public class DataFrameworkLogger_Tests
{
    private const int MinimumMessagesPerSecond = 1000;
    private const int MessageLoopCount = 10000;
    private const string NormalLogSource = "NormalString";
    private const string TestMessage =
        "Testing 123...  Testing 123...  Testing 123...  Testing 123...  Testing 123...  Testing 123...  Testing 123...  Testing 123...  ";
    private const string Prefix = "Discovery with ID: 12345:";
    private const string SmallText = "Hello";

    private readonly IConfigurationProvider _configurationProvider = new JsonConfigurationProvider("UnitTest");

    private delegate void DelegateLogMessage(DataFrameworkLogger logger, string msg);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Logger_CreateLoggerWithBadArgumentThrows(string logSource)
    {
        var logConfiguration = new LoggerConfiguration();
        Assert.ThrowsAny<ArgumentException>(() => new DataFrameworkLogger(logSource, logConfiguration, GetLoggerDirectoryPath()));
    }

    [Fact]
    public void Logger_CreateLoggerSuccess()
    {
        var logConfiguration = new LoggerConfiguration();
        using var myLog = new DataFrameworkLogger(NormalLogSource, logConfiguration, GetLoggerDirectoryPath());

        Assert.Equal(NormalLogSource, myLog.LogSource);
    }

    [Fact]
    public void Logger_BeginScope()
    {
        var logConfiguration = new LoggerConfiguration();
        string folder = null;

        try
        {
            using (var myLog = new DataFrameworkLogger(NormalLogSource, logConfiguration, GetLoggerDirectoryPath()))
            {
                using (myLog.BeginScope(new KeyValuePair<string, object>(EdgeSystemConstants.LogMessagePrefixPlaceholder, Prefix)))
                {
                    myLog.LogError(SmallText);
                }

                myLog.LogError(SmallText);
                folder = myLog.LogsFolder;
            }

            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            var loggedText = File.ReadAllText(testLogFile);

            Assert.Equal(2, Regex.Matches(loggedText, SmallText).Count);
            Assert.Single(Regex.Matches(loggedText, Prefix));
        }
        finally
        {
            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            if (testLogFile != null)
            {
                File.Delete(testLogFile);
            }
        }
    }

    [Fact]
    public void Logger_BeginScopeEnumerable()
    {
        var logConfiguration = new LoggerConfiguration();
        string folder = null;

        try
        {
            using (var myLog = new DataFrameworkLogger(NormalLogSource, logConfiguration, GetLoggerDirectoryPath()))
            {
                var states = new Dictionary<string, object>()
                {
                    [EdgeSystemConstants.LogMessagePrefixPlaceholder] = Prefix,
                };

                using (myLog.BeginScope(states))
                {
                    myLog.LogError(SmallText);
                }

                myLog.LogError(SmallText);
                folder = myLog.LogsFolder;
            }

            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            var loggedText = File.ReadAllText(testLogFile);

            Assert.Equal(2, Regex.Matches(loggedText, SmallText).Count);
            Assert.Single(Regex.Matches(loggedText, Prefix));
        }
        finally
        {
            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            if (testLogFile != null)
            {
                File.Delete(testLogFile);
            }
        }
    }

    [Fact]
    public async Task Logger_BeginScopeNoPrefixInConcurrentTask()
    {
        var logConfiguration = new LoggerConfiguration();
        string folder = null;

        try
        {
            using (var myLog = new DataFrameworkLogger(NormalLogSource, logConfiguration, GetLoggerDirectoryPath()))
            {
                var states = new Dictionary<string, object>()
                {
                    [EdgeSystemConstants.LogMessagePrefixPlaceholder] = Prefix,
                };

                var task = Task.Run(async () =>
                  {
                      for (int i = 0; i < 10; i++)
                      {
                          myLog.LogError(SmallText);
                          await Task.Delay(TimeSpan.FromSeconds(.2));
                      }
                  });

                await Task.Delay(TimeSpan.FromSeconds(.1));

                using (myLog.BeginScope(states))
                {
                    myLog.LogError(SmallText);
                    await Task.Run(() =>
                    {
                        myLog.LogError(SmallText);
                    });
                    await Task.Delay(TimeSpan.FromSeconds(.8));
                }

                await task;

                folder = myLog.LogsFolder;
            }

            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            var loggedText = await File.ReadAllTextAsync(testLogFile);

            Assert.Equal(12, Regex.Matches(loggedText, SmallText).Count);
            Assert.Equal(2, Regex.Matches(loggedText, Prefix).Count);
        }
        finally
        {
            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            if (testLogFile != null)
            {
                File.Delete(testLogFile);
            }
        }
    }

    [Fact]
    public void Logger_BeginScopeMultiplePairs_AllDisposed()
    {
        var logConfiguration = new LoggerConfiguration();
        string folder = null;

        try
        {
            var listStates = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>(EdgeSystemConstants.LogMessagePrefixPlaceholder, Prefix),
                new KeyValuePair<string, object>("Suffix", "birbs"),
            };

            using (var myLog = new DataFrameworkLogger(NormalLogSource, logConfiguration, GetLoggerDirectoryPath()))
            {
                using (myLog.BeginScope(listStates))
                {
                    myLog.LogError(SmallText);
                }

                myLog.LogError(SmallText);
                folder = myLog.LogsFolder;
            }

            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            var loggedText = File.ReadAllText(testLogFile);

            Assert.Equal(2, Regex.Matches(loggedText, SmallText).Count);
            Assert.Single(Regex.Matches(loggedText, Prefix));

            File.Delete(testLogFile);

            using (var myLog = new DataFrameworkLogger(NormalLogSource, logConfiguration, GetLoggerDirectoryPath()))
            {
                // invert the list to make sure order doesn't matter.
                listStates.Reverse();

                using (myLog.BeginScope(listStates))
                {
                    myLog.LogError(SmallText);
                }

                myLog.LogError(SmallText);
            }

            loggedText = File.ReadAllText(testLogFile);

            Assert.Equal(2, Regex.Matches(loggedText, SmallText).Count);
            Assert.Single(Regex.Matches(loggedText, Prefix));
        }
        finally
        {
            var logFiles = Directory.GetFiles(folder);
            var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(NormalLogSource, StringComparison.InvariantCulture));
            if (testLogFile != null)
            {
                File.Delete(testLogFile);
            }
        }
    }

    [Fact]
    public void Logger_WriteInformationFasterThan1000LogMessagesPerSec()
    {
        LoggerTest("Info", TestMessage, MessageLoopCount, new DelegateLogMessage(LogInformation), MinimumMessagesPerSecond);
    }

    [Fact]
    public void Logger_LogLevelFasterThan1000LogMessagesPerSec()
    {
        LoggerTest("Debug", TestMessage, MessageLoopCount, new DelegateLogMessage(LogDebug), MinimumMessagesPerSecond);
    }

    [Fact]
    public void Logger_LogWarningsFasterThan1000LogMessagesPerSec()
    {
        LoggerTest("Warning", TestMessage, MessageLoopCount, new DelegateLogMessage(LogWarning), MinimumMessagesPerSecond);
    }

    [Fact]
    public void Logger_LogErrorsFasterThan1000LogMessagesPerSec()
    {
        LoggerTest("Error", TestMessage, MessageLoopCount, new DelegateLogMessage(LogError), MinimumMessagesPerSecond);
    }

    [Fact]
    public void Logger_WriteExceptionsFasterThan1000LogMessagesPerSec()
    {
        LoggerTest("Exception", TestMessage, MessageLoopCount, new DelegateLogMessage(LogException), MinimumMessagesPerSecond);
    }

    [Fact]
    public void Logger_WriteCriticalFasterThan1000LogMessagesPerSec()
    {
        LoggerTest("Exception", TestMessage, MessageLoopCount, new DelegateLogMessage(LogCritical), MinimumMessagesPerSecond);
    }

    [Fact]
    public void Logger_WriteVerboseFasterThan1000LogMessagesPerSec()
    {
        LoggerTest("Verbose", TestMessage, MessageLoopCount, new DelegateLogMessage(LogTrace), MinimumMessagesPerSecond);
    }

    [Fact]
    public void Logger_WriteException_AggregateException_Test()
    {
        var logConfiguration = new LoggerConfiguration();
        var logSource = "UnitTestLog_AggregateException";
        var firstMessage = "One or more errors occurred.";
        var lastMessage = "Administrator permissions are required.";
        var logsFolder = string.Empty;

        var aggregateException = new AggregateException(
            firstMessage,
            new AggregateException("Even more errors occurred", new FormatException("Number isn't in correct format.")),
            new IOException("Unauthorized file access.", new SecurityException(lastMessage)));

        using (var testLogger = new DataFrameworkLogger(logSource, logConfiguration, GetLoggerDirectoryPath()))
        {
            testLogger.LogError(aggregateException, "Testing exception write behavior.");
            logsFolder = testLogger.LogsFolder;
        }

        var logFiles = Directory.GetFiles(logsFolder);

        var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(logSource, StringComparison.InvariantCulture));

        Assert.False(string.IsNullOrEmpty(testLogFile));

        var loggedText = File.ReadAllText(testLogFile);

#if DEBUG
        Assert.Contains("stack trace", loggedText, StringComparison.InvariantCulture);
        Assert.Contains(firstMessage, loggedText, StringComparison.InvariantCulture);
        Assert.Contains(lastMessage, loggedText, StringComparison.InvariantCulture);
#else
        Assert.DoesNotContain("stack trace", loggedText, StringComparison.InvariantCulture);
        Assert.Contains(firstMessage, loggedText, StringComparison.InvariantCulture);
        Assert.Contains(lastMessage, loggedText, StringComparison.InvariantCulture);
#endif
        File.Delete(testLogFile);
    }

    [Fact]
    public void Logger_WriteException_InnerException_Test()
    {
        var logConfiguration = new LoggerConfiguration();
        var logSource = "UnitTestLog_InnerException";
        var firstMessage = "Unauthorized file access.";
        var lastMessage = "Administrator permissions are required.";
        var logsFolder = string.Empty;

        var exception = new IOException(firstMessage, new SecurityException(lastMessage));

        using (var testLogger = new DataFrameworkLogger(logSource, logConfiguration, GetLoggerDirectoryPath()))
        {
            testLogger.LogError(exception, "Testing exception write behavior.");
            logsFolder = testLogger.LogsFolder;
        }

        var logFiles = Directory.GetFiles(logsFolder);

        var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(logSource, StringComparison.InvariantCulture));

        Assert.False(string.IsNullOrEmpty(testLogFile));

        var loggedText = File.ReadAllText(testLogFile);

#if DEBUG
        Assert.Contains("stack trace", loggedText, StringComparison.InvariantCulture);
        Assert.Contains(firstMessage, loggedText, StringComparison.InvariantCulture);
        Assert.Contains(lastMessage, loggedText, StringComparison.InvariantCulture);
#else
        Assert.DoesNotContain("stack trace", loggedText, StringComparison.InvariantCulture);
        Assert.Contains(firstMessage, loggedText, StringComparison.InvariantCulture);
        Assert.Contains(lastMessage, loggedText, StringComparison.InvariantCulture);
#endif
        File.Delete(testLogFile);
    }

    [Fact]
    public void Logger_WriteException_ExceptionMessage_Test()
    {
        var logConfiguration = new LoggerConfiguration();
        var logSource = "UnitTestLog_ExceptionMessage";
        var logsFolder = string.Empty;

        Exception exception = null;
        try
        {
            ThrowHelper.ThrowIfArgumentNull(null, logSource);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        using (var testLogger = new DataFrameworkLogger(logSource, logConfiguration, GetLoggerDirectoryPath()))
        {
            testLogger.LogError(exception, "Testing exception write behavior.");
            logsFolder = testLogger.LogsFolder;
        }

        var logFiles = Directory.GetFiles(logsFolder);

        var testLogFile = logFiles.FirstOrDefault(logFile => logFile.Contains(logSource, StringComparison.InvariantCulture));

        Assert.False(string.IsNullOrEmpty(testLogFile));

        var loggedText = File.ReadAllText(testLogFile);

#if DEBUG
        Assert.Contains(":line", loggedText, StringComparison.InvariantCulture);
        Assert.Contains(logSource, loggedText, StringComparison.InvariantCulture);
#else
        Assert.DoesNotContain(":line", loggedText, StringComparison.InvariantCulture);
        Assert.Contains(logSource, loggedText, StringComparison.InvariantCulture);
#endif
        File.Delete(testLogFile);
    }

    private void LogDebug(DataFrameworkLogger logger, string message)
    {
        logger.LogDebug(message);
    }

    private void LogCritical(DataFrameworkLogger logger, string message)
    {
        logger.LogCritical(message);
    }

    private void LogError(DataFrameworkLogger logger, string message)
    {
        logger.LogError(message);
    }

    private void LogException(DataFrameworkLogger logger, string message)
    {
        logger.LogError(new Exception(message), message);
    }

    private void LogInformation(DataFrameworkLogger logger, string message)
    {
        logger.LogInformation(message);
    }

    private void LogTrace(DataFrameworkLogger logger, string message)
    {
        logger.LogTrace(message);
    }

    private void LogWarning(DataFrameworkLogger logger, string message)
    {
        logger.LogWarning(message);
    }

    private void LoggerTest(string filenamePrefix, string testMessage, int messageLoopCount, DelegateLogMessage callBackAction, double minimumExpectedMessagesPerSecond)
    {
        var logConfiguration = new LoggerConfiguration();
        var logSource = $"{filenamePrefix}-Performance";
        using var myLog = new DataFrameworkLogger(logSource, logConfiguration, GetLoggerDirectoryPath());
        myLog.SetMinimumLogLevel(0);

        var sw = Stopwatch.StartNew();
        myLog.LogInformation($"Starting Performance Test of {filenamePrefix}: {messageLoopCount} lines are to be written, excluding first and last lines.");
        for (int n = 0; n < messageLoopCount; n++)
        {
            callBackAction(myLog, testMessage);
        }

        var elapsed = sw.ElapsedMilliseconds;
        var messagesPerSec = 1000.0 * (double)messageLoopCount / (double)elapsed;
        callBackAction(myLog, $"Messages Per Second: {messagesPerSec:F2} in {elapsed} milliseconds.");

        Assert.True(messagesPerSec > minimumExpectedMessagesPerSecond);
    }

    private string GetLoggerDirectoryPath() => Path.Combine(_configurationProvider.GetCommonApplicationDataDirectoryPath(), EdgeSystemConstants.LoggingDirectoryName, " ").TrimEnd();
}
