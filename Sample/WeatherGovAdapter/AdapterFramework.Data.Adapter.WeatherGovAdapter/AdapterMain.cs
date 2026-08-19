// Copyright 2026 AVEVA Group Limited
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

using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Discovery;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Interfaces;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.AdapterCommon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter;

/// <summary>
/// Main entry point for the WeatherGov Adapter.
/// This class orchestrates lifecycle, configuration, and service coordination.
/// </summary>
public partial class AdapterMain : AdapterMainBase<DataSourceConfiguration, DataSelectionItem>
{
    private readonly ILogManager _logManager;
    private ILogger _logger;

    private readonly IMetadataService _metadataService;
    private readonly IMeasurementProcessor _measurementProcessor;

    // Caps concurrent station requests within a single sampling pass. Sized from
    // DataSourceConfiguration.MaxConcurrentRequests when the adapter first sees a configuration, then
    // fixed for the component's lifetime (a cap change takes effect on restart), because in-flight
    // sampling passes hold permits on the instance and it cannot be safely swapped mid-flight.
    private SemaphoreSlim _parallelLimiter;

    // Adapter-owned factory seam used to create WeatherGovClient instances. Default implementation wraps
    // the framework IHttpClientFactory, matching the factory-by-interface pattern used by other adapters.
    private readonly IWeatherGovClientFactory _weatherGovClientFactory;

    // Client for the active data source; rebuilt on data-source updates. Volatile so the atomic
    // reference swap performed on a config-update thread is immediately visible to sampling passes
    // without a lock - the factory owns handler lifetime, so no HttpClient is ever disposed mid-request.
    private volatile WeatherGovClient _activeWeatherGovClient;

    private bool _disposed;

    /// <summary>
    /// Adapter constructor called by the framework.
    /// Responsible for wiring dependencies and initializing services.
    /// </summary>
    public AdapterMain(
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
        IHttpClientFactory httpClientFactory)
        : this(
            logManager,
            configurationProvider,
            messageProcessor,
            applicationManifest,
            runtimeConfigurationRegistry,
            edgeDataProtector,
            componentIdService,
            healthMessageProcessor,
            diagnosticsMessageProcessor,
            runtimeAdministrationRegistry,
            httpClientFactory,
            metadataService: null)
    {
    }

