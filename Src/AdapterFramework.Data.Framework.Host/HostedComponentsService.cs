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
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ComponentIdProvider;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Host.Helpers;
using AdapterFramework.Data.Framework.Host.Interfaces;
using AdapterFramework.Data.Framework.Host.Management;
using AdapterFramework.Data.Framework.Host.SystemMiddleware;

namespace AdapterFramework.Data.Framework.Host;

internal class HostedComponentsService : IHostedService
{
    #region Constants

    // wait 2 minutes for components to stop
    private const int WaitForStopTasks = 120000;

    #endregion

    #region Private Members

    private readonly ILogger _logger;
    private readonly ILoggerConfigurator _loggerConfigurator;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IRequestMetricsTracker _requestsTracker;
    private readonly IAllowRequestsManager _allowRequestsManager;
    private readonly IEdgeDiagnosticsService _edgeDiagnosticsService;
    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;
    private readonly IRuntimeManagementRegistry _runtimeManagementRegistry;
    private readonly IOmfHealthEndpointManager _omfHealthEndpointManager;
    private readonly IOmfDataEndpointManager _omfDataEndpointManager;
    private readonly IComponentIdService _componentIdService;
    private readonly IEdgeComponentsRepository _edgeComponentsRepository;
    private readonly IApplicationManifest _applicationManifest;
    private readonly IDiagnosticsMessageProcessor _diagnosticsMessageProcessor;
    private readonly IEdgeComponentsOperationService _edgeComponentsOperationService;
    private readonly HashSet<string> _registeredAdapterTypes = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Regex _secretPlaceholderRegex = new(EdgeSystemConstants.SecretIdPlaceholderPattern);
    private readonly ISecretsManager _secretsManager;
    private readonly string _sinkProviderName;
    private readonly IFailoverServiceProvider _failoverServiceProvider;
    private readonly IEnumerable<IEdgeService> _edgeServices;
    private GeneralConfiguration _generalConfiguration;
    private SystemCmdConfigService _systemCmdConfigService;
    private IEdgeEventProvider _edgeEventProvider;

    #endregion

    #region Constructor

    public HostedComponentsService(
        ILogger logger,
        ILoggerConfigurator loggerConfigurator,
        IComponentIdService componentIdService,
        IOmfHealthEndpointManager omfHealthEndpointManager,
        IRuntimeConfigurationRegistry runtimeConfigurationRegistry,
        IRuntimeManagementRegistry runtimeManagementRegistry,
        IConfigurationProvider configurationProvider,
        IServiceProvider serviceProvider,
        IRequestMetricsTracker requestsTracker,
        IAllowRequestsManager allowRequestsManager,
        IEdgeComponentsRepository edgeComponentsRepository,
        IEdgeComponentsOperationService edgeComponentsOperationService,
        IEdgeDiagnosticsService edgeDiagnosticsService,
        ISecretsManager secretsManager,
        IApplicationManifest applicationManifest,
        IDiagnosticsMessageProcessor diagnosticsMessageProcessor = null,
        ISinkProvider storageSink = null,
        IFailoverServiceProvider failoverServiceProvider = null,
        IEnumerable<IEdgeService> edgeServices = null,
        IEdgeEventProvider edgeEventProvider = null,
        IOmfDataEndpointManager omfDataEndpointManager = null)
    {
        _logger = logger;
        _loggerConfigurator = loggerConfigurator;
        _serviceProvider = serviceProvider;
        _configurationProvider = configurationProvider;
        _requestsTracker = requestsTracker;
        _allowRequestsManager = allowRequestsManager;
        _edgeDiagnosticsService = edgeDiagnosticsService;
        _omfHealthEndpointManager = omfHealthEndpointManager;
        _omfDataEndpointManager = omfDataEndpointManager;
        _componentIdService = componentIdService;
        _edgeComponentsRepository = edgeComponentsRepository;
        _edgeComponentsOperationService = edgeComponentsOperationService;
        _applicationManifest = applicationManifest;
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
        _runtimeManagementRegistry = runtimeManagementRegistry;
        _diagnosticsMessageProcessor = diagnosticsMessageProcessor;
        _secretsManager = secretsManager;
        _sinkProviderName = $"{(applicationManifest.IsEdgeDataStore ? EdgeSystemConstants.StorageComponentType : EdgeSystemConstants.OmfEgressComponentType)}";
        _edgeComponentsRepository.SetSinkProvider(storageSink);
        _failoverServiceProvider = failoverServiceProvider;
        _edgeServices = edgeServices;
        _edgeEventProvider = edgeEventProvider;
    }

