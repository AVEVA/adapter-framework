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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.Abstractions.Logging;

/// <summary>
/// Log manager that provides custom instance of a logger (custom source name and path to rolling file).
/// </summary>
/// <remarks>We could potentially add multiple constructors to write into multiple sinks.</remarks>>
public interface ILogManager
{
    /// <summary>
    /// Gets the folder path for the logs.
    /// </summary>
    string LogsFolderPath { get; }

    /// <summary>
    /// Gets or creates <see cref="ILogger"/> instance with specified <see paramref="logSourceName"/>
    /// and optionally <see paramref="logConfiguration"/>.
    /// </summary>
    /// <param name="logSourceName">Name of a log source.</param>
    /// <param name="logConfiguration">Optional <see cref="LoggerConfiguration"/> to configure the logger.</param>
    /// <returns>New or existing <see cref="ILogger"/> instance.</returns>
    ILogger GetOrCreateLogger(string logSourceName, LoggerConfiguration logConfiguration = null);

    /// <summary>
    /// Gets or creates <see cref="IInstrumentedLogger"/> instance with specified <see paramref="logSourceName"/>
    /// and optionally <see paramref="logConfiguration"/>.
    /// </summary>
    /// <param name="logSourceName">Name of a log source.</param>
    /// <param name="logConfiguration">Optional <see cref="LoggerConfiguration"/> to configure the logger.</param>
    /// <returns>New or existing <see cref="ILogger"/> instance.</returns>
    IInstrumentedLogger GetOrCreateInstrumentedLogger(string logSourceName, LoggerConfiguration logConfiguration = null);

    /// <summary>
    /// Removes and disposes existing instance of <see cref="ILogger"/> from collection of available loggers in <see cref="ILogManager"/>.
    /// </summary>
    /// <param name="logSourceName">Name of the logger instance to remove.</param>
    void RemoveLogger(string logSourceName);

    /// <summary>
    /// Gets the configurator for a logger.
    /// </summary>
    /// <param name="logSourceName">The logger to get a configurator for.</param>
    /// <returns>The configurator for the log source.</returns>
    ILoggerConfigurator GetLoggerConfigurator(string logSourceName);
}
