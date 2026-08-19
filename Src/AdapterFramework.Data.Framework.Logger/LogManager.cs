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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Logger;

public class LogManager : ILogManager
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, DataFrameworkLogger> _createdLoggers = new ConcurrentDictionary<string, DataFrameworkLogger>();
    private readonly IConfigurationProvider _configurationProvider;

    #endregion

    #region Public Constructor

    public LogManager(IConfigurationProvider configurationProvider)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));

        _configurationProvider = configurationProvider;
        LogsFolderPath = Path.Combine(_configurationProvider.GetCommonApplicationDataDirectoryPath(), EdgeSystemConstants.LoggingDirectoryName, " ").TrimEnd();
    }

    #endregion

    #region Public Properties

    public string LogsFolderPath { get; private set; }

    #endregion

    #region Public Methods

    public ILogger GetOrCreateLogger(string logSourceName, LoggerConfiguration logConfiguration = null)
    {
        if (TryGetLogger(logSourceName, out var logger))
        {
            return logger;
        }

        logConfiguration = GetLoggerConfiguration(logSourceName, logConfiguration, out var errors);
        logger = new DataFrameworkLogger(logSourceName, logConfiguration, LogsFolderPath);
        _createdLoggers.TryAdd(logSourceName, logger);
        if (!errors.IsNullOrEmpty())
        {
            logger.LogWarning(EdgeSystemConstants.ConfigurationInvalidMessage, LoggerConfiguration.ConfigName, errors);
        }

        return logger;
    }

    public IInstrumentedLogger GetOrCreateInstrumentedLogger(string logSourceName, LoggerConfiguration logConfiguration = null)
    {
        if (TryGetLogger(logSourceName, out var logger))
        {
            return logger;
        }

        logConfiguration = GetLoggerConfiguration(logSourceName, logConfiguration, out var errors);

        logger = new DataFrameworkLogger(logSourceName, logConfiguration, LogsFolderPath, true);
        _createdLoggers.TryAdd(logSourceName, logger);
        if (!errors.IsNullOrEmpty())
        {
            logger.LogWarning(EdgeSystemConstants.ConfigurationInvalidMessage, LoggerConfiguration.ConfigName, errors);
        }

        return logger;
    }

    public void RemoveLogger(string logSourceName)
    {
        if (logSourceName != null && _createdLoggers.TryRemove(logSourceName, out var logger))
        {
            logger?.Dispose();
        }
    }

    public ILoggerConfigurator GetLoggerConfigurator(string logSourceName)
    {
        if (TryGetLogger(logSourceName, out var logger))
        {
            return logger;
        }
        else
        {
            throw new InvalidOperationException($"Logger {logSourceName} must be created before getting its log configurator.");
        }
    }

    #endregion

    #region Private Methods

    private bool TryGetLogger(string logSourceName, out DataFrameworkLogger logger)
    {
        if (_createdLoggers.TryGetValue(logSourceName, out logger))
        {
            return true;
        }

        return false;
    }

    private LoggerConfiguration GetLoggerConfiguration(string logSourceName, LoggerConfiguration loggerConfiguration, out ICollection<string> errors)
    {
        errors = null;
        if (loggerConfiguration == null)
        {
            _configurationProvider.TryGetConfiguration(logSourceName, LoggerConfiguration.ConfigName, out loggerConfiguration, out errors);
            if (loggerConfiguration == null)
            {
                loggerConfiguration = new LoggerConfiguration();
                
                // Only save the configuration if it does not exist. Invalid configuration will not be overwritten to preserve user data. 
                if (errors.IsNullOrEmpty())
                {
                    _configurationProvider.TrySaveConfiguration(logSourceName, LoggerConfiguration.ConfigName, loggerConfiguration, out _);
                }
            }
        }

        return loggerConfiguration;
    }

    #endregion
}