    /// <summary>
    /// Test-focused constructor that accepts an <see cref="IMetadataService"/> so unit tests can inject a
    /// substitute and assert the adapter's framework writes. Kept <c>internal</c> so the framework DI
    /// container keeps selecting the public constructor; production passes <see langword="null"/>, which
    /// builds the real service that reads framework services lazily via CommonService.
    /// </summary>
    internal AdapterMain(
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
        IHttpClientFactory httpClientFactory,
        IMetadataService metadataService,
        IWeatherGovClientFactory weatherGovClientFactory = null)
        : base(
            logManager,
            configurationProvider,
            messageProcessor,
            applicationManifest,
            runtimeConfigurationRegistry,
            edgeDataProtector,
            componentIdService,
            healthMessageProcessor,
            diagnosticsMessageProcessor,
            runtimeAdministrationRegistry)
    {
        _logManager = logManager;
        _weatherGovClientFactory = weatherGovClientFactory ?? new WeatherGovClientFactory(
            httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)));
        _logger = NullLogger.Instance;

        // Metadata service uses delegates instead of direct CommonService access. Tests inject a
        // substitute; production builds the real service (the delegates are evaluated lazily).
        var _measurementMapper = new WeatherMeasurementMapper();
        _metadataService = metadataService ?? new MetadataService(
            () => CommonService?.MessageProcessor as IAdapterMessageProcessor,
            () => CommonService?.GetAdapterDataTypeId("WeatherObservationMeasurement"),
            _measurementMapper
        );
        _measurementProcessor = new MeasurementProcessor(_measurementMapper);

        // Enable built-in features. The framework scheduler drives sampling via SampleDataAsync.
        EnableDiscovery = true;
        EnableScheduling = true;
    }

    /// <summary>
    /// Invoked by the framework host during service registration so the adapter can register the
    /// dependencies it needs injected. Registers the Weather.gov <see cref="IHttpClientFactory"/> client
    /// that the constructor consumes.
    /// </summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="dependencies">Framework services made available to components during registration.</param>
    public static void AddComponent(IServiceCollection services, IReadOnlyDictionary<Type, object> dependencies)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddWeatherGovHttpClient();
    }

    /// <summary>
    /// Adapter component type identifier.
    /// </summary>
    public override string ComponentType => AdapterConstants.ComponentType;

    /// <summary>
    /// Gets or sets the active data source configuration snapshot. Exposed as a protected
    /// seam so tests can set it without running the adapter lifecycle.
    /// </summary>
    protected DataSourceConfiguration ActiveDataSource { get; set; }

    // Resolves the component logger, falling back to the current logger (NullLogger.Instance until the
    // adapter starts) when no LogManager is available. Shared by the lifecycle and discovery entry points.
    private ILogger ResolveLogger() => _logManager?.GetOrCreateLogger(ComponentId, null) ?? _logger;

    // Builds a WeatherGovClient through the adapter's client-factory seam. Shared by start,
    // data-source update, and discovery.
    private WeatherGovClient CreateClient(DataSourceConfiguration config, ILogger logger) =>
        _weatherGovClientFactory.Create(config, logger);

    // Sizes the concurrency limiter from configuration the first time a data source is seen, then leaves
    // it fixed (first config wins). Created lazily so both StartAdapterAsync and the first data-source
    // update establish it before any sampling pass reaches ProcessStation.
    private void EnsureParallelLimiter(DataSourceConfiguration config) =>
        _parallelLimiter ??= new SemaphoreSlim(config.MaxConcurrentRequests);


    /// <summary>
    /// Called when adapter is registered with the framework.
    /// Used to set up default stream ID patterns and metadata.
    /// </summary>
    protected override Task RegisterAdapterAsync(CancellationToken cancellationToken)
    {
        StreamIdGenerator.SetDefaultStreamIdPattern(
            AdapterConstants.DefaultStreamIdPattern,
            AdapterConstants.DefaultStreamIdKeywords);

        _metadataService.EnsureTypeRegistered();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called once to initialize adapter resources.
    /// </summary>
    protected override Task InitializeAdapterAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the adapter starts. Creates the API client; the framework scheduler
    /// drives sampling via <see cref="SampleDataAsync"/>.
    /// </summary>
    protected override Task StartAdapterAsync(
        DataSourceConfiguration config,
        CancellationToken cancellationToken)
    {
        _logger = ResolveLogger();

        ActiveDataSource = config;
        EnsureParallelLimiter(config);

        _logger.LogInformation(
            "Starting WeatherGov adapter. ComponentId={ComponentId}",
            ComponentId);

        // Surface an auditable signal when an insecure (plaintext HTTP) base URL is actually in effect.
        // AllowInsecureBaseUrl is meant only for local development and must never be used in production.
        if (config.AllowInsecureBaseUrl &&
            Uri.TryCreate(config.BaseUrl, UriKind.Absolute, out var baseUri) &&
            baseUri.Scheme == Uri.UriSchemeHttp)
        {
            _logger.LogWarning(
                "AllowInsecureBaseUrl is enabled and BaseUrl '{BaseUrl}' uses plaintext HTTP; this must not be used in production.",
                config.BaseUrl);
        }

        // The factory (injected by the host) owns HttpClient lifetime; build the client from a pooled one.
        _activeWeatherGovClient = CreateClient(config, _logger);

        // Seed the collection cadences the adapter offers before the framework seeds its generic
        // 5-second default, so schedules "1"/"2"/"3" fire at their documented 1/5/10 minute periods.
        SeedDefaultSchedules();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Ensures the collection schedules this adapter offers exist in the Schedules facet. Runs before
    /// the framework seeds its generic 5-second default, so schedules "1"/"2"/"3" collect at the
    /// documented 1/5/10 minute cadences instead, and items assigned to "2"/"3" are actually sampled.
    /// Existing (user-configured) schedules are respected.
    /// </summary>
    private void SeedDefaultSchedules()
    {
        var existing = GetConfiguredSchedules();

        var defaults = ResolveSchedulesToSeed(existing);

        if (defaults is null)
        {
            return;
        }

        _logger.LogInformation(
            "No schedules configured. Seeding {Count} default collection schedule(s).",
            defaults.Length);

        ApplySchedules(defaults);
    }

    /// <summary>
    /// Reads the schedules currently present in the Schedules facet. Exposed as a protected virtual
    /// seam (mirroring <see cref="StreamIdGenerator"/>) so tests can drive the adapter lifecycle
    /// without a live CommonService.
    /// </summary>
    /// <returns>The configured schedules, or an empty array when none are present.</returns>
    protected virtual ScheduleConfiguration[] GetConfiguredSchedules() =>
        CommonService.ConfigurationProvider
            .GetArrayConfiguration<ScheduleConfiguration>(CommonConstants.SchedulesConfigurationName);

    /// <summary>
    /// Writes the given schedules into the Schedules facet. Exposed as a protected virtual seam
    /// (the write counterpart to <see cref="GetConfiguredSchedules"/>) so the seeding decision can be
    /// verified without a live CommonService, and so an adapter author sees both sides of the seam.
    /// </summary>
    /// <param name="schedules">The schedules to write.</param>
    protected virtual void ApplySchedules(ScheduleConfiguration[] schedules) =>
        UpdateSchedulesConfiguration(schedules);

    /// <summary>
    /// Decides which schedules, if any, should be seeded into the Schedules facet. Returns the
    /// default collection schedules when none are already configured, or <see langword="null"/>
    /// when existing (user-configured) schedules are present and should be respected.
    /// </summary>
    /// <param name="existing">The schedules currently present in the Schedules facet, if any.</param>
    /// <returns>The schedules to seed, or <see langword="null"/> to skip seeding.</returns>
    internal static ScheduleConfiguration[] ResolveSchedulesToSeed(ScheduleConfiguration[] existing) =>
        existing is { Length: > 0 } ? null : BuildDefaultSchedules();

    /// <summary>
    /// Builds one <see cref="ScheduleConfiguration"/> per entry in
    /// <see cref="AdapterConstants.SchedulePeriods"/> so each selectable schedule ID has its
    /// documented collection cadence.
    /// </summary>
    internal static ScheduleConfiguration[] BuildDefaultSchedules() =>
        AdapterConstants.SchedulePeriods
            .OrderBy(period => period.Key, StringComparer.OrdinalIgnoreCase)
            .Select(period => new ScheduleConfiguration
            {
                Id = period.Key,
                Period = period.Value,
                Offset = TimeSpan.Zero,
            })
            .ToArray();

    /// <summary>
    /// Called when the adapter is stopped. Disposes the API client.
    /// </summary>
    protected override Task StopAdapterAsync(
        DataSourceConfiguration config,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Stopping WeatherGov adapter. ComponentId={ComponentId}",
            ComponentId);

        // Nothing to dispose here: the factory (released in Dispose) owns handler lifetime, and the
        // volatile clear is immediately visible to any in-flight sampling pass, which then no-ops.
        _activeWeatherGovClient = null;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when data source configuration changes. Swaps in a client built from the new
    /// configuration; the framework continues driving sampling on the existing schedules.
    /// </summary>
    protected override Task ProcessDataSourceUpdateAsync(
        DataSourceConfiguration oldValue,
        DataSourceConfiguration newValue)
    {
        _logger = ResolveLogger();

        // Skip the rebuild when nothing that affects the client changed (matches the reference adapters).
        if (oldValue != null && oldValue.Equals(newValue))
        {
            _logger.LogInformation("Data source configuration unchanged. Keeping current client.");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Data source updated. Rebuilding client.");

        // Set ActiveDataSource before the volatile _activeWeatherGovClient write so a sampling pass that observes the
        // new client also observes the matching configuration. The factory owns handler lifetime, so
        // there is nothing to dispose and no need to drain in-flight sampling.
        ActiveDataSource = newValue;
        EnsureParallelLimiter(newValue);
        _activeWeatherGovClient = CreateClient(newValue, _logger);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when selection configuration changes. The framework routes updated items to the
    /// matching schedule, so no adapter-side selection bookkeeping is required.
    /// </summary>
    protected override Task ProcessSelectionUpdateAsync(
        DataSelectionItem[] oldValue,
        DataSelectionItem[] newValue)
    {
        _logger.LogInformation("Selection updated.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the framework scheduler when it is time to sample the stations that belong to
    /// <paramref name="scheduleId"/>. Fans out across stations under a concurrency cap.
    /// </summary>
    protected override async Task SampleDataAsync(
        string scheduleId,
        IReadOnlyList<DataSelectionItem> items,
        CancellationToken cancellationToken)
    {
        if (items == null || items.Count == 0)
        {
            return;
        }

        // Snapshot the volatile client reference for this pass. A concurrent data-source update swaps in
        // a new client atomically; because the factory owns handler lifetime, an in-flight pass keeps
        // using its snapshot safely, and overlapping schedules sample concurrently (no shared gate).
        var client = _activeWeatherGovClient;
        if (client == null)
        {
            _logger.LogWarning(
                "Sample requested for schedule {ScheduleId} before the client was ready. Skipping.",
                scheduleId);
            return;
        }

        var tasks = items.Select(item => ProcessStation(client, item, cancellationToken));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches and processes the latest observation for a single station.
    /// </summary>
    private async Task ProcessStation(
        WeatherGovClient client,
        DataSelectionItem item,
        CancellationToken cancellationToken)
    {
        await _parallelLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using (_logger.BeginScope("Station:{StationId}", item.StationId))
            {
                var obs = await client.GetLatestObservation(item.StationId, cancellationToken).ConfigureAwait(false);

                if (obs?.Properties == null)
                {
                    _logger.LogWarning("Invalid observation payload.");
                    return;
                }

                var measurement = _measurementProcessor.Process(obs, item, _logger);

                if (measurement == null)
                {
                    return;
                }

                if (ActiveDataSource is { DropNullMeasurements: true } &&
                    !measurement.HasAtLeastOneNonTimestampValue())
                {
                    _logger.LogDebug("Dropping empty measurement.");
                    return;
                }

                _metadataService.Write(item, measurement, _logger);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Station {StationId} sampling failed.", item.StationId);
        }
        finally
        {
            _parallelLimiter.Release();
        }
    }

    /// <summary>
    /// Called when general adapter configuration changes.
    /// </summary>
    protected override Task ProcessGeneralConfigurationUpdateAsync(
        IAdapterGeneralConfiguration oldValue,
        IAdapterGeneralConfiguration newValue)
    {
        // Optional: resend metadata if needed
        return Task.CompletedTask;
    }


    /// <summary>
    /// Called by the framework when a discovery operation is triggered.
    /// Surfaces explicitly configured station IDs and stations resolved from seed points,
    /// pushing each as a data selection item via <paramref name="discoveryService"/>.
    /// </summary>
    protected override async Task DiscoverDataSourceAsync(
        string discoveryQuery,
        DataSourceConfiguration dataSourceConfiguration,
        IDataSourceDiscoveryService<DataSelectionItem> discoveryService,
        CancellationToken cancellationToken)
    {
        var logger = ResolveLogger();
        var discovery = new WeatherGovDiscovery(logger, GetDefaultStreamId);

        // Track station IDs already seen so duplicates across seed points are skipped.
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Count of seed-resolved candidates skipped because they have no working observation endpoint.
        var skipped = 0;

        // Resolve seed points (from the discovery query) into nearby stations.
        var seedPoints = discovery.ParseQuerySeedPoints(discoveryQuery);
        if (seedPoints.Count == 0)
        {
            discoveryService.UpdateProgress(100);
            logger.LogInformation("Discovery completed. {Count} station(s) surfaced.", discovered.Count);
            return;
        }

        var client = CreateClient(dataSourceConfiguration, logger);

        for (var i = 0; i < seedPoints.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = seedPoints[i];

            try
            {
                var stationsUrl = await client
                    .GetObservationStationsUrl(seed.Latitude, seed.Longitude, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(stationsUrl))
                {
                    logger.LogWarning(
                        "No observation stations URL returned for seed point {Lat},{Lon}.",
                        seed.Latitude, seed.Longitude);
                }
                else
                {
                    var stations = await client.GetStations(stationsUrl, cancellationToken).ConfigureAwait(false);
                    foreach (var feature in stations?.Features ?? new List<StationFeature>())
                    {
                        var stationId = feature?.Properties?.StationIdentifier;
                        if (string.IsNullOrWhiteSpace(stationId) || !discovered.Add(stationId.Trim()))
                        {
                            continue;
                        }

                        // Seed-resolved candidates can include stations that have no observation endpoint
                        // and would 404 on every sampling pass. Surface only stations that actually report,
                        // so non-reporting ones are never scheduled or sampled.
                        if (!await client.StationHasObservations(stationId, cancellationToken).ConfigureAwait(false))
                        {
                            LogSkippedNonReportingStation(logger, stationId);
                            skipped++;
                            continue;
                        }

                        discoveryService.AddOrUpdateSelectionItem(
                            discovery.BuildSelectionItem(stationId));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Discovery failed for seed point {Lat},{Lon}.",
                    seed.Latitude, seed.Longitude);
            }

            discoveryService.UpdateProgress((int)((i + 1) / (double)seedPoints.Count * 100));
        }

        logger.LogInformation(
            "Discovery completed. {Surfaced} station(s) surfaced, {Skipped} non-reporting station(s) skipped.",
            discovered.Count - skipped,
            skipped);
    }

    // Logged once per seed-resolved station that has no working observation endpoint, so the skip is
    // visible without the per-cycle sampling errors it would otherwise cause. Expected (not exceptional),
    // hence Information. Uses the source-generated logging pattern established by the RDBMS adapter.
    [LoggerMessage(1, LogLevel.Information,
        "Skipping discovered station {StationId}: no observation endpoint, it will not be scheduled.")]
    static partial void LogSkippedNonReportingStation(ILogger logger, string stationId);


    // Testability seam: StreamIdGenerator is read through this virtual member so unit tests can
    // substitute a controlled generator without running the full adapter lifecycle. Production
    // returns the framework-populated value.
    /// <summary>
    /// Gets the stream ID generator used to register the default stream ID pattern with the framework.
    /// </summary>
    protected virtual IDefaultStreamIdGenerator StreamIdGenerator =>
        CommonService.DefaultStreamIdGenerator;

    /// <summary>
    /// Generates the default stream ID for a selection item by delegating to the framework's stream-id
    /// generator, which applies the pattern registered in <see cref="RegisterAdapterAsync"/> and
    /// resolves the effective stream-id prefix (with ComponentId fallback) centrally.
    /// </summary>
    protected override string GetDefaultStreamId(DataSelectionItem item)
    {
        if (item == null)
        {
            return string.Empty;
        }

        return StreamIdGenerator.GetDefaultStreamId(item.StationId);
    }

    /// <summary>
    /// Provides help text for data source configuration.
    /// </summary>
    protected override string GetDataSourceHelpInfo()
    {
        return $@"{GetCommandlineHelpHeader(CommonConstants.DataSourceConfigurationName)}
        {DataSourceConfiguration.GetHelpText()}";
    }

    /// <summary>
    /// Provides help text for data selection configuration.
    /// </summary>
    protected override string GetDataSelectionHelpInfo()
    {
        return DataSelectionItem.GetHelpInfo(
            GetCommandlineHelpHeader(CommonConstants.DataSelectionConfigurationName));
    }

    /// <summary>
    /// Disposes adapter-owned resources.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // The IHttpClientFactory and its handler pool are owned by the host's service provider.
            // Intentionally do not dispose _parallelLimiter: ProcessStation releases permits in finally,
            // and a late release racing with disposal can throw ObjectDisposedException during shutdown.
            // The adapter never uses AvailableWaitHandle, so leaving SemaphoreSlim undisposed is safe here.
        }

        _disposed = true;
        base.Dispose(disposing);
    }
}
