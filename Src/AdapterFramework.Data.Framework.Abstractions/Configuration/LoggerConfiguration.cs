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
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Constants;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <inheritdoc />
/// <summary>
/// Defines Edge System logging configuration.
/// </summary>
[ConfigurationFacet(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.LoggingFacetName, "1.0.0", false)]
public class LoggerConfiguration : EdgeConfigurationBase
{
    public const long MinLogFileSizeLimitBytes = 1000;
    public const string ConfigName = EdgeSystemConstants.LoggingFacetName;
    private const long DefaultLogFileSizeLimitBytes = 1073741824 / 31;
    private const int DefaultLogFileCountLimit = 31;

    public LoggerConfiguration()
    {
        LogLevel = LogLevel.Information;
        LogFileSizeLimitBytes = DefaultLogFileSizeLimitBytes;
        LogFileCountLimit = DefaultLogFileCountLimit;
    }

    /// <summary>
    /// Log level settings.
    /// </summary>
    [EnumDataType(typeof(LogLevel))]
    public LogLevel LogLevel { get; set; }

    /// <summary>
    /// Maximum log file size in bytes.
    /// </summary>
    [Range(MinLogFileSizeLimitBytes, long.MaxValue)]
    public long? LogFileSizeLimitBytes { get; set; }

    /// <summary>
    /// Maximum count of log files stored.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? LogFileCountLimit { get; set; }

    public override IEnumerable<ValidationResult> Validate()
    {
        yield return ValidationResult.Success;
    }
}
