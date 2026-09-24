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
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.AdapterCommon.Diagnostics;
using AdapterFramework.Data.Framework.AdapterCommon.Discovery;
using AdapterFramework.Data.Framework.AdapterCommon.Health;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;
using AdapterFramework.Data.Framework.AdapterCommon.Scheduling;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.MessageProcessor;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;
using static AdapterFramework.Data.Framework.AdapterCommon.CommonConstants;

namespace AdapterFramework.Data.Framework.AdapterCommon;

public abstract class AdapterMainBase<TDataSource, TSelection> : IEdgeAdapter
    where TDataSource : DataSourceConfigurationBase
    where TSelection : DataSelectionConfigurationBase, IEquatable<TSelection>
{
    #region Internal Constants

    internal const string DataSourceRemovedLogMessage = "The data source configuration has been removed. Please add a data source configuration in order to collect data.";
    internal const string DataSourceAddedLogMessage = "A new data source configuration has been added.";
    internal const string DataSourceEditedLogMessage = "The data source configuration has been received.";

    #endregion

    #region Private Constants

    private const int WaitTime = 120_000;
    private const string DefaultProductVersion = "1.0.0.0";
    private const string RemovalAppendString = "_removed_";
    private const string TimeFormatString = "yyyy-MM-dd--hh-mm-ss";
    private const string DiscoveryResultString = "Discovery";
    private const string DiscoveryResultWildcard = "*";
    private const string Underscore = "_";
    private const string Hyphen = "-";
    private const string JsonSuffix = ".json";
    private const string TxtSuffix = ".txt";
    private const string LogRegex = @"\d\d\d\d\d\d\d\d";
    private const string ConfigurationNotFoundMessage = "{ConfigurationName} configuration wasn't found. Please configure the adapter.";
    private const string ConfigurationInvalidMessage = "{ConfigurationName} configuration is invalid: {Errors}. Please configure the adapter.";
    private const string UnableToStartMessage = "Unable to start the adapter.";
    private const string UnableToStopMessage = "Unable to stop the adapter.";
    private const string UnableToUpdateDataSelectionMessage = "Unable to update the data selection for the adapter.";
    private const string UnableToStartComponentDueToHistoryOnlyMessage = "Data collection mode is set to HistoryOnly. The adapter component will not be started.";

    #endregion

    #region Private Fields

    private readonly IApplicationManifest _applicationManifest;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;
    private readonly IRuntimeAdministrationRegistry _runtimeAdministrationRegistry;
    private readonly IHealthMessageProcessor _healthMessageProcessor;
    private readonly IDiagnosticsMessageProcessor _diagnosticsMessageProcessor;
    private readonly IComponentIdService _componentIdService;
    private readonly IMessageProcessor _messageProcessor;
    private readonly IEdgeDataProtector _edgeDataProtector;
    private readonly IEdgeEventProvider _edgeEventProvider;
    private readonly IFailoverManager _failoverManager;
    private readonly ILogManager _logManager;
    private readonly SemaphoreSlim _stateChangeSemaphore = new(1, 1);
    private readonly SemaphoreSlim _callbackSemaphore = new(1, 1);
    private readonly List<string> _configurationFileNames = new()
    {
        DataSelectionConfigurationName,
        DataSourceConfigurationName,
        LoggingConfigurationName,
        SchedulesConfigurationName,
        DataFiltersConfigurationName,
        DiscoveriesConfigurationName,
        HistoryRecoveryConfigurationName,
        IntervalsToRecoverConfigurationName,
    };
    private CancellationTokenSource _eventListenerCts;
    private InstrumentedMessageProcessor _instrumentedMessageProcessor;
    private TDataSource _dataSourceConfiguration;
    private AdapterCmdHelpService _adapterCmdHelpService;
    private HealthServiceBase _healthService;
    private AdapterDiagnosticsService _diagnosticsService;
    private IInstrumentedLogger _logger;
    private ILoggerConfigurator _loggerConfigurator;
    private string _productVersion;
    private bool _adapterStarted;
    private bool _adapterStoppedFromCallback;
    private bool _registered;
    private bool _initialized;
    private bool _disposed;
    private Action<IDataSourceConfiguration> _commonServiceDataSourceHandler;
    private AdapterScheduleManager<TSelection> _scheduleManager;
    private AdapterMessageProcessor _adapterMessageProcessor;
    private AdapterCommonService _adapterCommonService;
    private DataSourceDiscoveryManager<TDataSource, TSelection> _dataSourceDiscoveryManager;
    private HistoryRecoveryManager<TDataSource, TSelection> _historyRecoveryManager;
    private FailoverMode _currentFailoverMode;
    private FailoverRole _currentFailoverRole;
    private Task _eventListenerTask;

    #endregion

    #region Protected Constructor

    protected AdapterMainBase(
        ILogManager logManager,
        IConfigurationProvider configurationProvider,
        IMessageProcessor messageProcessor,
        IApplicationManifest applicationManifest,
        IRuntimeConfigurationRegistry runtimeConfigurationRegistry,
        IEdgeDataProtector edgeDataProtector,
        IComponentIdService componentIdService,
        IHealthMessageProcessor healthMessageProcessor,
        IDiagnosticsMessageProcessor diagnosticsMessageProcessor,
        IRuntimeAdministrationRegistry runtimeAdministrationRegistry,
        IFailoverManager failoverManager = null,
        IEdgeEventProvider edgeEventProvider = null)
    {
        _applicationManifest = applicationManifest;
        _configurationProvider = configurationProvider;
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
        _runtimeAdministrationRegistry = runtimeAdministrationRegistry;
        _messageProcessor = messageProcessor;
        _healthMessageProcessor = healthMessageProcessor;
        _diagnosticsMessageProcessor = diagnosticsMessageProcessor;
        _componentIdService = componentIdService;
        _edgeDataProtector = edgeDataProtector;
        _logManager = logManager;
        _edgeEventProvider = edgeEventProvider;
        _failoverManager = failoverManager;

        _eventListenerCts = new CancellationTokenSource();
        _productVersion = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }

    #endregion

    #region Public Fields

    public abstract string ComponentType { get; }

    public string ComponentId { get; private set; }

    public FailoverMode SupportedFailoverModes { get; protected set; }

    #endregion

    #region Protected Fields

    protected bool EnableScheduling { get; set; }

    protected bool EnableDiscovery { get; set; }

    protected bool EnableHistoryRecovery { get; set; }

    protected EdgeEventType SubscribedEdgeEventTypes { get; set; }

#pragma warning disable CA2227 // Collection properties should be read only
    protected HashSet<DeviceStatus> AutomaticHistoryRecoveryIntervalStates { get; set; } = new HashSet<DeviceStatus> { DeviceStatus.DeviceInError, DeviceStatus.Shutdown };
#pragma warning restore CA2227 // Collection properties should be read only

    protected IAdapterCommonService CommonService => _adapterCommonService;

    #endregion

    #region Private Fields

    private TDataSource DataSourceConfiguration
    {
        get
        {
            return _dataSourceConfiguration;
        }
        set
        {
            _commonServiceDataSourceHandler(value);
            _dataSourceConfiguration = value;
        }
    }

    #endregion

    #region Public Methods

    public Channel<IEdgeEvent> EdgeEventChannel { get; private set; }

    public void Register(CancellationToken cancellationToken)
    {
        _stateChangeSemaphore.Wait(cancellationToken);
        try
        {
            RegisterAdapterInternal();
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public string GetCommandlineHelpHeader(string facet)
    {
        return _adapterCmdHelpService.GetHelpHeaderString(facet);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _stateChangeSemaphore.WaitAsync(cancellationToken);
        try
        {
            RegisterAdapterInternal();

            if (EnableScheduling)
            {
                _scheduleManager = new AdapterScheduleManager<TSelection>(_logger, SampleDataAsync);
            }

            if (EnableDiscovery)
            {
                _dataSourceDiscoveryManager = new DataSourceDiscoveryManager<TDataSource, TSelection>(
                    ComponentId,
                    _applicationManifest.BaseApplicationAddress,
                    _configurationProvider,
                    _logger,
                    DiscoverDataSourceAsync,
                    UpdateDataSelectionConfiguration,
                    () => DataSourceConfiguration);

                _runtimeConfigurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, _dataSourceDiscoveryManager);
            }

            if (EnableHistoryRecovery)
            {
#pragma warning disable CA2000 // History recovery manager owns this object and is in charge of disposing it.
                var onDemandHistoryRecoveryProcessor = new OnDemandHistoryRecoveryProcessor<TDataSource, TSelection>(
                    ComponentId,
                    ComponentType,
                    _logger,
                    _configurationProvider,
                    _edgeDataProtector,
                    _instrumentedMessageProcessor,
                    _healthService,
                    RecoverDataAsync,
                    _applicationManifest.OmfVersion);
#pragma warning restore CA2000 // Dispose objects before losing scope

                var automaticHistoryRecoveryProcessor = new AutomaticHistoryRecoveryProcessor<TDataSource, TSelection>(
                    ComponentId,
                    _logger,
                    _configurationProvider,
                    _healthService,
                    AutomaticHistoryRecoveryIntervalStates,
                    RecoverDataIntervalAsync,
                    GetLastReadTime);

                _historyRecoveryManager = new HistoryRecoveryManager<TDataSource, TSelection>(onDemandHistoryRecoveryProcessor, automaticHistoryRecoveryProcessor);

                _runtimeConfigurationRegistry.RegisterHistoryRecoveryManager(ComponentId, _historyRecoveryManager);
            }

            if (_failoverManager is not null)
            {
                // Failover role must be registered before mode change to avoid changing mode before realizing role is Primary
                _failoverManager.RegisterFailoverRoleChangeCallback(ComponentId, UpdateFailoverRoleAsync);
                _failoverManager.RegisterFailoverModeChangeCallback(ComponentId, UpdateFailoverModeAsync);
                _failoverManager.AddComponentHealthService(ComponentId, _healthService);
            }

            if (string.IsNullOrWhiteSpace(_productVersion))
            {
                _logger.LogWarning("Unable to get adapter version. Defaulting to version {ProductVersion}.", DefaultProductVersion);
                _productVersion = DefaultProductVersion;
            }

            if (ShouldCreateEdgeEventChannel())
            {
                var channelOptions = new BoundedChannelOptions(15)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                };

                EdgeEventChannel = Channel.CreateBounded<IEdgeEvent>(channelOptions);
            }

            await _healthService.InitializeAsync();
            await _diagnosticsService.InitializeAsync();

            PassDataFiltersConfiguration();

            await InitializeAdapterAsync(cancellationToken);

            _initialized = true;
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _stateChangeSemaphore.WaitAsync(cancellationToken);
        try
        {
            await _healthService.StartAsync();
            await _diagnosticsService.StartAsync();

            if (_configurationProvider.TryGetConfiguration<TDataSource>(ComponentId, DataSourceConfigurationName, out var dataSourceConfiguration, out var errors))
            {
                DataSourceConfiguration = dataSourceConfiguration;
                if (EnableHistoryRecovery && dataSourceConfiguration is IHistoryDataSourceConfiguration dataSourceConfigurationWithHistory)
                {
                    if (!_historyRecoveryManager.TryProcessDataSourceUpdate(dataSourceConfigurationWithHistory, out _))
                    {
                        _logger.LogError("Cannot start adapter component {ComponentId} when the {CollectionMode} is set to {CollectionModeValue}.",
                            ComponentId, nameof(IHistoryDataSourceConfiguration.DataCollectionMode), DataCollectionMode.HistoryOnly);
                    }
                    else if (dataSourceConfigurationWithHistory.DataCollectionMode != DataCollectionMode.HistoryOnly)
                    {
                        await StartAdapterInternalAsync(cancellationToken);
                    }
                    else
                    {
                        _logger.LogInformation(UnableToStartComponentDueToHistoryOnlyMessage);
                        PassDataSelectionConfiguration();
                    }
                }
                else
                {
                    await StartAdapterInternalAsync(cancellationToken);
                }
            }
            else
            {
                if (errors.IsNullOrEmpty())
                {
                    _healthService.SendDeviceStatus(DeviceStatus.NotConfigured);
                    _logger.LogInformation(ConfigurationNotFoundMessage, DataSourceString);
                }
                else
                {
                    _healthService.SendDeviceStatus(DeviceStatus.DeviceInError);
                    _logger.LogWarning(ConfigurationInvalidMessage, DataSourceString, errors);
                }
            }
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stateChangeSemaphore.WaitAsync(cancellationToken);
        try
        {
            await StopAdapterInternalAsync(cancellationToken);

            await _healthService.StopAsync();
            await _diagnosticsService.StopAsync();

            if (EnableHistoryRecovery)
            {
                if (_historyRecoveryManager.OnDemandHistoryRecoveryProcessor.Started)
                {
                    _historyRecoveryManager.OnDemandHistoryRecoveryProcessor.Stop();
                }
                else if (_historyRecoveryManager.AutomaticHistoryRecoveryProcessor.Started)
                {
                    _historyRecoveryManager.AutomaticHistoryRecoveryProcessor.Stop();
                }
            }
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public void Unregister(CancellationToken cancellationToken)
    {
        _stateChangeSemaphore.Wait(cancellationToken);
        try
        {
            _runtimeConfigurationRegistry.UnregisterComponent(ComponentId);
            _runtimeAdministrationRegistry.UnregisterComponent(ComponentId);

            if (EnableHistoryRecovery)
            {
                _runtimeConfigurationRegistry.UnregisterHistoryRecoveryManager(ComponentId);
                _historyRecoveryManager?.Dispose();
                _historyRecoveryManager = null;
            }

            if (_initialized)
            {
                UnregisterAdapterAsync(CancellationToken.None).GetAwaiter().GetResult();

                _logger?.LogInformation("{ComponentType} instance '{ComponentId}' has been removed from the system host.", ComponentType, ComponentId);
                _logManager?.RemoveLogger(ComponentId);

                MoveAndRenameAdapterComponentFiles();

                if (_failoverManager is not null)
                {
                    _failoverManager.UnregisterFailoverModeChangeCallback(ComponentId);
                    _failoverManager.UnregisterFailoverRoleChangeCallback(ComponentId);
                    _failoverManager.RemoveComponentHealthService(ComponentId);
                }
            }
            else
            {
                _logManager?.RemoveLogger(ComponentId);
            }
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public void ResendHealthMetadata()
    {
        _healthService?.ResendTypesAndStreams();
        _healthService?.ResendDeviceStatus();
        _diagnosticsService?.ResendTypesAndStreams();
    }

    public void ResendDynamicMetadata()
    {
        _instrumentedMessageProcessor?.ResendTypesAndStreams();
    }

    public async Task ProcessGeneralConfigurationUpdateCallbackAsync(IAdapterGeneralConfiguration oldConfig, IAdapterGeneralConfiguration newConfig)
    {
        if (newConfig == null)
        {
            _instrumentedMessageProcessor.StreamMetadataLevel = MetadataInfo.Medium;
            _instrumentedMessageProcessor.IncludeSourceProperties = StreamProperties.All;
            _adapterCommonService.StreamMetadataLevel = MetadataInfo.Medium;
            _adapterCommonService.IncludeSourceProperties = StreamProperties.All;
        }
        else
        {
            _instrumentedMessageProcessor.StreamMetadataLevel = newConfig.MetadataLevel;
            _instrumentedMessageProcessor.IncludeSourceProperties = newConfig.IncludeSourceProperties;
            _adapterCommonService.StreamMetadataLevel = newConfig.MetadataLevel;
            _adapterCommonService.IncludeSourceProperties = newConfig.IncludeSourceProperties;

            if (newConfig.MetadataLevel >= MetadataInfo.Low && (oldConfig == null || oldConfig.MetadataLevel == MetadataInfo.None))
            {
                _diagnosticsService?.ResendTypesAndStreams();
            }
        }

        if (_adapterStarted)
        {
            await ProcessGeneralConfigurationUpdateAsync(oldConfig, newConfig);
        }
    }

    /// <summary>
    /// Registers a new component configuration facet for the current adapter.
    /// </summary>
    /// <typeparam name="T">The type of configuration object.</typeparam>
    /// <param name="newFacetName">Name of the new configuration facet. <paramref name="newFacetName"/> cannot be null, blank, or contain whitespace.</param>
    /// <param name="configurationChangeCallback">A callback method to inform the adapter of a change in configuration.</param>
    /// <param name="helpCallback">Command-line help output function for the given <paramref name="newFacetName"/>.</param>
    /// <param name="customValidationFunction">Optional custom validation function that gets called before a new configuration object is persisted.</param>
    /// <param name="supportedOperations">Operation(s) supported on the configuration object.</param>
    public void RegisterCustomFacet<T>(
        string newFacetName,
        Action<ConfigurationChangedEventArgs> configurationChangeCallback,
        Func<string> helpCallback,
        Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction = null,
        Operations supportedOperations = Operations.Get | Operations.Create | Operations.Delete | Operations.Update) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(newFacetName, nameof(newFacetName));

        var commandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, newFacetName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<T>(
            ComponentId,
            newFacetName,
            commandGenerator,
            configurationChangeCallback,
            helpCallback,
            customValidationFunction,
            supportedOperations);

        _configurationFileNames.Add(newFacetName);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Internal Methods

    internal async Task UpdateFailoverModeAsync(FailoverMode oldMode, FailoverMode newMode)
    {
        _currentFailoverMode = newMode;

        if (oldMode == newMode)
        {
            return;
        }

        _logger.LogInformation("Failover mode changed from {OldMode} to {NewMode}.", oldMode, newMode);
        _historyRecoveryManager?.UpdateFailoverMode(newMode);

        if (_currentFailoverRole == FailoverRole.Primary)
        {
            await ProcessFailoverModeChangeAsync(oldMode, newMode);
            return;
        }

        await UpdateFailoverModeInternalAsync(oldMode, newMode);
        await ProcessFailoverModeChangeAsync(oldMode, newMode);
    }

    internal async Task UpdateFailoverRoleAsync(FailoverRole oldRole, FailoverRole newRole)
    {
        if (newRole == oldRole)
        {
            return;
        }

        _currentFailoverRole = newRole;
        _historyRecoveryManager?.UpdateFailoverRole(newRole);
        _logger.LogInformation("Failover role changed from {OldRole} to {NewRole}.", oldRole, newRole);

        switch (newRole)
        {
            case FailoverRole.Primary:
                await UpdateFailoverModeInternalAsync(_currentFailoverMode, FailoverMode.NotConfigured);
                break;

            case FailoverRole.Secondary:
                await UpdateFailoverModeInternalAsync(FailoverMode.NotConfigured, _currentFailoverMode);
                break;
        }

        await ProcessFailoverRoleChangeAsync(oldRole, newRole);
    }

    #endregion

    #region Protected Methods

    /// <summary>
    /// This method is called by the adapter framework to register an adapter instance. Custom configuration facets if any must be registered in this method.
    /// </summary>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    /// <remarks>This method can be called multiple times when a new adapter instance is added to the application.</remarks>
    protected abstract Task RegisterAdapterAsync(CancellationToken cancellationToken);

    /// <summary>
    /// This method is called by the adapter framework to initialize an adapter.
    /// </summary>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected abstract Task InitializeAdapterAsync(CancellationToken cancellationToken);

    /// <summary>
    /// This method is called by the adapter framework when the adapter should start.
    /// </summary>
    /// <param name="dataSourceConfiguration">Data source configuration.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected abstract Task StartAdapterAsync(TDataSource dataSourceConfiguration, CancellationToken cancellationToken);

    /// <summary>
    /// This method is called by the adapter framework when the adapter should stop.
    /// </summary>
    /// <param name="dataSourceConfiguration">Data source configuration. Can be null when adapter is stopped due to data source deletion.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected abstract Task StopAdapterAsync(TDataSource dataSourceConfiguration, CancellationToken cancellationToken);

    /// <summary>
    /// This method is called by the adapter framework when the data source configuration has been updated.
    /// </summary>
    /// <param name="oldValue">Old configuration.</param>
    /// <param name="newValue">New Configuration.</param>
    /// <returns>A Task instance.</returns>
    protected abstract Task ProcessDataSourceUpdateAsync(TDataSource oldValue, TDataSource newValue);

    /// <summary>
    /// This method is called by the adapter framework when the data selection items have been updated.
    /// </summary>
    /// <param name="oldValue">Old configuration values.</param>
    /// <param name="newValue">New configuration values</param>
    /// <returns>A Task instance.</returns>
    protected abstract Task ProcessSelectionUpdateAsync(TSelection[] oldValue, TSelection[] newValue);

    /// <summary>
    /// This method is called by the adapter framework when the general configuration has been updated. It only contains items relevant to adapter developers.
    /// </summary>
    /// <param name="oldValue">Old configuration values.</param>
    /// <param name="newValue">New configuration values.</param>
    /// <returns>A Task instance.</returns>
    protected abstract Task ProcessGeneralConfigurationUpdateAsync(IAdapterGeneralConfiguration oldValue, IAdapterGeneralConfiguration newValue);

    /// <summary>
    /// This method is called by the adapter framework when the help message for configuring data source is requested. 
    /// </summary>
    /// <returns>The help message to configure a valid data source.</returns>
    protected abstract string GetDataSourceHelpInfo();

    /// <summary>
    /// This method is called by the adapter framework when the help message for configuring data selections is requested. 
    /// </summary>
    /// <returns>The help message to configure valid data selection items.</returns>
    protected abstract string GetDataSelectionHelpInfo();

    /// <summary>
    /// This method is called by the adapter framework when default stream ID is requested during the data selection configuration pre-validation callback.
    /// </summary>
    /// <param name="selectionItem">Item of <typeparamref name="TSelection"> type to generate the default stream ID for.</typeparamref></param>
    /// <returns>Default stream ID string for <paramref name="selectionItem"/>.</returns>
    protected abstract string GetDefaultStreamId(TSelection selectionItem);

    /// <summary>
    /// This method is called by the adapter framework when failover mode is changed.
    /// </summary>
    /// <param name="oldMode">The old failover mode.</param>
    /// <param name="newMode">The new failover mode.</param>
    protected virtual async Task ProcessFailoverModeChangeAsync(FailoverMode oldMode, FailoverMode newMode)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when failover roles are changed.
    /// </summary>
    /// <param name="oldRole">The old failover role.</param>
    /// <param name="newRole">The new failover role.</param>
    protected virtual async Task ProcessFailoverRoleChangeAsync(FailoverRole oldRole, FailoverRole newRole)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework to allow the adapter a chance to pre-validate the data source configuration.
    /// </summary>
    /// <param name="configurations">Contains the old and new configurations</param>
    /// <returns>A list of validation errors. If the list is blank then the configuration will be applied by the adapter framework.</returns>
    protected virtual ICollection<string> ValidateDataSourceConfiguration(ConfigurationChangedEventArgs configurations)
    {
        return new List<string>();
    }

    /// <summary>
    /// This method is called by the adapter framework to allow the adapter a chance to pre-validate the entire data selection configuration.
    /// </summary>
    /// <param name="configurations">Contains the old and new configurations</param>
    /// <returns>A list of validation errors. If the list is blank then the configuration will be applied by the adapter framework.</returns>
    protected virtual ICollection<string> ValidateDataSelectionConfiguration(ConfigurationChangedEventArgs configurations)
    {
        return new List<string>();
    }

    /// <summary>
    /// This method is called by the adapter framework when it's time to sample data on the data source according to configured schedules.
    /// </summary>
    /// <param name="scheduleId">ID of the current schedule.</param>
    /// <param name="dataSelectionItems">Items that are part of the schedule.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected virtual Task SampleDataAsync(string scheduleId, IReadOnlyList<TSelection> dataSelectionItems, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when a new discovery operation is triggered by a user.
    /// </summary>
    /// <param name="discoveryQuery">Adapter specific discovery query string to limit scope of the discovery.</param>
    /// <param name="dataSourceConfiguration">Configuration of a data source for which discovery process should start.</param>
    /// <param name="discoveryService"><see cref="IDataSourceDiscoveryService{T}"/> instance to push discovered items to and to update discovery progress.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of the discovery operation.</param>
    /// <returns>A task instance.</returns>
    protected virtual Task DiscoverDataSourceAsync(
        string discoveryQuery,
        TDataSource dataSourceConfiguration,
        IDataSourceDiscoveryService<TSelection> discoveryService,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when a new on-demand history recovery operation is triggered by a user.
    /// </summary>
    /// <param name="details">The details of the history recovery operation.</param>
    /// <param name="dataSourceConfiguration">Configuration of a data source for which history recovery process should start.</param>
    /// <param name="dataSelectionItems">Data selection items associated with the component to perform history recovery.</param>
    /// <param name="historyRecoveryService"><see cref="IAdapterHistoryRecoveryService"/> Instance to push recovered items to and to update history recovery progress.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of the history recovery operation.</param>
    /// <returns>A task instance.</returns>
    protected virtual Task RecoverDataAsync(
        HistoryRecoveryDetails details,
        TDataSource dataSourceConfiguration,
        IReadOnlyList<TSelection> dataSelectionItems,
        IAdapterHistoryRecoveryService historyRecoveryService,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// The method is called by the adapter framework when the device status is back to good and there are periods of data needing to be recovered from the data source.
    /// </summary>
    /// <param name="interval"><see cref="Interval"/> to recover data for.</param>
    /// <param name="dataSourceConfiguration">Configuration of a data source for which history recovery process should start.</param>
    /// <param name="dataSelectionItems">Data selection items associated with the component to perform history recovery.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of the history recovery operation.</param>
    /// <returns>A task instance.</returns>
    protected virtual Task RecoverDataIntervalAsync(
        Interval interval,
        TDataSource dataSourceConfiguration,
        IReadOnlyList<TSelection> dataSelectionItems,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when <see cref="IEdgeEvent"/> is raised.
    /// </summary>
    protected virtual void OnEventRaised(IEdgeEvent edgeEvent)
    {
    }

    /// <summary>
    /// The method is called by the adapter framework when the device status changes to InError or Shutdown to allow adapter to persist custom begging of auto-history recovery interval as <see cref="Interval.LastReadTime"/> value.
    /// </summary>
    /// <returns>Custom <see cref="DateTime"/> to persist with the <see cref="Interval"/>.</returns>
    /// <remarks>Note that difference between <see cref="Interval.EndTime"/> and <see cref="Interval.LastReadTime"/> is cap to 4 days and this is enforced by the framework.</remarks>
    protected virtual DateTime? GetLastReadTime() => null;

    /// <summary>
    /// This method is called by the adapter framework when an adapter component is removed from the host.
    /// </summary>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected virtual Task UnregisterAdapterAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Provides an opportunity to dispose any disposable objects.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _eventListenerCts?.Cancel();
            _stateChangeSemaphore?.Dispose();
            _callbackSemaphore?.Dispose();
            _scheduleManager?.Dispose();
            _historyRecoveryManager?.Dispose();
            _diagnosticsService?.Dispose();
            _dataSourceDiscoveryManager?.Dispose();
            _healthService?.Dispose();
            _logManager?.RemoveLogger(ComponentId);
            _logger = null;
            _eventListenerCts?.Dispose();
            _eventListenerCts = null;
        }

        _disposed = true;
    }

    /// <summary>
    /// This method enables adapter developer to migrate schedules configuration and push change to the framework
    /// to make sure it is updated at runtime.
    /// </summary>
    /// <param name="newSchedules">New schedules configuration to push and persist.</param>
    protected void UpdateSchedulesConfiguration(ScheduleConfiguration[] newSchedules)
    {
        ThrowHelper.ThrowIfArgumentNull(newSchedules, nameof(newSchedules));

        if (!_configurationProvider.TrySaveConfiguration(ComponentId, SchedulesFacetName, newSchedules, out var errors))
        {
            _logger.LogError(ConfigurationInvalidMessage, SchedulesFacetName, errors);
            return;
        }

        UpdateSchedulesConfigurationInternal(new ConfigurationChangedEventArgs(null, newSchedules));
    }

    #endregion

    #region Private Methods

    private static void CreateRemovedFolder(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private void RegisterAdapterInternal()
    {
        if (!_registered)
        {
            SetComponentId(_componentIdService);

            CreateAdapterCommonService();

            RegisterAdapterConfigurations();
            RegisterCallbackFunctions();

            RegisterAdapterAsync(CancellationToken.None).GetAwaiter().GetResult();

            _registered = true;
        }
    }

    private void CreateAdapterCommonService()
    {
        _adapterCmdHelpService = new AdapterCmdHelpService(ComponentId);
        _logger = GetAdapterLogger(_logManager);
        _loggerConfigurator = GetAdapterLoggerConfigurator(_logManager);

        _instrumentedMessageProcessor = new InstrumentedMessageProcessor(
            _messageProcessor,
            _logger,
            ComponentId,
            ComponentType);

        _adapterMessageProcessor = new AdapterMessageProcessor(_instrumentedMessageProcessor, _applicationManifest.OmfVersion);

        _healthService = new AdapterHealthService(
            _healthMessageProcessor,
            _logger,
            _applicationManifest,
            ComponentId,
            ComponentType,
            ComponentType,
            _productVersion);

        _diagnosticsService = new AdapterDiagnosticsService(
            _diagnosticsMessageProcessor,
            _logger,
            ComponentId,
            ComponentType,
            _healthService.GetHealthLinkNode(),
            _instrumentedMessageProcessor,
            _applicationManifest.OmfVersion);

        _adapterCommonService = new AdapterCommonService(
            _logger,
            _configurationProvider,
            _adapterMessageProcessor,
            _edgeDataProtector,
            ComponentType,
            ComponentId,
            _healthService);

        _commonServiceDataSourceHandler = _adapterCommonService.DataSourceHandler;
    }

    private async Task StopAdapterCallbackAsync()
    {
        await _callbackSemaphore.WaitAsync();
        try
        {
            if (EnableHistoryRecovery && _historyRecoveryManager.IsOnDemandRecoveryInProgress())
            {
                _logger.LogError("Cannot stop adapter component {ComponentId} when the {CollectionMode} is set to {CollectionModeValue}.",
                    ComponentId, nameof(IHistoryDataSourceConfiguration.DataCollectionMode), DataCollectionMode.HistoryOnly);
            }
            else
            {
                using var cancellationTokenSource = new CancellationTokenSource(WaitTime);
                if (_adapterStarted)
                {
                    await StopAsync(cancellationTokenSource.Token);
                }

                _adapterStoppedFromCallback = true;
            }
        }
        finally
        {
            _callbackSemaphore.Release();
        }
    }

    private async Task StartAdapterCallbackAsync()
    {
        await _callbackSemaphore.WaitAsync();
        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(WaitTime);
            _adapterStoppedFromCallback = false;

            if (!_adapterStarted)
            {
                await StartAsync(cancellationTokenSource.Token);
            }
        }
        finally
        {
            _callbackSemaphore.Release();
        }
    }

    private void RegisterCallbackFunctions()
    {
        _runtimeAdministrationRegistry.RegisterComponentCallbackFunction(ComponentId, StopCallbackName, StopAdapterCallbackAsync);

        _runtimeAdministrationRegistry.RegisterComponentCallbackFunction(ComponentId, StartCallbackName, StartAdapterCallbackAsync);
    }

    private void RegisterAdapterConfigurations()
    {
        var loggerCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, LoggingConfigurationName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<LoggerConfiguration>(
            ComponentId,
            LoggingConfigurationName,
            loggerCommandGenerator,
            UpdateLoggingConfiguration,
            _adapterCmdHelpService.GetLoggingHelpOutput,
            null,
            Operations.Get | Operations.Create | Operations.Update);

        var dataSourceCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, DataSourceConfigurationName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<TDataSource>(
            ComponentId,
            DataSourceConfigurationName,
            dataSourceCommandGenerator,
            UpdateDataSourceConfiguration,
            GetDataSourceHelpInfo,
            ValidateDataSourceConfigurationInternal);

        var dataFiltersCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, DataFiltersConfigurationName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<DataFiltersConfiguration[]>(
            ComponentId,
            DataFiltersConfigurationName,
            dataFiltersCommandGenerator,
            UpdateDataFiltersConfiguration,
            _adapterCmdHelpService.GetDataFiltersHelpInfo);

        var dataSelectionCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, DataSelectionConfigurationName);

        _runtimeConfigurationRegistry.RegisterComponentConfiguration<TSelection[]>(
            ComponentId,
            DataSelectionConfigurationName,
            dataSelectionCommandGenerator,
            UpdateDataSelectionConfiguration,
            GetDataSelectionHelpInfo,
            ValidateDataSelectionConfigurationInternal);

        if (EnableScheduling)
        {
            // Enforce the TSelection to define [Id] attribute to allow for scheduling. 
            if (ConfigurationHelper.GetIdProperty(typeof(TSelection)) == null)
            {
                throw new InvalidOperationException("No [Id] attribute found in data selection configuration. [Id] attribute is required for enabling scheduling. ");
            }

            var schedulesCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, SchedulesConfigurationName);

            _runtimeConfigurationRegistry.RegisterComponentConfiguration<ScheduleConfiguration[]>(
                ComponentId,
                SchedulesConfigurationName,
                schedulesCommandGenerator,
                UpdateSchedulesConfigurationInternal,
                _adapterCmdHelpService.GetSchedulesHelpOutput);
        }

        if (EnableHistoryRecovery)
        {
            var intervalsToRecoverCommandGenerator = new ConfigurationCommandGenerator(_configurationProvider, ComponentId, IntervalsToRecoverConfigurationName);

            _runtimeConfigurationRegistry.RegisterComponentConfiguration<Interval[]>(
                ComponentId,
                IntervalsToRecoverConfigurationName,
                intervalsToRecoverCommandGenerator,
                UpdateIntervalsToRecover,
                null,
                null,
                Operations.Get | Operations.Delete);
        }
    }

    private void UpdateDataSourceConfiguration(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        _stateChangeSemaphore.Wait();
        try
        {
            TDataSource newValue = null;
            TDataSource oldValue = null;

            if (configurationChangeEvent.OldValue is TDataSource typedOldValue)
            {
                oldValue = typedOldValue;
            }

            if (configurationChangeEvent.NewValue is TDataSource typedNewValue)
            {
                newValue = typedNewValue;
            }

            DataSourceConfiguration = newValue;

            if (newValue == null)
            {
                using var cancellationTokenSource = new CancellationTokenSource(WaitTime);
                _logger.LogWarning(DataSourceRemovedLogMessage);

                try
                {
                    StopAdapterInternalAsync(cancellationTokenSource.Token).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, UnableToStopMessage);
                }
            }
            else if (oldValue == null)
            {
                if (EnableHistoryRecovery && newValue is IHistoryDataSourceConfiguration { DataCollectionMode: DataCollectionMode.HistoryOnly })
                {
                    _logger.LogInformation(UnableToStartComponentDueToHistoryOnlyMessage);
                }
                else
                {
                    _logger.LogInformation(DataSourceAddedLogMessage);
                    using var cancellationTokenSource = new CancellationTokenSource(WaitTime);
                    StartAdapterInternalAsync(cancellationTokenSource.Token).GetAwaiter().GetResult();
                }
            }
            else
            {
                _logger.LogInformation(DataSourceEditedLogMessage);
                var newValueIsHistoryOnlyMode = EnableHistoryRecovery && newValue is IHistoryDataSourceConfiguration { DataCollectionMode: DataCollectionMode.HistoryOnly };
                if (_adapterStarted || newValueIsHistoryOnlyMode)
                {
                    if (_adapterStarted && newValueIsHistoryOnlyMode)
                    {
                        _logger.LogInformation("Data collection mode is set to HistoryOnly. The adapter component will be stopped.");
                        using var cancellationTokenSource = new CancellationTokenSource(WaitTime);

                        try
                        {
                            StopAdapterInternalAsync(cancellationTokenSource.Token).GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, UnableToStopMessage);
                        }
                    }

                    if (!string.Equals(oldValue.StreamIdPrefix, newValue.StreamIdPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("{Property} value has changed. Restart the adapter component to apply the change. Note: New streams might get created as a result.", nameof(newValue.StreamIdPrefix));
                    }

                    ProcessDataSourceUpdateAsync(oldValue, newValue);
                }
                else if (EnableHistoryRecovery && oldValue is IHistoryDataSourceConfiguration { DataCollectionMode: DataCollectionMode.HistoryOnly })
                {
                    _logger.LogInformation("Data collection mode is set to non-HistoryOnly. The adapter component will be started.");
                    using var cancellationTokenSource = new CancellationTokenSource(WaitTime);
                    StartAdapterInternalAsync(cancellationTokenSource.Token).GetAwaiter().GetResult();
                }
            }
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    private void UpdateDataSelectionConfiguration(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        TSelection[] newValue = default;
        TSelection[] oldValue = default;

        if (configurationChangeEvent.OldValue is TSelection[] typedOldValue)
        {
            oldValue = typedOldValue;
        }

        if (configurationChangeEvent.NewValue is TSelection[] typedNewValue)
        {
            newValue = typedNewValue;
        }

        if (EnableHistoryRecovery && _historyRecoveryManager != null)
        {
            _historyRecoveryManager.ProcessDataSelectionUpdate(newValue);
        }

        if (!_adapterStarted)
        {
            return;
        }

        try
        {
            ProcessSelectionUpdateAsync(oldValue, newValue).GetAwaiter().GetResult();
            _healthService.SetDataSelectionCount(newValue?.Where(x => x.Selected).Count() ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, UnableToUpdateDataSelectionMessage);
        }

        if (EnableScheduling)
        {
            _scheduleManager.ProcessDataSelectionChanges(newValue);
        }

        if (oldValue != null)
        {
            _instrumentedMessageProcessor.ProcessDataSelectionConfigurationChanges(newValue);
        }
    }

    private void UpdateSchedulesConfigurationInternal(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        if (!_adapterStarted)
        {
            return;
        }

        ScheduleConfiguration[] newValue = null;
        if (configurationChangeEvent.NewValue is ScheduleConfiguration[] typedNewValue)
        {
            newValue = typedNewValue;
        }

        _scheduleManager.ProcessSchedulesConfigurationChanges(newValue);
    }

    private void UpdateIntervalsToRecover(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        if (!_adapterStarted)
        {
            return;
        }

        Interval[] newValue = null;
        if (configurationChangeEvent.NewValue is Interval[] typedNewValue)
        {
            newValue = typedNewValue;
        }

        _historyRecoveryManager.AutomaticHistoryRecoveryProcessor.ProcessIntervalsToRecoverChanges(newValue);
    }

    private void UpdateDataFiltersConfiguration(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        DataFiltersConfiguration[] newValue = null;
        if (configurationChangeEvent.NewValue is DataFiltersConfiguration[] typedNewValue)
        {
            newValue = typedNewValue;
        }

        _adapterMessageProcessor?.ProcessDataFiltersConfigurationChanges(newValue);
    }

    private void PassDataSelectionConfiguration()
    {
        if (_configurationProvider.TryGetConfiguration<TSelection[]>(ComponentId, DataSelectionConfigurationName, out var dataSelectionConfiguration, out var errors))
        {
            UpdateDataSelectionConfiguration(new ConfigurationChangedEventArgs(null, dataSelectionConfiguration));
        }
        else
        {
            if (errors.IsNullOrEmpty())
            {
                _healthService.SendDeviceStatus(DeviceStatus.NotConfigured);
                _logger.LogInformation(ConfigurationNotFoundMessage, DataSelectionString);
            }
            else
            {
                _healthService.SendDeviceStatus(DeviceStatus.DeviceInError);
                _logger.LogWarning(ConfigurationInvalidMessage, DataSelectionString, errors);
            }
        }
    }

    private void PassSchedulesConfiguration()
    {
        if (_configurationProvider.TryGetConfiguration<ScheduleConfiguration[]>(ComponentId, SchedulesConfigurationName, out var schedulesConfiguration, out var errors))
        {
            UpdateSchedulesConfigurationInternal(new ConfigurationChangedEventArgs(null, schedulesConfiguration));
        }
        else
        {
            if (errors.IsNullOrEmpty())
            {
                var defaultScheduleConfig = new[] { new ScheduleConfiguration { Id = "1", Period = TimeSpan.FromSeconds(5), Offset = TimeSpan.Zero } };
                _configurationProvider.TrySaveConfiguration(ComponentId, SchedulesConfigurationName, defaultScheduleConfig, out _);
                UpdateSchedulesConfigurationInternal(new ConfigurationChangedEventArgs(null, defaultScheduleConfig));
            }
            else
            {
                _healthService.SendDeviceStatus(DeviceStatus.DeviceInError);
                _logger.LogWarning(ConfigurationInvalidMessage, SchedulesFacetName, errors);
            }
        }
    }

    private void PassDataFiltersConfiguration()
    {
        if (_configurationProvider.TryGetConfiguration<DataFiltersConfiguration[]>(ComponentId, DataFiltersConfigurationName, out var filtersConfiguration, out var errors))
        {
            UpdateDataFiltersConfiguration(new ConfigurationChangedEventArgs(null, filtersConfiguration));
        }
        else
        {
            if (errors.IsNullOrEmpty())
            {
                DataFiltersConfiguration[] defaultConfig = new[]
                {
                    new DataFiltersConfiguration
                    {
                        Id = "DuplicateData",
                        AbsoluteDeadband = 0,
                        PercentChange = null,
                        ExpirationPeriod = new TimeSpan(1, 0, 0),
                    },
                };

                _configurationProvider.TrySaveConfiguration(ComponentId, DataFiltersConfigurationName, defaultConfig, out _);

                UpdateDataFiltersConfiguration(new ConfigurationChangedEventArgs(null, defaultConfig));
            }
            else
            {
                _healthService.SendDeviceStatus(DeviceStatus.DeviceInError);
                _logger.LogWarning(ConfigurationInvalidMessage, DataFiltersFacetName, errors);
            }
        }
    }

    private async Task StartAdapterInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            _healthService.ActivateDeviceStatusUpdates();

            if (_eventListenerTask == null && IsSubscribedToEdgeEvents())
            {
                _eventListenerTask = Task.Run(async () =>
                {
                    try
                    {
                        await ListenEventsListenerAsync(_eventListenerCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Events listener encountered an unexpected error and stopped.");
                    }
                }, cancellationToken);
            }

            if (DataSourceConfiguration != null && !_adapterStarted
                && !_adapterStoppedFromCallback && (_currentFailoverRole == FailoverRole.Primary || _currentFailoverMode != FailoverMode.Cold))
            {
                _healthService.SendDeviceStatus(DeviceStatus.Starting);

                _instrumentedMessageProcessor.SetStreamIdPrefix(_adapterCommonService.StreamIdPrefix);

                if (!string.IsNullOrWhiteSpace(_adapterCommonService.StreamIdPrefix))
                {
                    _logger.LogInformation("Adapter is using Stream ID Prefix '{PrefixString}'.", _adapterCommonService.StreamIdPrefix);
                }

                await StartAdapterAsync(DataSourceConfiguration, cancellationToken);

                _adapterStarted = true;

                if (EnableScheduling)
                {
                    PassSchedulesConfiguration();
                }

                PassDataSelectionConfiguration();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, UnableToStartMessage);
        }
    }

    private async Task StopAdapterInternalAsync(CancellationToken cancellationToken)
    {
        if (_adapterStarted)
        {
            if (EnableScheduling)
            {
                if (_currentFailoverRole != FailoverRole.Primary && _currentFailoverMode == FailoverMode.Cold)
                {
                    _scheduleManager.Disable();
                }
                else
                {
                    _scheduleManager.ClearAllSchedules();
                }
            }

            await StopAdapterAsync(DataSourceConfiguration, cancellationToken);

            _healthService.UpdateDeviceStatusAndSuppress(DeviceStatus.Shutdown);
            _instrumentedMessageProcessor.ClearCounters();
            _instrumentedMessageProcessor.ClearStreamsCollection();

            _adapterStarted = false;
        }
    }

    private void SetComponentId(IComponentIdService componentIdService)
    {
        var componentId = componentIdService.GetEdgeComponentId(ComponentType);

        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        ComponentId = componentId;
    }

    private void UpdateLoggingConfiguration(ConfigurationChangedEventArgs configurationChangeEvent)
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

    private ICollection<string> ValidateDataSourceConfigurationInternal(ConfigurationChangedEventArgs configurations)
    {
        if (EnableHistoryRecovery && configurations.NewValue is IHistoryDataSourceConfiguration typedNewValue)
        {
            if (_historyRecoveryManager != null)
            {
                if (!_historyRecoveryManager.TryProcessDataSourceUpdate(typedNewValue, out var errorMessage))
                {
                    return new List<string>() { $"Failed to update data source configuration: {errorMessage}" };
                }
            }
        }

        return ValidateDataSourceConfiguration(configurations);
    }

    private ICollection<string> ValidateDataSelectionConfigurationInternal(ConfigurationChangedEventArgs configurations)
    {
        var errors = new List<string>();

        if (configurations?.NewValue is TSelection[] dataSelectionItems)
        {
            var filterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_configurationProvider.TryGetConfiguration<DataFiltersConfiguration[]>(ComponentId, DataFiltersConfigurationName, out var filtersConfiguration, out _))
            {
                foreach (var filterConfiguration in filtersConfiguration)
                {
                    filterIds.Add(filterConfiguration.Id);
                }
            }

            var dataFilterConfigurationWarnings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (TSelection selectionItem in dataSelectionItems)
            {
                if (!string.IsNullOrWhiteSpace(selectionItem.DataFilterId))
                {
                    if (!filterIds.Contains(selectionItem.DataFilterId))
                    {
                        var error = $"No data filter configuration associated with data filter ID '{selectionItem.DataFilterId}'.";
                        dataFilterConfigurationWarnings.TryAdd(selectionItem.DataFilterId, error);
                    }
                }

                if (string.IsNullOrWhiteSpace(selectionItem.StreamId))
                {
                    selectionItem.StreamId = GetDefaultStreamId(selectionItem);
                }

                if (selectionItem.StreamId.ToOmfIdentifier().Length > MaximumStreamIdLengthNoPrefix)
                {
                    errors.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        MaximumIdentifierLengthExceededError,
                        nameof(DataSelectionConfigurationBase.StreamId),
                        MaximumStreamIdLengthNoPrefix));
                }
            }

            if (dataFilterConfigurationWarnings.Count > 0)
            {
                // log a message for each dataFilterId that user configured on dataSelection item but did not configure in dataFilters facet.
                _logger?.LogWarning("{Filters}{NewLine}Data Filtering will start on the data selection item once the data filters configuration has been added.",
                    string.Join("; ", dataFilterConfigurationWarnings.Values), Environment.NewLine);
            }
        }

        errors.AddRange(ValidateDataSelectionConfiguration(configurations));

        return errors;
    }

    private IInstrumentedLogger GetAdapterLogger(ILogManager logManager)
    {
        var logger = logManager.GetOrCreateInstrumentedLogger(ComponentId);

        return logger;
    }

    private ILoggerConfigurator GetAdapterLoggerConfigurator(ILogManager logManager)
    {
        var loggerConfigurator = logManager.GetLoggerConfigurator(ComponentId);
        return loggerConfigurator;
    }

    private void MoveAndRenameAdapterComponentFiles()
    {
        var basePath = _configurationProvider.GetCommonApplicationDataDirectoryPath();

        var configFilesMoveResult = MoveAndRenameFilesByFolderName(ConfigurationDirectoryName, basePath);
        var logFilesMoveResult = MoveAndRenameFilesByFolderName(LoggingDirectoryName, basePath);

        _healthService?.SendDeviceStatus(DeviceStatus.Removed);

        if (configFilesMoveResult || logFilesMoveResult)
        {
            var systemLogger = _logManager?.GetOrCreateLogger(SystemLogSourceName);
            systemLogger?.LogInformation("The {ComponentType} instance '{ComponentId}' has been removed. The configuration and log files for this component have been renamed and moved to their respective {RemovedFolderName} folder.",
                ComponentType, ComponentId, RemovedDirectoryName);
        }
    }

    private bool MoveAndRenameFilesByFolderName(string folderName, string basePath)
    {
        var searchPath = string.Empty;

        try
        {
            searchPath = Path.Combine(basePath, folderName);

            if (Directory.Exists(searchPath))
            {
                var files = FindFiles(folderName, basePath);

                if (files.Count > 0)
                {
                    var removalPath = Path.Combine(searchPath, RemovedDirectoryName);
                    CreateRemovedFolder(removalPath);
                    MoveAndRenameFiles(files, searchPath, RemovedDirectoryName);

                    return true;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            var systemLogger = _logManager?.GetOrCreateLogger(SystemLogSourceName);
            systemLogger?.LogTrace("The application does not have adequate permissions to the {SearchPath} directory to move and rename {FolderName} files for removed adapter component '{ComponentId}'.",
                searchPath, folderName, ComponentId);
            return false;
        }
        catch (Exception e)
        {
            var systemLogger = _logManager?.GetOrCreateLogger(SystemLogSourceName);
            systemLogger?.LogTrace(e, "The following exception occurred while trying to move and rename {FolderName} files for removed adapter component '{ComponentId}'.", folderName, ComponentId);
            return false;
        }

        return false;
    }

    private void MoveAndRenameFiles(IEnumerable<string> files, string targetPath, string folderName)
    {
        var systemLogger = _logManager.GetOrCreateLogger(SystemLogSourceName);
        var currentTimeString = DateTime.Now.ToString(TimeFormatString, CultureInfo.InvariantCulture);

        foreach (var file in files)
        {
            if (file == null)
            {
                continue;
            }

            try
            {
                var fileName = Path.GetFileName(file) + RemovalAppendString + currentTimeString + TxtSuffix;
                var fileNewPath = Path.Combine(targetPath, folderName, fileName);
                File.Move(file, fileNewPath);
            }
            catch (Exception e)
            {
                var filePrintable = new Uri(file);
                systemLogger.LogWarning(e, "Failed to move file {FileName} with the following exception. The file was not renamed nor moved.", filePrintable);
            }
        }
    }

    private List<string> FindFiles(string folderName, string searchPath)
    {
        var resultFiles = new List<string>();
        searchPath = Path.Combine(searchPath, folderName);

        switch (folderName)
        {
            case ConfigurationDirectoryName:
                foreach (var foundFiles in _configurationFileNames.Select(file => ComponentId + Underscore + file + JsonSuffix).Select(searchPattern => Directory.GetFiles(searchPath, searchPattern)))
                {
                    resultFiles.AddRange(foundFiles);
                }

                var discoveryResultsSearchPattern = ComponentId + Underscore + DiscoveryResultString + Underscore + DiscoveryResultWildcard + JsonSuffix;
                var discoveryResultFiles = Directory.GetFiles(searchPath, discoveryResultsSearchPattern);
                resultFiles.AddRange(discoveryResultFiles);
                break;

            case LoggingDirectoryName:
                var regex = new Regex(ComponentId + Hyphen + LogRegex + TxtSuffix);
                var matchedFiles = Directory.GetFiles(searchPath).Where(x => regex.IsMatch(x));
                resultFiles.AddRange(matchedFiles);
                break;
        }

        return resultFiles;
    }

    private async Task UpdateFailoverModeInternalAsync(FailoverMode oldMode, FailoverMode newMode)
    {
        switch (newMode)
        {
            case FailoverMode.NotConfigured:
            case FailoverMode.Hot:
                if (EnableScheduling && (oldMode == FailoverMode.Warm || oldMode == FailoverMode.Cold))
                {
                    _scheduleManager.Enable();

                    // Logging message here instead of in schedule manager `StartAllSchedules` to avoid repetitive logging
                    _logger.LogInformation("All schedules have been started.");
                }

                await StartAdapterInternalAsync(CancellationToken.None);
                break;

            case FailoverMode.Warm:
                if (EnableScheduling)
                {
                    _scheduleManager.Disable();

                    // Logging message here instead of in schedule manager `StopAllSchedules` to avoid repetitive logging
                    _logger.LogInformation("All schedules have been stopped.");
                }

                await StartAdapterInternalAsync(CancellationToken.None);
                break;

            case FailoverMode.Cold:
                await StopAdapterInternalAsync(CancellationToken.None);
                break;
        }
    }

    private async Task ListenEventsListenerAsync(CancellationToken token)
    {
        var reader = EdgeEventChannel.Reader;
        while (await reader.WaitToReadAsync(token))
        {
            while (reader.TryRead(out var evt))
            {
                if (!_adapterStarted)
                {
                    continue;
                }

                try
                {
                    if (SubscribedEdgeEventTypes.HasFlag(evt.EventType))
                    {
                        OnEventRaised(evt);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Adapter failed to process an event.");
                }
            }
        }
    }

    private bool IsSubscribedToEdgeEvents() 
    {
        return SubscribedEdgeEventTypes.HasFlag(EdgeEventType.Egress)
            || SubscribedEdgeEventTypes.HasFlag(EdgeEventType.Buffering);
    }

    private bool ShouldCreateEdgeEventChannel()
    {
        return _edgeEventProvider != null && EdgeEventChannel == null && IsSubscribedToEdgeEvents();
    }

    #endregion
}
