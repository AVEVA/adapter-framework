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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <inheritdoc/>
[ConfigurationFacet(SystemComponentId, BufferingFacetName, "1.1.0", false)]
public class BufferingConfiguration : EdgeConfigurationBase, IBufferingConfiguration
{
    private const int DefaultMaxBufferSize = 1024;
    private const bool DefaultPersistentBufferingState = true;

    /// <inheritdoc/>
    [Required]
    public string BufferLocation { get; set; }

    /// <inheritdoc/>
    [Range(1, int.MaxValue)]
    public int MaxBufferSizeMB { get; set; } = DefaultMaxBufferSize;

    /// <inheritdoc/>
    public bool EnablePersistentBuffering { get; set; } = DefaultPersistentBufferingState;

    /// <inheritdoc/>
    [Description($"MaxDataBulkTime must have a valid timespan format (e.g. hh:mm:ss) and must be between 00:00:01 - 00:10:00")]
    [RegexPattern(PositiveNonZeroTimespanRegexPattern)]
    public TimeSpan MaxDataBulkTime { get; set; } = TimeSpan.FromMilliseconds(DefaultDataBulkTime);

    /// <summary>Get or create buffering configuration.</summary>
    /// <param name="configurationProvider"><see cref="IConfigurationProvider"/>Configuration Provider instance.</param>
    /// <param name="logger"><see cref="ILogger"/>Logger instance.</param>
    /// <returns>Buffering configuration.</returns>
    public static BufferingConfiguration GetOrCreateBufferingConfiguration(IConfigurationProvider configurationProvider, ILogger logger)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));

        if (!configurationProvider.TryGetConfiguration<BufferingConfiguration>(SystemComponentId, BufferingFacetName, out var bufferingConfiguration, out var getErrors))
        {
            bufferingConfiguration = new BufferingConfiguration
            {
                BufferLocation = Path.Combine(configurationProvider.GetCommonApplicationDataDirectoryPath(), BuffersDirectoryName).Replace("\\", "/", StringComparison.InvariantCulture),
            };

            if (getErrors.IsNullOrEmpty())
            {
                if (!configurationProvider.TrySaveConfiguration(SystemComponentId, BufferingFacetName, bufferingConfiguration, out var saveErrors))
                {
                    logger.LogError(FailedToSaveConfigurationMessage, BufferingFacetName, saveErrors);
                }
            }
            else
            {
                logger.LogWarning(ConfigurationInvalidMessage, BufferingFacetName, getErrors);
            }

            _ = bufferingConfiguration.Validate().Any(); // to overcome lazy execution 
        }

        return bufferingConfiguration;
    }

    /// <inheritdoc/>
    public override IEnumerable<ValidationResult> Validate()
    {
        var validationErrors = new List<string>();
        
        ValidateBufferLocation(validationErrors);
        ValidateMaxBufferSizeMb(validationErrors);
        ValidateMaxDataBulkTime(validationErrors);

        foreach (var error in validationErrors)
        {
            yield return new ValidationResult(error);
        }
    }

    private void ValidateMaxBufferSizeMb(List<string> errors)
    {
        if (MaxBufferSizeMB < 1)
        {
            errors.Add($"{nameof(MaxBufferSizeMB)} - invalid buffer size '{MaxBufferSizeMB}' specified. {nameof(MaxBufferSizeMB)} must be a positive number.");
        }
    }

    private void ValidateMaxDataBulkTime(List<string> errors)
    {
        if (MaxDataBulkTime < TimeSpan.FromSeconds(1) || MaxDataBulkTime > TimeSpan.FromMinutes(10))
        {
            errors.Add($"{nameof(MaxDataBulkTime)} must be between 00:00:01 - 00:10:00.");
        }
    }

    private void ValidateBufferLocation(List<string> errors)
    {
        if (BufferLocation.IndexOfAny(Path.GetInvalidPathChars()) != -1)
        {
            errors.Add($"{nameof(BufferLocation)} - invalid characters specified in path");
        }
        else if (!(Path.IsPathFullyQualified(BufferLocation) && Path.IsPathRooted(BufferLocation)))
        {
            errors.Add($"{nameof(BufferLocation)} - full path must be specified");
        }
        else if (!Directory.Exists(BufferLocation))
        {
            try
            {
                Directory.CreateDirectory(BufferLocation);
            }
            catch (IOException inOutException)
            {
                errors.Add($"{nameof(BufferLocation)} - {inOutException.Message}");
            }
            catch (UnauthorizedAccessException unauthorizedAccessException)
            {
                errors.Add($"{nameof(BufferLocation)} - {unauthorizedAccessException.Message}");
            }
            catch (ArgumentException argumentException)
            {
                errors.Add($"{nameof(BufferLocation)} - {argumentException.Message}");
            }
            catch (NotSupportedException notSupportedException)
            {
                errors.Add($"{nameof(BufferLocation)} - {notSupportedException.Message}");
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception exception)
            {
                errors.Add($"{nameof(BufferLocation)} - {exception.Message}");
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }
    }
}
