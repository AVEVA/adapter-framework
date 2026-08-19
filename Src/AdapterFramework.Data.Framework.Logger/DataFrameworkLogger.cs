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
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Extensions;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;

namespace AdapterFramework.Data.Framework.Logger;

public class DataFrameworkLogger : IInstrumentedLogger, IDisposable, ILoggerConfigurator
{
    #region Private Constants

    private const string DefaultFileExtension = "-.txt";
    private const int FlushToDiskIntervalSeconds = 1;
    private const string Scope = "Scope";

    private const string FileMessageOutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Prefix}{Message}{NewLine}{Exception}";
    private const string ConsoleMessageOutputTemplateStart = "[{Timestamp:HH:mm:ss} {Level:u3}] [";
    private const string ConsoleMessageOutputTemplateEnd = "] {Prefix}{Message:lj}{NewLine}{Exception}";
#if DEBUG
    private const string ExceptionWithStackTraceTemplate = "{Reason} Exception type: {ExceptionType}. Exception: {Exception}";
#else
    private const string ExceptionMessageTemplate = "{Reason} {ExceptionMessages}";
#endif
    #endregion

    #region Private Fields

    private static readonly object _directoryCreateLock = new object();
    private readonly AdapterFramework.Data.Framework.Abstractions.Configuration.LoggerConfiguration _configuration;
    private readonly LoggingLevelSwitch _levelSwitch = new LoggingLevelSwitch();
    private readonly bool _isInstrumented;
    private long _errorCount;
    private Serilog.Core.Logger _logger;
    private bool _disposed;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DataFrameworkLogger"/> class.
    /// </summary>
    /// <param name="logSource">Logger source name.</param>
    /// <param name="logConfiguration">Tells how to initially configure the logger (level, max file size, max # files).</param>
    /// <param name="logsFolderPath">Path to a directory where log files should be created.</param>
    /// <param name="isInstrumented">When True the logger collects statistics about number of errors written for the component.</param>
    public DataFrameworkLogger(string logSource, AdapterFramework.Data.Framework.Abstractions.Configuration.LoggerConfiguration logConfiguration, string logsFolderPath, bool isInstrumented = false)
    {
        Debug.Assert(ValidateLogLevelEnum(out var error), $"Serilog enum values differ from Edge enum values: {error}.");
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(logSource, nameof(logSource));

        _configuration = logConfiguration;
        _isInstrumented = isInstrumented;

        // Set interactive flag based on the fact that output console is redirected.
        var runsInteractively = !Console.IsOutputRedirected;

        LogsFolder = logsFolderPath;
        LogSource = logSource;

        CreateLogger(runsInteractively);
    }

    #endregion

    #region Public Fields

    public string LogSource { get; }

    public string LogsFolder { get; }

    #endregion

    #region Public Methods

    #region IInstrumentedLogger Implementation

    public long GetAndResetErrorCount()
    {
        return Interlocked.Exchange(ref _errorCount, 0);
    }

    #endregion

    #region ILogger Implementation

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (exception != null)
        {
            WriteException(state?.ToString(), exception, logLevel);
            return;
        }

        switch (logLevel)
        {
            case LogLevel.Debug:
                WriteDebug(formatter != null ? formatter(state, exception) : state.ToString());
                break;
            case LogLevel.Trace:
                WriteVerbose(formatter != null ? formatter(state, exception) : state.ToString());
                break;
            case LogLevel.Information:
                WriteInformation(formatter != null ? formatter(state, exception) : state.ToString());
                break;
            case LogLevel.Warning:
                WriteWarning(formatter != null ? formatter(state, exception) : state.ToString());
                break;
            case LogLevel.Error:
                WriteError(formatter != null ? formatter(state, exception) : state.ToString());
                break;
            case LogLevel.Critical:
                WriteFatal(formatter != null ? formatter(state, exception) : state.ToString());
                break;
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _configuration.LogLevel;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        if (state is IEnumerable<KeyValuePair<string, object>> enumerable)
        {
            var multiScope = new MultiScope();

            foreach (var (key, value) in enumerable)
            {
                multiScope.AddDispose(LogContext.PushProperty(key, value, true));
            }

            return multiScope;
        }

        if (state is KeyValuePair<string, object> item)
        {
            return LogContext.PushProperty(item.Key, item.Value, true);
        }

        return LogContext.PushProperty(Scope, state.ToString());
    }

    #endregion

    #region ILoggerConfigurator Implementation

    public void SetMinimumLogLevel(LogLevel logLevel)
    {
        _levelSwitch.MinimumLevel = (LogEventLevel)logLevel;
        _configuration.LogLevel = logLevel;
    }

    #endregion

    #region IDisposable Implementation

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #endregion

    #region Protected Methods

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger?.Dispose();
            _logger = null;
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private static bool ValidateLogLevelEnum(out string error)
    {
        error = null;

        var logLevels = (LogLevel[])Enum.GetValues(typeof(LogLevel));
        var expectedLogLevels = (LogEventLevel[])Enum.GetValues(typeof(LogEventLevel));

        if (expectedLogLevels.Length + 1 != logLevels.Length)
        {
            error = "Invalid enum length.";
            return false;
        }

        for (var i = 0; i < expectedLogLevels.Length; i++)
        {
            if ((int)logLevels[i] != (int)expectedLogLevels[i])
            {
                error = $"Invalid enum value for '{logLevels[i]}' state. Expected: '{expectedLogLevels[i]}' Actual: '{logLevels[i]}'";
                return false;
            }
        }

        return true;
    }

    private static void CreateDirectory(string directoryPath)
    {
        lock (_directoryCreateLock)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
    }

    private void IncrementErrorsCounter()
    {
        if (_isInstrumented)
        {
            Interlocked.Increment(ref _errorCount);
        }
    }

    private void CreateLogger(bool runsInteractively)
    {
        CreateDirectory(LogsFolder);

        _logger = CreateLogger(LogSource, runsInteractively);
    }

    private Serilog.Core.Logger CreateLogger(string logSource, bool runsInteractively)
    {
        _levelSwitch.MinimumLevel = (LogEventLevel)_configuration.LogLevel;

        if (runsInteractively)
        {
            return new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.File(LogsFolder + logSource + DefaultFileExtension, LogEventLevel.Verbose,
                    FileMessageOutputTemplate, rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: _configuration.LogFileSizeLimitBytes,
                    flushToDiskInterval: TimeSpan.FromSeconds(FlushToDiskIntervalSeconds),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: _configuration.LogFileCountLimit,
                    formatProvider: CultureInfo.InvariantCulture)
                .MinimumLevel.ControlledBy(_levelSwitch)
                .WriteTo.Console(outputTemplate: ConsoleMessageOutputTemplateStart + logSource + ConsoleMessageOutputTemplateEnd,
                    formatProvider: CultureInfo.InvariantCulture)
                .CreateLogger();
        }

        return new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.File(LogsFolder + logSource + DefaultFileExtension, LogEventLevel.Verbose,
                FileMessageOutputTemplate, rollOnFileSizeLimit: true,
                fileSizeLimitBytes: _configuration.LogFileSizeLimitBytes,
                flushToDiskInterval: TimeSpan.FromSeconds(FlushToDiskIntervalSeconds),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: _configuration.LogFileCountLimit,
                formatProvider: CultureInfo.InvariantCulture)
            .MinimumLevel.ControlledBy(_levelSwitch)
            .CreateLogger();
    }

    private void WriteVerbose(string message, params object[] args)
    {
        _logger?.Write(LogEventLevel.Verbose, message, args);
    }

    private void WriteDebug(string message, params object[] args)
    {
        _logger?.Write(LogEventLevel.Debug, message, args);
    }

    private void WriteInformation(string message, params object[] args)
    {
        _logger?.Write(LogEventLevel.Information, message, args);
    }

    private void WriteWarning(string message, params object[] args)
    {
        _logger?.Write(LogEventLevel.Warning, message, args);
    }

    private void WriteError(string message, params object[] args)
    {
        _logger?.Write(LogEventLevel.Error, message, args);

        IncrementErrorsCounter();
    }

    private void WriteFatal(string message, params object[] args)
    {
        _logger?.Write(LogEventLevel.Fatal, message, args);
    }

    private void WriteException(string reason, Exception exception, LogLevel logLevel = LogLevel.Error)
    {
#if DEBUG
        _logger?.Write((LogEventLevel)logLevel, ExceptionWithStackTraceTemplate, reason, exception.GetType(), exception);
#else
        _logger?.Write((LogEventLevel)logLevel, ExceptionMessageTemplate, reason, exception.GetExceptionTypeAndMessages());
#endif
        if (logLevel == LogLevel.Error)
        {
            IncrementErrorsCounter();
        }
    }

    #endregion
}