    #endregion

    #region Public Methods

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting hosted components.");

        var storageStarted = false;

        SetGeneralConfiguration();

        // Start Storage
        var sinkProvider = _edgeComponentsRepository.GetSinkProvider();
        if (sinkProvider != null)
        {
            try
            {
                await sinkProvider.StartAsync();
                storageStarted = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting {SinkProviderName}.", _sinkProviderName);
            }
        }

        try
        {
            SetRegisteredAdapterTypes();
            RegisterSystemConfigurations();
            RegisterManagementConfigurations();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting adapter types and registering {ComponentId} configuration routes.", EdgeSystemConstants.SystemComponentId);
        }

        if (storageStarted)
        {
            // Start failover services if registered.
            if (_failoverServiceProvider != null)
            {
                await _failoverServiceProvider.StartAsync(CancellationToken.None);
            }

            await StartEdgeDiagnosticsServiceAsync(cancellationToken);

            var adapters = GetConfiguredAdapterComponents();

            await StartAdapterComponentsAsync(adapters, cancellationToken);

            RegisterSecretManagementIdentifiers();
        }

        // regardless of any errors in components initializing/starting, we must always
        // notify the RequestsManager middle-ware to allow requests to flow.  This allows us
        // to at least bring up the platform and make it available for configuration and diagnosis
        _allowRequestsManager.RequestsAllowed = true;

        await StartEdgeServicesAsync(_edgeServices);

        StartEventsListenerAsync(CancellationToken.None);

        _logger.LogInformation("Hosted components have been started and the platform is available for requests.");
        _logger.LogInformation("Now listening on: {BaseAddress}.", _applicationManifest.BaseApplicationAddress);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping hosted components.");

        // disallow incoming requests for the platform and await completion of in-flight requests
        _allowRequestsManager.RequestsAllowed = false;
        await _requestsTracker.AwaitCompletionOfFinalRequestsAsync(TimeSpan.FromSeconds(30), _logger);

        // Stop the adapters
        StopAdapterComponents(_edgeComponentsRepository.GetAdapters(), cancellationToken);

        // Stop Edge Diagnostics service
        StopEdgeDiagnosticsService(cancellationToken);

        // Stop failover services if registered.
        if (_failoverServiceProvider != null)
        {
            await _failoverServiceProvider.StopAsync(cancellationToken);
        }

        await StopEdgeServicesAsync(_edgeServices);

        var sinkProvider = _edgeComponentsRepository.GetSinkProvider();

        // Stop Storage
        if (sinkProvider != null)
        {
            var componentTasks = new List<Task>
            {
                sinkProvider.StopAsync(),
            };

            WaitForStopTasksAsync(componentTasks);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        _logger.LogInformation("Hosted components are shutdown.");
    }

    #endregion

    #region Private Methods

