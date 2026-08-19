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
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.ConfigurationProvider;

public class ComponentConfigurationProvider : IComponentConfigurationProvider
{
    #region Private Fields

    private const string UnableToReadValidConfiguration = "Unable to read valid '{ConfigName}' configuration: {Errors}.";

    private readonly IConfigurationProvider _configurationProvider;
    private readonly string _componentId;
    private readonly ILogger _logger;

    #endregion

    #region Public Constructor

    public ComponentConfigurationProvider(IConfigurationProvider configurationProvider, string componentId, ILogger logger)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));

        _configurationProvider = configurationProvider;
        _componentId = componentId;
        _logger = logger;
    }

    #endregion

    #region Public Methods

    public string GetAdapterDataDirectoryPath()
    {
        return _configurationProvider.GetCommonApplicationDataDirectoryPath(_componentId);
    }

    public T GetConfiguration<T>(string configName) where T : EdgeConfigurationBase
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(configName, nameof(configName));

        if (_configurationProvider.TryGetConfiguration(_componentId, configName, out T configuration, out var errors))
        {
            return configuration;
        }

        if (!errors.IsNullOrEmpty())
        {
            _logger.LogError(UnableToReadValidConfiguration, configName, errors);
        }

        return null;
    }

    public void SaveConfiguration<T>(string configName, T config) where T : EdgeConfigurationBase
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(configName, nameof(configName));
        ThrowHelper.ThrowIfArgumentNull(config, nameof(config));

        if (!_configurationProvider.TryGetConfiguration(_componentId, configName, out T _, out var getErrors) && !getErrors.IsNullOrEmpty())
        {
            _logger.LogWarning(OriginalConfigurationInvalidMessage, configName, getErrors);
            if (!_configurationProvider.TryMoveCorruptedConfiguration(_componentId, configName, out var moveErrors))
            {
                _logger.LogError(FailedToMoveInvalidConfigurationMessage, configName, moveErrors);
            }
        }

        if (!_configurationProvider.TrySaveConfiguration(_componentId, configName, config, out var errors))
        {
            _logger.LogError(FailedToSaveConfigurationMessage, configName, errors);
        }
    }

    public T[] GetArrayConfiguration<T>(string configName) where T : EdgeConfigurationBase
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(configName, nameof(configName));

        if (_configurationProvider.TryGetConfiguration(_componentId, configName, out T[] configuration, out var errors))
        {
            return configuration;
        }

        if (!errors.IsNullOrEmpty())
        {
            _logger.LogError(UnableToReadValidConfiguration, configName, errors);
        }

        return null;
    }

    public void SaveArrayConfiguration<T>(string configName, T[] config) where T : EdgeConfigurationBase
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(configName, nameof(configName));
        ThrowHelper.ThrowIfArgumentNull(config, nameof(config));

        if (!_configurationProvider.TryGetConfiguration(_componentId, configName, out T[] _, out var getErrors) && !getErrors.IsNullOrEmpty())
        {
            _logger.LogWarning(OriginalConfigurationInvalidMessage, configName, getErrors);
            if (!_configurationProvider.TryMoveCorruptedConfiguration(_componentId, configName, out var moveErrors))
            {
                _logger.LogError(FailedToMoveInvalidConfigurationMessage, configName, moveErrors);
            }
        }

        if (!_configurationProvider.TrySaveConfiguration(_componentId, configName, config, out var errors))
        {
            _logger.LogError(FailedToSaveConfigurationMessage, configName, errors);
        }
    }

    #endregion
}
