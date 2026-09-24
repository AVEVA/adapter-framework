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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Common.HierarchyCreation;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.EgressComponent.Diagnostics;
using AdapterFramework.Data.Framework.EgressComponent.Extensions;
using AdapterFramework.Data.Framework.EgressComponent.Health;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.EgressComponent;

public class OmfEgressComponent : ISinkProvider, IDisposable
{
    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;
    private readonly IRuntimeAdministrationRegistry _runtimeAdministrationRegistry;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IEdgeComponentsOperationService _edgeComponentsOperationService;
    private readonly IOmfDataEndpointManager _omfDataEndpointManager;
    private readonly ILogger _logger;
    private readonly ILoggerConfigurator _loggerConfigurator;
    private readonly EgressDiagnosticsService _diagnosticsService;
    private readonly HealthServiceBase _healthService;
    private readonly BaseHierarchyCreator _baseHierarchyCreator;
    private readonly IHealthMessageProcessor _healthMessageProcessor;
    private EgressCmdConfigService _egressCmdConfigService;
    private bool _disposed;

    public OmfEgressComponent(ILogManager logManager, IEgressComponentIdService egressComponentIdService, IRuntimeConfigurationRegistry runtimeConfigurationRegistry, IRuntimeAdministrationRegistry runtimeAdministrationRegistry, 
        IConfigurationProvider configurationProvider, IEdgeComponentsOperationService edgeComponentsOperationService, IOmfDataEndpointManager omfDataEndpointManager, IDiagnosticsMessageProcessor diagnosticsMessageProcessor,
        IHealthMessageProcessor healthMessageProcessor, IApplicationManifest applicationManifest)
    {
        ThrowHelper.ThrowIfArgumentNull(egressComponentIdService, nameof(egressComponentIdService));
        ThrowHelper.ThrowIfArgumentNull(logManager, nameof(logManager));
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(omfDataEndpointManager, nameof(omfDataEndpointManager));

        ComponentId = egressComponentIdService.ComponentId;
        _logger = logManager.GetOrCreateLogger(ComponentId);
        _loggerConfigurator = logManager.GetLoggerConfigurator(ComponentId);

        _configurationProvider = configurationProvider;
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
        _runtimeAdministrationRegistry = runtimeAdministrationRegistry;
        _edgeComponentsOperationService = edgeComponentsOperationService;
        _omfDataEndpointManager = omfDataEndpointManager;
        _healthMessageProcessor = healthMessageProcessor;
        _omfDataEndpointManager.SetDeviceStatusHandler(SendDeviceStatus);
        _baseHierarchyCreator = new AdapterBaseHierarchyCreator(applicationManifest);
        _baseHierarchyCreator.CreateAndSendBaseHierarchy(healthMessageProcessor);
        _healthService = new EgressHealthService(healthMessageProcessor, _logger, applicationManifest, ComponentId);
        _diagnosticsService = new EgressDiagnosticsService(diagnosticsMessageProcessor, _logger, ComponentId, _omfDataEndpointManager, _healthService.GetHealthLinkNode(), _healthService,
            applicationManifest?.OmfVersion ?? OmfVersion.Omf12);
    }

    public string ComponentId { get; private set; }

    public string ComponentType => EdgeSystemConstants.OmfEgressComponentType;

    public static void AddComponent(IServiceCollection services, IReadOnlyDictionary<Type, object> dependencies)
    {
        ThrowHelper.ThrowIfArgumentNull(dependencies, nameof(dependencies));

        var componentIdService = (IComponentIdService)dependencies[typeof(IComponentIdService)];
        services.AddEgress(componentIdService);
    }

    public static void UseComponent(IApplicationBuilder app)
    {
        app.UseEgress();
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task StartAsync()
    {
        _egressCmdConfigService = new EgressCmdConfigService(ComponentId);
        await StartHealthDiagnosticsAsync();
        RegisterConfigurationFacets();
        RegisterAdministrationsFacets();
        _logger.LogDebug("Component has been started.");
    }

    public async Task StopAsync()
    {
        _healthService.SendDeviceStatus(DeviceStatus.Shutdown);
        await _diagnosticsService.StopAsync();
        await _healthService.StopAsync();
        _logger.LogDebug("Component has been stopped.");
    }

    public void ResendHealthMetadata()
    {
        _baseHierarchyCreator.CreateAndSendBaseHierarchy(_healthMessageProcessor);
        _healthService.ResendTypesAndStreams();
        _healthService.ResendDeviceStatus();
        _diagnosticsService.ResendTypesAndStreams();
    }

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

        _healthService.Dispose();
        _diagnosticsService.Dispose();

        _disposed = true;
    }

    private void SendDeviceStatus(DeviceStatus status)
    {
        _healthService.SendDeviceStatus(status);
    }

    private void RegisterConfigurationFacets()
    {
        var loggerCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, EdgeSystemConstants.LoggingFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<LoggerConfiguration>(ComponentId, EdgeSystemConstants.LoggingFacetName, loggerCommandGenerator,
            LoggingConfigurationChangedAction, _egressCmdConfigService.GetLoggingHelpOutput, null, Operations.Get | Operations.Create | Operations.Update);

        var dataEndpointsConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider,
            ComponentId, EdgeSystemConstants.DataEndpointsFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<EgressEndpointConfiguration[]>(ComponentId,
            EdgeSystemConstants.DataEndpointsFacetName, dataEndpointsConfigurationCommandGenerator,
            EgressEndpointsChangeAction, _egressCmdConfigService.GetEgressEndpointsHelpOutput, EgressEndpointConfiguration.CheckForDuplicateEndpoints);
    }

    private void RegisterAdministrationsFacets()
    {
        _runtimeAdministrationRegistry.RegisterComponentCallbackFunction(ComponentId, EdgeSystemConstants.ResetFunctionName, ResetDataBuffersCallbackAsync);
    }

    private async Task ResetDataBuffersCallbackAsync()
    {
        try
        {
            await _omfDataEndpointManager.ResetDataBuffersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to perform data buffer reset.");
        }
    }

    private void LoggingConfigurationChangedAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        if (configurationChangeEvent.NewValue is LoggerConfiguration newLoggerConfiguration)
        {
            _loggerConfigurator?.SetMinimumLogLevel(newLoggerConfiguration.LogLevel);

            if (configurationChangeEvent.OldValue is LoggerConfiguration oldLoggerConfiguration)
            {
                if (newLoggerConfiguration.LogFileCountLimit != oldLoggerConfiguration.LogFileCountLimit ||
                    newLoggerConfiguration.LogFileSizeLimitBytes != oldLoggerConfiguration.LogFileSizeLimitBytes)
                {
                    _logger.LogWarning("Logging configuration change will take effect only on Adapter restart.");
                }
            }
        }
    }

    private void EgressEndpointsChangeAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        if (_omfDataEndpointManager.AddRemoveEndpoints(configurationChangeEvent, OmfWriterType.Data))
        {
            _edgeComponentsOperationService.ResendDynamicMetadata();
        }
    }

    private async Task StartHealthDiagnosticsAsync()
    {
        await _healthService.InitializeAsync();
        await _healthService.StartAsync();
        await _diagnosticsService.InitializeAsync();
        await _diagnosticsService.StartAsync();

        _healthService.SendDeviceStatus(DeviceStatus.Good);
    }
}