    private void StartEventsListenerAsync(CancellationToken token)
    {
        if (_edgeEventProvider == null)
        {
            return;
        }

        // Run as a background task instead of fire-and-forget async
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessEventsAsync(token);
            }
            catch (OperationCanceledException)
            {
                // OMF egress event listener was cancelled.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OMF egress events listener encountered an unexpected error and stopped.");
            }
        }, token);
    }

    private async Task ProcessEventsAsync(CancellationToken token)
    {
        _logger.LogDebug("Starting OMF egress events listener.");

        var reader = _edgeEventProvider.EdgeEventChannel.Reader;
        while (await reader.WaitToReadAsync(token))
        {
            var adapters = _edgeComponentsRepository.GetAdapters().ToList();
            while (reader.TryRead(out var evt))
            {
                foreach (var adapter in adapters)
                {
                    try
                    {
                        if (adapter.EdgeEventChannel != null)
                        {
                            await adapter.EdgeEventChannel.Writer.WriteAsync(evt, token);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing egress event in adapter {AdapterId}.", adapter.ComponentId);
                    }
                }
            }
        }
    }

    private async Task StartEdgeServicesAsync(IEnumerable<IEdgeService> edgeServices)
    {
        if (edgeServices == null)
        {
            return;
        }

        foreach (var edgeService in edgeServices)
        {
            try
            {
                await edgeService.InitializeAsync();
                await edgeService.StartAsync();

                _edgeComponentsRepository.TryAddEdgeService(edgeService);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Edge Service of type: {Type} with Id: {ID}.", edgeService.ComponentType, edgeService.ComponentId);
            }
        }
    }

    private async Task StopEdgeServicesAsync(IEnumerable<IEdgeService> edgeServices)
    {
        if (edgeServices == null)
        {
            return;
        }

        foreach (var edgeService in edgeServices)
        {
            try
            {
                _edgeComponentsRepository.TryRemoveEdgeService(edgeService.ComponentId, out _);

                await edgeService.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop Edge Service of type: {Type} with Id: {ID}.", edgeService.ComponentType, edgeService.ComponentId);
            }
        }
    }

    private ICollection<string> PreValidateComponentsChangeAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        var errors = new List<string>();

        var newConfiguration = (EdgeComponentConfig[])configurationChangeEvent.NewValue;

        if (newConfiguration != null)
        {
            var foundStorage = 0;
            foreach (var component in newConfiguration)
            {
                if (component.ComponentId.Equals(EdgeSystemConstants.SystemComponentId, StringComparison.InvariantCultureIgnoreCase))
                {
                    errors.Add($"Cannot add component id '{EdgeSystemConstants.SystemComponentId}'.");
                    return errors;
                }

                if (component.ComponentId.Equals(EdgeSystemConstants.ManagementComponentId, StringComparison.InvariantCultureIgnoreCase))
                {
                    errors.Add($"Cannot add component id '{EdgeSystemConstants.ManagementComponentId}'.");
                    return errors;
                }

                if (Services.IsStorage(component.ComponentType) || Services.IsOmfEgress(component.ComponentType))
                {
                    foundStorage++;
                }
                else
                {
                    if (!_registeredAdapterTypes.Contains(component.ComponentType))
                    {
                        errors.Add($"Adapter type '{component.ComponentType}' isn't registered. " +
                                   $"{(_applicationManifest.IsEdgeDataStore ? "Please use one of the registered adapter types: " : "Please use the registered adapter type: ")}" +
                                   $"{string.Join(", ", _registeredAdapterTypes)}.");
                    }
                }
            }

            if (foundStorage == 1)
            {
                return errors;
            }

            errors.Add(foundStorage == 0
                ? $"Cannot delete {_sinkProviderName} component, since exactly one {_sinkProviderName} component must be registered."
                : $"Cannot add another component of '{_sinkProviderName}' type.");
        }
        else
        {
            errors.Add("Entire components configuration cannot be deleted. Individual components can be deleted by ID.");
        }

        return errors;
    }

    private async Task StartAdapterComponentsAsync(IReadOnlyList<IEdgeAdapter> adapters, CancellationToken cancellationToken)
    {
        var componentTasks = new List<Task>();

        // Initialize the adapters
        foreach (var adapterService in adapters)
        {
            _logger.LogInformation("Initializing {AdapterType} instance.", adapterService.ComponentType);
            componentTasks.Add(adapterService.InitializeAsync(cancellationToken));
        }

        await WaitForTransitionTasksAsync(componentTasks, "initialized");

        // Start the adapters
        foreach (var adapterService in adapters)
        {
            await adapterService.ProcessGeneralConfigurationUpdateCallbackAsync(_generalConfiguration, _generalConfiguration);
            _logger.LogInformation("Starting {AdapterType} instance with ID {AdapterId}.", adapterService.ComponentType, adapterService.ComponentId);
            componentTasks.Add(adapterService.StartAsync(cancellationToken));
        }

        await WaitForTransitionTasksAsync(componentTasks, "started");

        foreach (var adapter in adapters)
        {
            _edgeComponentsRepository.TryAddAdapter(adapter);
        }
    }

    private void StopAdapterComponents(IEnumerable<IEdgeAdapter> adapters, CancellationToken cancellationToken)
    {
        var componentTasks = new List<Task>();

        foreach (var adapterService in adapters)
        {
            _logger.LogInformation("Stopping {AdapterType} instance with ID {AdapterId}.", adapterService.ComponentType, adapterService.ComponentId);
            componentTasks.Add(adapterService.StopAsync(cancellationToken));
        }

        WaitForStopTasksAsync(componentTasks);
    }

    private async Task StartEdgeDiagnosticsServiceAsync(CancellationToken cancellationToken)
    {
        var componentTasks = new List<Task>
        {
            _edgeDiagnosticsService.InitializeAsync(cancellationToken),
        };

        await WaitForTransitionTasksAsync(componentTasks, "initialized");

        componentTasks.Add(_edgeDiagnosticsService.StartAsync(cancellationToken));

        await WaitForTransitionTasksAsync(componentTasks, "started");
    }

    private void StopEdgeDiagnosticsService(CancellationToken cancellationToken)
    {
        var componentTasks = new List<Task>
        {
            _edgeDiagnosticsService.StopAsync(cancellationToken),
        };

        WaitForStopTasksAsync(componentTasks);
    }

    private void RegisterSystemConfigurations()
    {
        _systemCmdConfigService = new SystemCmdConfigService(EdgeSystemConstants.SystemComponentId, _registeredAdapterTypes, _applicationManifest);

        var loggingConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, EdgeSystemConstants.SystemComponentId, LoggerConfiguration.ConfigName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<LoggerConfiguration>(EdgeSystemConstants.SystemComponentId, LoggerConfiguration.ConfigName, loggingConfigurationCommandGenerator,
            LoggingConfigurationChangedAction, _systemCmdConfigService.GetLoggingHelpOutput, null, Operations.Get | Operations.Create | Operations.Update);

        var healthConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.HealthEndpointsFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<OmfHealthEndpointConfiguration[]>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.HealthEndpointsFacetName, healthConfigurationCommandGenerator,
            HealthEndpointsChangeAction, _systemCmdConfigService.GetHealthEndpointsHelpOutput, OmfHealthEndpointConfiguration.CheckForDuplicateEndpoints);

        var componentsConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<EdgeComponentConfig[]>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName, componentsConfigurationCommandGenerator,
            ComponentsChangedAction, _systemCmdConfigService.GetComponentsHelpOutput, PreValidateComponentsChangeAction);

        var bufferingConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.BufferingFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<BufferingConfiguration>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.BufferingFacetName, bufferingConfigurationCommandGenerator,
            BufferingConfigurationChangedAction, _systemCmdConfigService.GetBufferingHelpOutput, null, Operations.Get | Operations.Create | Operations.Update);

        var generalConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.GeneralFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<GeneralConfiguration>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.GeneralFacetName, generalConfigurationCommandGenerator,
            GeneralConfigurationChangedAction, _systemCmdConfigService.GetGeneralHelpOutput, null, Operations.Get | Operations.Create | Operations.Update);
    }

    private void RegisterManagementConfigurations()
    {
        var managementService = new ManagementComponentsService();
        var secretConfigurationCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, EdgeSystemConstants.ManagementComponentId, EdgeSystemConstants.SecretsFacetName);

        _runtimeManagementRegistry.RegisterComponentConfiguration<ManagedSecretConfiguration[]>(EdgeSystemConstants.ManagementComponentId, EdgeSystemConstants.SecretsFacetName,
            secretConfigurationCommandGenerator, _secretsManager.ConfigurationChangedAction, managementService.GetSecretsHelpOutput);
    }

    private void GeneralConfigurationChangedAction(ConfigurationChangedEventArgs configurationChangedEvent)
    {
        var (oldConfig, newConfig) = ((GeneralConfiguration)configurationChangedEvent.OldValue, (GeneralConfiguration)configurationChangedEvent.NewValue);

        if (_diagnosticsMessageProcessor != null)
        {
            if (newConfig == null)
            {
                _diagnosticsMessageProcessor.SystemDiagnosticsEnabled = true;
                _diagnosticsMessageProcessor.StreamMetadataLevel = MetadataInfo.Medium;
            }
            else
            {
                _diagnosticsMessageProcessor.SystemDiagnosticsEnabled = newConfig.EnableDiagnostics;
                _diagnosticsMessageProcessor.StreamMetadataLevel = newConfig.MetadataLevel;

                if ((oldConfig == null || !oldConfig.EnableDiagnostics) && newConfig.EnableDiagnostics)
                {
                    _edgeDiagnosticsService?.ResendTypesAndStreams();
                    _edgeComponentsOperationService.ResendHealthMetadata();
                    _failoverServiceProvider?.ResendHealthMetadata();
                }
                else if (newConfig.MetadataLevel >= MetadataInfo.Low && (oldConfig == null || oldConfig.MetadataLevel == MetadataInfo.None))
                {
                    _edgeComponentsOperationService.ResendHealthMetadata();
                }
            }
        }

        if (!string.Equals(newConfig?.HealthPrefix, oldConfig?.HealthPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("{Property} value has changed. Restart the adapter service to apply the change. Note: New streams might get created as a result.", nameof(newConfig.HealthPrefix));
        }

        _edgeComponentsOperationService.ProcessGeneralConfigurationUpdate(oldConfig, newConfig);
    }

    private void SetGeneralConfiguration()
    {
        _configurationProvider.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.GeneralFacetName, out _generalConfiguration, out var getErrors);

        if (_generalConfiguration == null)
        {
            _generalConfiguration = new GeneralConfiguration();
            if (getErrors.IsNullOrEmpty())
            {
                if (!_configurationProvider.TrySaveConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.GeneralFacetName, _generalConfiguration, out var saveErrors))
                {
                    _logger.LogError(EdgeSystemConstants.FailedToSaveConfigurationMessage, EdgeSystemConstants.GeneralFacetName, saveErrors);
                }
            }
            else
            {
                _logger.LogWarning(EdgeSystemConstants.ConfigurationInvalidMessage, EdgeSystemConstants.GeneralFacetName, getErrors);
            }
        }

        _logger.LogInformation("Metadata level: {Level}.", _generalConfiguration.MetadataLevel);

        if (_diagnosticsMessageProcessor != null)
        {
            _diagnosticsMessageProcessor.SystemDiagnosticsEnabled = _generalConfiguration.EnableDiagnostics;
            _diagnosticsMessageProcessor.StreamMetadataLevel = _generalConfiguration.MetadataLevel;
        }
    }

    private void BufferingConfigurationChangedAction(ConfigurationChangedEventArgs configurationChangedEvent)
    {
        var oldConfig = configurationChangedEvent.OldValue as BufferingConfiguration;
        var newConfig = configurationChangedEvent.NewValue as BufferingConfiguration;
        var restartRequiredProperties = new List<string>();

        // Handle deletion or initial configuration
        if (newConfig == null)
        {
            if (oldConfig != null)
            {
                _logger.LogWarning("Buffering configuration change will take effect only on Adapter restart.");
            }

            return;
        }

        if (oldConfig == null)
        {
            return; // Initial configuration - no action needed
        }

        // Determine what changed
        bool maxBufferSizeChanged = newConfig.MaxBufferSizeMB != oldConfig.MaxBufferSizeMB;
        if (!string.Equals(newConfig.BufferLocation, oldConfig.BufferLocation, StringComparison.Ordinal))
        {
            restartRequiredProperties.Add(nameof(BufferingConfiguration.BufferLocation));
        }

        if (newConfig.EnablePersistentBuffering != oldConfig.EnablePersistentBuffering)
        {
            restartRequiredProperties.Add(nameof(BufferingConfiguration.EnablePersistentBuffering));
        }

        if (newConfig.MaxDataBulkTime != oldConfig.MaxDataBulkTime)
        {
            restartRequiredProperties.Add(nameof(BufferingConfiguration.MaxDataBulkTime));
        }

        bool otherPropertiesChanged = restartRequiredProperties.Count > 0;

        // Apply MaxBufferSizeMB at runtime if persistent buffering is enabled
        if (maxBufferSizeChanged && newConfig.EnablePersistentBuffering)
        {
            if (_omfDataEndpointManager == null)
            {
                _logger.LogWarning("Buffering configuration change requires data endpoint manager initialization before runtime update can be applied.");
                restartRequiredProperties.Add(nameof(BufferingConfiguration.MaxBufferSizeMB));
                _logger.LogWarning("Buffering configuration change will take effect only on Adapter restart for: {Properties}.",
                    string.Join(", ", restartRequiredProperties.Distinct()));
                return;
            }

            _omfDataEndpointManager.UpdateBufferSize(newConfig.MaxBufferSizeMB);

            _logger.LogInformation("Buffering configuration change applied. New MaxBufferSizeMB: {MaxBufferSizeMB}.",
                newConfig.MaxBufferSizeMB);

            // If only MaxBufferSizeMB changed, no restart needed
            if (!otherPropertiesChanged)
            {
                return;
            }
        }

        // Warn if restart is needed for any property change
        if (otherPropertiesChanged || (maxBufferSizeChanged && !newConfig.EnablePersistentBuffering))
        {
            if (maxBufferSizeChanged && !newConfig.EnablePersistentBuffering)
            {
                restartRequiredProperties.Add(nameof(BufferingConfiguration.MaxBufferSizeMB));
            }

            _logger.LogWarning("Buffering configuration change will take effect only on Adapter restart for: {Properties}.",
                string.Join(", ", restartRequiredProperties.Distinct()));
        }
    }

    private void HealthEndpointsChangeAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        if (_omfHealthEndpointManager.AddRemoveEndpoints(configurationChangeEvent, OmfWriterType.Health))
        {
            _edgeDiagnosticsService.ResendTypesAndStreams();
            _edgeComponentsOperationService.ResendHealthMetadata();
            _failoverServiceProvider?.ResendHealthMetadata();
        }
    }

    private void LoggingConfigurationChangedAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        if (configurationChangeEvent.NewValue is LoggerConfiguration newLoggerConfiguration)
        {
            _loggerConfigurator.SetMinimumLogLevel(newLoggerConfiguration.LogLevel);

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

    private void ComponentsChangedAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        if (configurationChangeEvent.NewValue is EdgeComponentConfig[] newEdgeComponentConfigs)
        {
            var adaptersToStart = new List<IEdgeAdapter>();
            var configuredComponentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var component in newEdgeComponentConfigs)
            {
                if (Services.IsStorage(component.ComponentType) || Services.IsOmfEgress(component.ComponentType))
                {
                    continue;
                }

                configuredComponentIds.Add(component.ComponentId);

                if (!_edgeComponentsRepository.TryGetAdapter(component.ComponentId, out _))
                {
                    var adapter = GetAdapterComponent(component);
                    if (adapter == null)
                    {
                        _logger.LogWarning("Requested adapter type: {ComponentType} was not found.", component.ComponentType);
                        continue;
                    }

                    _componentIdService.AddEdgeComponentId(component.ComponentType, component.ComponentId);
                    adaptersToStart.Add(adapter);
                }
            }

            StartAdapterComponentsAsync(adaptersToStart, CancellationToken.None).GetAwaiter().GetResult();

            var adaptersToRemove = new List<IEdgeAdapter>();
            foreach (var adapter in _edgeComponentsRepository.GetAdapters())
            {
                if (!configuredComponentIds.Contains(adapter.ComponentId))
                {
                    adaptersToRemove.Add(adapter);
                }
            }

            StopAdapterComponents(adaptersToRemove, CancellationToken.None);

            foreach (var adapterToStop in adaptersToRemove)
            {
                _edgeComponentsRepository.TryRemoveAdapter(adapterToStop.ComponentId, out _);

                adapterToStop.Unregister(CancellationToken.None);
                adapterToStop.Dispose();
            }
        }
    }

    private IReadOnlyList<IEdgeAdapter> GetConfiguredAdapterComponents()
    {
        var adapterComponents = new List<IEdgeAdapter>();

        if (!_configurationProvider.TryGetConfiguration<EdgeComponentConfig[]>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName, out var componentsConfiguration, out var getErrors))
        {
            componentsConfiguration = ComponentIdService.CreateDefaultComponentConfiguration(_applicationManifest);
            if (getErrors.IsNullOrEmpty())
            {
                if (!_configurationProvider.TrySaveConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName, componentsConfiguration, out var saveErrors))
                {
                    _logger.LogError(EdgeSystemConstants.FailedToSaveConfigurationMessage, EdgeSystemConstants.ComponentsFacetName, saveErrors);
                }
            }
        }

        foreach (var component in componentsConfiguration)
        {
            if (Services.IsStorage(component.ComponentType) || Services.IsOmfEgress(component.ComponentType))
            {
                continue;
            }

            var registeredAdapters = _serviceProvider.GetServices<IEdgeAdapter>();
            var adapterTypeFound = false;
            foreach (var adapter in registeredAdapters)
            {
                if (adapter.ComponentType.Equals(component.ComponentType, StringComparison.OrdinalIgnoreCase))
                {
                    adapterComponents.Add(adapter);
                    adapterTypeFound = true;
                }
                else
                {
                    adapter.Dispose();
                }
            }

            if (!adapterTypeFound)
            {
                _logger.LogWarning("Configured adapter type: {AdapterType} is not registered. Adapter with ID {AdapterId} will not be added.",
                    component.ComponentType, component.ComponentId);
            }
        }

        return adapterComponents;
    }

    private void SetRegisteredAdapterTypes()
    {
        var registeredAdapters = _serviceProvider.GetServices<IEdgeAdapter>();

        foreach (var adapter in registeredAdapters)
        {
            // add adapter type to the collection of registered types
            _registeredAdapterTypes.Add(adapter.ComponentType);

            adapter.Dispose();
        }
    }

    private IEdgeAdapter GetAdapterComponent(EdgeComponentConfig edgeComponentConfig)
    {
        IEdgeAdapter requestedAdapter = null;
        var registeredAdapters = _serviceProvider.GetServices<IEdgeAdapter>();

        foreach (var adapter in registeredAdapters)
        {
            if (adapter.ComponentType.Equals(edgeComponentConfig.ComponentType, StringComparison.OrdinalIgnoreCase))
            {
                requestedAdapter = adapter;
            }
            else
            {
                adapter.Dispose();
            }
        }

        return requestedAdapter;
    }

    private void RegisterSecretManagementIdentifiers()
    {
        var protectedPropertyInfos = _runtimeConfigurationRegistry.GetProtectedPropertyInfos();
        if (protectedPropertyInfos == null)
        {
            return;
        }

        foreach (var protectedPropertyInfo in protectedPropertyInfos)
        {
            if (!_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((protectedPropertyInfo.Key.ComponentId, protectedPropertyInfo.Key.Facet),
                out var commandGeneratorTuple))
            {
                continue;
            }

            var getCommand = commandGeneratorTuple.CommandGenerator.GenerateConfigurationGetCommand(commandGeneratorTuple.ConfigurationType, null);
            if (!getCommand.TryExecute(out var configuration, out _) || configuration == null)
            {
                continue;
            }

            if (commandGeneratorTuple.ConfigurationType.IsArray)
            {
                if (configuration is not object[] arrayConfiguration)
                {
                    continue;
                }

                foreach (var propertyInfo in protectedPropertyInfo.Value)
                {
                    RegisterIdentifiersFromConfigurationEntries(protectedPropertyInfo.Key.ComponentId, protectedPropertyInfo.Key.Facet,
                        arrayConfiguration, commandGeneratorTuple.ConfigurationType, propertyInfo);
                }

                continue;
            }

            foreach (var propertyInfo in protectedPropertyInfo.Value)
            {
                if (IsSecretPlaceholder(propertyInfo, configuration, out var secretId))
                {
                    var scrubbedSecretId = ConfigurationHelper.RemovePatternFromId(secretId);
                    _runtimeManagementRegistry.AddOrUpdateSecretIdFacetsMapping(scrubbedSecretId, protectedPropertyInfo.Key.ComponentId,
                        protectedPropertyInfo.Key.Facet);

                    _logger.LogDebug("Secret Id '{SecretId}' mapped to component '{ComponentId}' and facet '{Facet}'.",
                        scrubbedSecretId, protectedPropertyInfo.Key.ComponentId, protectedPropertyInfo.Key.Facet);
                }
            }
        }
    }

    private bool IsSecretPlaceholder(PropertyInfo propertyInfo, object configuration, out string secretId)
    {
        secretId = (string)propertyInfo.GetValue(configuration);

        return secretId != null && _secretPlaceholderRegex.IsMatch(secretId);
    }

    private void RegisterIdentifiersFromConfigurationEntries(string componentId, string facet, object[] configurations, Type configurationType,
        PropertyInfo propertyInfo)
    {
        foreach (var configurationEntry in configurations)
        {
            if (!IsSecretPlaceholder(propertyInfo, configurationEntry, out var secretId))
            {
                continue;
            }

            var idPropertyInfo = ConfigurationHelper.GetIdProperty(configurationType.GetElementType());
            var entryId = (string)idPropertyInfo.GetValue(configurationEntry);

            var scrubbedSecretId = ConfigurationHelper.RemovePatternFromId(secretId);
            _runtimeManagementRegistry.AddOrUpdateSecretIdFacetsMapping(scrubbedSecretId, componentId, facet, entryId);

            _logger.LogDebug("Secret Id '{SecretId}' mapped to component '{ComponentId}' facet '{Facet}' and configuration entry '{EntryId}'.",
                scrubbedSecretId, componentId, facet, entryId);
        }
    }

    private void WaitForStopTasksAsync(List<Task> adapterTasks)
    {
        try
        {
            var tasks = new Task[adapterTasks.Count];
            adapterTasks.CopyTo(tasks, 0);
            using var cts = new CancellationTokenSource(WaitForStopTasks);
            Task.WaitAll(tasks, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error awaiting for components to transition to stopped.");
        }

        adapterTasks.Clear();
    }

    private async Task WaitForTransitionTasksAsync(List<Task> adapterTasks, string transitionState)
    {
        try
        {
            await Task.WhenAll(adapterTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error awaiting for components to transition to {State}.", transitionState);
        }

        adapterTasks.Clear();
    }

    #endregion
}
