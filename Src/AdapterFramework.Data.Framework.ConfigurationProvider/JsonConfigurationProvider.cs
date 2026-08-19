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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.ConfigurationProvider.Converters;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider;

/// <summary>
/// Implementation of <see cref="IConfigurationProvider"/> interface that uses JSON serialization format.
/// </summary>
public class JsonConfigurationProvider : ConfigurationProviderBase, IConfigurationProvider
{
    #region Private Constants

    private const string ErrorLoadingConfigurationString = "Error encountered while loading configuration: {0}. {1}";
    private const string ErrorSavingConfigurationString = "Error encountered while saving configuration: {0}. {1}";
    private const string DiscoveryResultFileKeyword = "Discovery";
    private const string ErrorMovingCorruptedConfigurationString = "Error encountered while moving corrupted configuration: {0}. {1}";

    #endregion

    #region Private Fields

    private readonly JsonSerializerOptions _serializerOptions;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonConfigurationProvider"/> class.
    /// </summary>
    /// <param name="applicationDataDirectory">Directory structure that should be created in CommonApplicationData directory.</param>
    public JsonConfigurationProvider(string applicationDataDirectory)
        : base(applicationDataDirectory)
    {
        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new FlagEnumConverter<StreamProperties>(() => StreamProperties.All), new JsonStringEnumConverter(), new StringToTimeSpanConverter() },
        };
    }

    #endregion

    #region Protected Fields

    /// <inheritdoc/>
    protected override string FileExtension => ".json";

    #endregion

    #region Public Methods

    public T GetConfiguration<T>(string componentId, string configurationName) where T : class
    {
        return LoadConfiguration<T>(componentId, configurationName);
    }

    /// <inheritdoc/>
    public object GetConfiguration(string componentId, string configurationName, Type configurationType)
    {
        return LoadConfiguration(componentId, configurationName, configurationType);
    }

    /// <inheritdoc/>
    public void SaveConfiguration<T>(string componentId, string configurationName, T config) where T : class
    {
        PersistConfiguration(componentId, configurationName, config);
    }

    /// <inheritdoc/>
    public bool TryGetConfiguration<T>(string componentId, string configurationName, out T config, out ICollection<string> errors) where T : class
    {
        config = null;
        errors = new List<string>();

        try
        {
            config = LoadConfiguration<T>(componentId, configurationName);
        }
        catch (Exception ex)
        {
            var errorMessage = string.Format(CultureInfo.InvariantCulture, ErrorLoadingConfigurationString, configurationName, ex.Message);
            errors.Add(errorMessage);
            return false;
        }

        GetValidationErrors(config, ref errors);
        return config != null && errors.Count == 0;
    }

    /// <inheritdoc/>
    public bool TryGetConfiguration(string componentId, string configurationName, Type configurationType, out object config, out ICollection<string> errors)
    {
        config = null;
        errors = new List<string>();

        try
        {
            config = LoadConfiguration(componentId, configurationName, configurationType);
        }
        catch (Exception ex)
        {
            var errorMessage = string.Format(CultureInfo.InvariantCulture, ErrorLoadingConfigurationString, configurationName, ex.Message);
            errors.Add(errorMessage);
            return false;
        }

        GetValidationErrors(config, ref errors);
        return config != null && errors.Count == 0;
    }

    /// <inheritdoc/>
    public bool IsConfigurationValid<T>(T configuration, out ICollection<string> errors) where T : class
    {
        errors = new List<string>();

        if (configuration == null)
        {
            errors.Add($"{nameof(configuration)} is null.");
            return false;
        }

        GetValidationErrors(configuration, ref errors);

        return errors.Count == 0;
    }

    /// <inheritdoc/>
    public bool TrySaveConfiguration<T>(string componentId, string configurationName, T configuration, out ICollection<string> errors) where T : class
    {
        errors = new List<string>();
        GetValidationErrors(configuration, ref errors);

        if (errors.Count == 0)
        {
            try
            {
                PersistConfiguration(componentId, configurationName, configuration);
                return true;
            }
            catch (Exception ex)
            {
                var errorMessage = string.Format(CultureInfo.InvariantCulture, ErrorSavingConfigurationString, configurationName, ex.Message);
                errors.Add(errorMessage);
                return false;
            }
        }

        return false;
    }

    public bool TryGetDiscoveryResult<T>(string componentId, string discoveryId, out T discoveryResult, out ICollection<string> errors) where T : class
    {
        discoveryResult = null;
        errors = new List<string>();
        string configurationName = null;
        try
        {
            configurationName = GetDiscoveryResultConfigurationName(discoveryId);
            return TryGetConfiguration(componentId, configurationName, out discoveryResult, out errors);
        }
        catch (Exception ex)
        {
            var errorMessage = string.Format(CultureInfo.InvariantCulture, ErrorLoadingConfigurationString, configurationName, ex.Message);
            errors.Add(errorMessage);
            return false;
        }
    }

    public bool TrySaveDiscoveryResult<T>(string componentId, string discoveryId, T discoveryConfiguration, out ICollection<string> errors) where T : class
    {
        errors = new List<string>();
        string configurationName = null;
        try
        {
            configurationName = GetDiscoveryResultConfigurationName(discoveryId);
            return TrySaveConfiguration(componentId, configurationName, discoveryConfiguration, out errors);
        }
        catch (Exception ex)
        {
            var errorMessage = string.Format(CultureInfo.InvariantCulture, ErrorSavingConfigurationString, configurationName, ex.Message);
            errors.Add(errorMessage);
            return false;
        }
    }

    public void DeleteDiscoveryResult(string componentId, string discoveryId)
    {
        var configurationName = GetDiscoveryResultConfigurationName(discoveryId);
        DeleteConfiguration(componentId, configurationName);
    }

    public void MoveCorruptedConfiguration(string componentId, string configurationName)
    {
        var sourcePath = GetConfigFilePath(componentId, configurationName);
        if (!Directory.Exists(RemovedConfigDirPath))
        {
            Directory.CreateDirectory(RemovedConfigDirPath);
        }

        var destinationPath = GetCorruptedConfigFilePath(componentId, configurationName);
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(sourcePath, destinationPath);
    }

    public bool TryMoveCorruptedConfiguration(string componentId, string configurationName, out string errorMessage)
    {
        var result = true;
        errorMessage = null;
        try
        {
            MoveCorruptedConfiguration(componentId, configurationName);
        }
        catch (Exception ex)
        {
            result = false;
            errorMessage = string.Format(CultureInfo.InvariantCulture, ErrorMovingCorruptedConfigurationString, configurationName, ex.Message);
        }

        return result;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Returns discovery result configuration name.
    /// </summary>
    /// <param name="discoveryId">Discovery ID.</param>
    /// <returns>Discovery result configuration name where discovery ID is converted to upper.</returns>
    /// <remarks>ToUpperInvariant() is used to make sure we are able to load results on Linux systems in a case-insensitive manner.</remarks>
    private static string GetDiscoveryResultConfigurationName(string discoveryId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(discoveryId, nameof(discoveryId));

        return DiscoveryResultFileKeyword + Separator + discoveryId.ToUpperInvariant();
    }

    private void PersistConfiguration<T>(string componentId, string configName, T config) where T : class
    {
        var path = GetConfigFilePath(componentId, configName);
        File.WriteAllText(path, JsonSerializer.Serialize(config, _serializerOptions));
    }

    private T LoadConfiguration<T>(string componentId, string configName) where T : class
    {
        var path = GetConfigFilePath(componentId, configName);

        if (!File.Exists(path))
        {
            return null;
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var streamReader = new StreamReader(file, Encoding.UTF8);
        var readContents = streamReader.ReadToEnd();
        return JsonSerializer.Deserialize<T>(readContents, _serializerOptions);
    }

    private object LoadConfiguration(string componentId, string configName, Type configurationType)
    {
        var path = GetConfigFilePath(componentId, configName);

        if (!File.Exists(path))
        {
            return null;
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var streamReader = new StreamReader(file, Encoding.UTF8);
        var readContents = streamReader.ReadToEnd();
        return JsonSerializer.Deserialize(readContents, configurationType, _serializerOptions);
    }

    #endregion
}
