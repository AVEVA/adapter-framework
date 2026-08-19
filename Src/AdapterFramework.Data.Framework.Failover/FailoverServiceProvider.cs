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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.Failover.Configuration;
using AdapterFramework.Data.Framework.Failover.Extensions;

namespace AdapterFramework.Data.Framework.Failover;

public class FailoverServiceProvider : IFailoverServiceProvider
{
    private readonly ILogger _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;
    private readonly IFailoverManager _failoverManager;
    private readonly FailoverCmdHelpService _failoverCmdHelpService;

    private FailoverMode _supportedFailoverModes;
    private bool _disposed;

    public FailoverServiceProvider(ILogger logger, IServiceProvider serviceProvider, IConfigurationProvider configurationProvider,
        IRuntimeConfigurationRegistry runtimeConfigurationRegistry, IFailoverManager failoverManager)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configurationProvider = configurationProvider;
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
        _failoverManager = failoverManager;

        _failoverCmdHelpService = new FailoverCmdHelpService();
    }

    public string ComponentId => "Failover";

    public string ComponentType => "FailoverManager";

    public static void AddComponent(IServiceCollection services, IReadOnlyDictionary<Type, object> dependencies)
    {
        services.AddFailoverServices();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _supportedFailoverModes = GetSupportedFailoverModes();

        _logger.LogDebug("{ServiceName} will use the following failover modes: {Modes}.", nameof(FailoverManager), _supportedFailoverModes);

        if (IsFailoverEnabled(_supportedFailoverModes))
        {
            _failoverManager.Initialize(_supportedFailoverModes);

            RegisterOptionalFailoverConfigurations();

            await _failoverManager.StartAsync(cancellationToken);

            _logger.LogDebug("{ServiceName} has been started.", nameof(FailoverManager));
        }
        else
        {
            _logger.LogDebug("{ServiceName} has been added but it's not initialized because the adapter component doesn't support any failover mode.",
                nameof(FailoverManager));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (IsFailoverEnabled(_supportedFailoverModes))
        {
            await _failoverManager.StopAsync(cancellationToken);

            _logger.LogDebug("{ServiceName} has been stopped.", nameof(FailoverManager));
        }
    }

    public void ResendHealthMetadata() => _failoverManager.ResendHealthAndDiagnostics();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        _failoverManager?.Dispose();
        _disposed = true;
    }

    private static bool IsFailoverEnabled(FailoverMode supportedFailoverModes)
    {
        return supportedFailoverModes.HasFlag(FailoverMode.Hot) ||
               supportedFailoverModes.HasFlag(FailoverMode.Warm) ||
               supportedFailoverModes.HasFlag(FailoverMode.Cold);
    }

    private void RegisterOptionalFailoverConfigurations()
    {
        var failoverConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, EdgeSystemConstants.SystemComponentId,
            EdgeSystemConstants.ClientFailoverFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<ClientFailoverConfiguration>(EdgeSystemConstants.SystemComponentId,
            EdgeSystemConstants.ClientFailoverFacetName, failoverConfigurationCommandGenerator, _failoverManager.UpdateFailoverConfiguration,
            _failoverCmdHelpService.GetClientFailoverHelpOutput, _failoverManager.ValidateFailoverConfiguration);
    }

    private FailoverMode GetSupportedFailoverModes()
    {
        var registeredAdapters = _serviceProvider.GetServices<IEdgeAdapter>();
        var supportedFailoverModes = FailoverMode.NotConfigured;

        try
        {
            foreach (var adapter in registeredAdapters)
            {
                supportedFailoverModes = adapter.SupportedFailoverModes;

                if (supportedFailoverModes.HasFlag(FailoverMode.Hot) ||
                    supportedFailoverModes.HasFlag(FailoverMode.Warm) ||
                    supportedFailoverModes.HasFlag(FailoverMode.Cold))
                {
                    break;
                }
            }

            return supportedFailoverModes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to obtain supported failover modes.");
            return supportedFailoverModes;
        }
        finally
        {
            if (registeredAdapters != null)
            {
                foreach (var registeredAdapter in registeredAdapters)
                {
                    registeredAdapter.Dispose();
                }
            }
        }
    }
}
