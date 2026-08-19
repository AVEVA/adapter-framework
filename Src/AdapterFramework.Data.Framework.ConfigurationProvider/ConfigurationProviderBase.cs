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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider;

/// <summary>
/// Configuration provider base class implementation that handles file and directory operations.
/// </summary>
public abstract class ConfigurationProviderBase : IConfigurationProviderBase
{
    #region Private Fields

    protected const string Separator = EdgeSystemConstants.SeparatorUnderscore;
    private const string AdapterFrameworkDirName = EdgeSystemConstants.AdapterFrameworkDirectoryName;
    private const string ConfigurationDirName = EdgeSystemConstants.ConfigurationDirectoryName;
    private const string RemovedDirName = EdgeSystemConstants.RemovedDirectoryName;
    private const string CorruptedFileSuffix = "corrupted";

    /// <summary>
    /// Path to the application base directory.
    /// </summary>
    private static readonly string _baseDirPath = Path.GetDirectoryName(AppContext.BaseDirectory);

    /// <summary>
    /// Path to the common application data directory.
    /// </summary>
    private static readonly string _commonApplicationDataDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>
    /// Name of the common application data directory.
    /// </summary>
    private readonly string _commonApplicationDataDirName;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationProviderBase"/> class.
    /// </summary>
    /// <param name="applicationDataDirectory">Full or relative path to application data directory. Relative path is relative to <see cref="Environment.SpecialFolder.CommonApplicationData"/> directory.</param>
    protected ConfigurationProviderBase(string applicationDataDirectory)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(applicationDataDirectory, nameof(applicationDataDirectory));

        CommonDirPath = !Path.IsPathRooted(applicationDataDirectory)
            ? Path.Combine(_commonApplicationDataDir, AdapterFrameworkDirName, applicationDataDirectory, " ").TrimEnd()
            : Path.Combine(applicationDataDirectory, " ").TrimEnd();

        ConfigDirPath = Path.Combine(CommonDirPath, ConfigurationDirName, " ").TrimEnd();
        RemovedConfigDirPath = Path.Combine(ConfigDirPath, RemovedDirName, " ").TrimEnd();
        _commonApplicationDataDirName = GetCommonApplicationDataDirectoryName(applicationDataDirectory);

        CreateDirectory(ConfigDirPath);
    }

    #endregion

    #region Public Fields

    /// <summary>
    /// Path to the configuration directory.
    /// </summary>
    public string ConfigDirPath { get; }

    /// <summary>
    /// Path to the directory containing the removed configurations.
    /// </summary>
    public string RemovedConfigDirPath { get; }

    /// <summary>
    /// Path to the common directory.
    /// </summary>
    public string CommonDirPath { get; }

    #endregion

    #region Protected Fields

    protected abstract string FileExtension { get; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns path to common application data folder.
    /// </summary>
    /// <param name="componentId">Component ID is going to be appended to the end of common application data folder path when specified.</param>
    /// <returns>Path to common application data folder.</returns>
    public string GetCommonApplicationDataDirectoryPath(string componentId = null)
    {
        if (componentId != null)
        {
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

            return Path.Combine(CommonDirPath, componentId);
        }

        return CommonDirPath;
    }

    /// <inheritdoc/>
    public string GetBaseDirectoryPath() => _baseDirPath;

    /// <inheritdoc/>
    public string GetCommonApplicationDataDirectoryName()
    {
        return _commonApplicationDataDirName;
    }

    /// <inheritdoc/>
    public string GetConfigFilePath(string componentId, string configName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(configName, nameof(configName));

        var fileName = componentId + Separator + configName;
        return Path.Combine(ConfigDirPath, fileName.EndsWith(FileExtension, StringComparison.InvariantCultureIgnoreCase) ? fileName : fileName + FileExtension);
    }

    public string GetCorruptedConfigFilePath(string componentId, string configName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(configName, nameof(configName));

        var fileName = componentId + Separator + configName + Separator + CorruptedFileSuffix + Separator + DateTime.Now.ToString("yyyy-MM-dd--hh-mm-ss", CultureInfo.InvariantCulture);
        return Path.Combine(RemovedConfigDirPath, fileName.EndsWith(FileExtension, StringComparison.InvariantCultureIgnoreCase) ? fileName : fileName + FileExtension);
    }

    /// <inheritdoc/>
    public void DeleteConfiguration(string componentId, string configName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(configName, nameof(configName));

        var path = GetConfigFilePath(componentId, configName);
        File.Delete(path);
    }

    #endregion

    #region Protected Methods

    /// <summary>
    /// Returns collection of validation results from <see cref="IValidatableObject"/> Validate operation.
    /// </summary>
    /// <typeparam name="T">Configuration type.</typeparam>
    /// <param name="config">Configuration object to validate.</param>
    /// <param name="errors">Results from <see cref="IValidatableObject"/> Validate operation on top of <paramref name="config"/>.</param>
    protected static void GetValidationErrors<T>(T config, ref ICollection<string> errors)
        where T : class
    {
        errors ??= new List<string>();

        if (config is IEnumerable collection)
        {
            foreach (var item in collection)
            {
                try
                {
                    AddValidationErrors(item, ref errors);
                }
                catch (Exception ex)
                {
                    errors.Add(ex.GetExceptionTypeAndMessages());
                }
            }
        }
        else if (config is IValidatableObject)
        {
            try
            {
                AddValidationErrors(config, ref errors);
            }
            catch (Exception ex)
            {
                errors.Add(ex.GetExceptionTypeAndMessages());
            }
        }
    }

    #endregion

    #region Private Methods

    private static string GetCommonApplicationDataDirectoryName(string applicationDataDirectory)
    {
        if (applicationDataDirectory.EndsWith(EdgeSystemConstants.SeparatorForwardSlash, StringComparison.InvariantCulture))
        {
            applicationDataDirectory = applicationDataDirectory.TrimEnd(EdgeSystemConstants.SeparatorSlashCharacter);
        }

        return applicationDataDirectory[(applicationDataDirectory.LastIndexOf(EdgeSystemConstants.SeparatorSlashCharacter) + 1)..];
    }

    private static void AddValidationErrors(object config, ref ICollection<string> errors)
    {
        if (config is IValidatableObject)
        {
            var validationResults = new List<ValidationResult>();
            Validator.TryValidateObject(config, new ValidationContext(config), validationResults, true);
            if (!validationResults.IsNullOrEmpty())
            {
                foreach (var validationResult in validationResults)
                {
                    errors.Add(validationResult.ErrorMessage);
                }
            }
        }
    }

    private static void CreateDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    #endregion
}
