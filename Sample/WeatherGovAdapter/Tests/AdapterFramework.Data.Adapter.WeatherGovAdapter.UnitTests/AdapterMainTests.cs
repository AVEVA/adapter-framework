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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Interfaces;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests;

/// <summary>
/// Unit tests for <see cref="AdapterMain"/>. They exercise the real
/// <c>GetDefaultStreamId</c> branching logic through a test subclass that substitutes the
/// framework-populated stream-id generator via the adapter's protected virtual testability
/// seam, so no full adapter lifecycle is required.
/// </summary>
public class AdapterMainTests
{
    private const string TestStationId = "KSEA";

    /// <summary>
    /// Verifies that a null selection item resolves to <see cref="string.Empty"/> without calling the
    /// framework's stream-id generator (there is no station id to substitute into the pattern).
    /// </summary>
    [Fact]
    public void GetDefaultStreamId_NullItem_ReturnsEmpty()
    {
        var generator = new Mock<IDefaultStreamIdGenerator>(MockBehavior.Strict);
        var adapter = CreateAdapter(generator.Object);

        var streamId = adapter.InvokeGetDefaultStreamId(null);

        Assert.Equal(string.Empty, streamId);
        generator.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies that GetDefaultStreamId delegates stream-id composition to the framework's
    /// <see cref="IDefaultStreamIdGenerator"/>, passing the item's station id so the framework can
    /// substitute it into the pattern registered in <c>RegisterAdapterAsync</c> and apply the
    /// configured StreamIdPrefix (with ComponentId fallback) centrally.
    /// </summary>
    [Fact]
    public void GetDefaultStreamId_DelegatesToStreamIdGenerator()
    {
        const string generated = "framework.generated.stream.id";
        var generator = new Mock<IDefaultStreamIdGenerator>();
        generator
            .Setup(g => g.GetDefaultStreamId(TestStationId))
            .Returns(generated);
        var adapter = CreateAdapter(generator.Object);

        var streamId = adapter.InvokeGetDefaultStreamId(new DataSelectionItem { StationId = TestStationId });

        Assert.Equal(generated, streamId);
        generator.Verify(g => g.GetDefaultStreamId(TestStationId), Times.Once);
    }

    private static TestableAdapterMain CreateAdapter(
        IDefaultStreamIdGenerator streamIdGenerator = null,
        IHttpClientFactory httpClientFactory = null,
        ILogManager logManager = null,
        IMetadataService metadataService = null)
    {
        return new TestableAdapterMain(
            logManager ?? new Mock<ILogManager>().Object,
            new Mock<IConfigurationProvider>().Object,
            new Mock<IMessageProcessor>().Object,
            new Mock<IApplicationManifest>().Object,
            new Mock<IRuntimeConfigurationRegistry>().Object,
            new Mock<IEdgeDataProtector>().Object,
            new Mock<IComponentIdService>().Object,
            new Mock<IHealthMessageProcessor>().Object,
            new Mock<IDiagnosticsMessageProcessor>().Object,
            new Mock<IRuntimeAdministrationRegistry>().Object,
            httpClientFactory ?? new Mock<IHttpClientFactory>().Object,
            streamIdGenerator ?? new Mock<IDefaultStreamIdGenerator>().Object,
            metadataService);
    }

    /// <summary>
    /// Test-only subclass that exposes the protected <c>GetDefaultStreamId</c> method and
    /// overrides the adapter's testability seams so the component id and the stream-id
    /// generator can be controlled without running the adapter lifecycle. Future
    /// AdapterMain tests (lifecycle, discovery) can extend this same harness.
    /// </summary>
    private sealed class TestableAdapterMain : AdapterMain
    {
        private readonly IDefaultStreamIdGenerator _streamIdGenerator;

        public TestableAdapterMain(
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
            IDefaultStreamIdGenerator streamIdGenerator,
            IMetadataService metadataService)
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
                runtimeAdministrationRegistry,
                httpClientFactory,
                metadataService)
        {
            _streamIdGenerator = streamIdGenerator;
        }

        protected override IDefaultStreamIdGenerator StreamIdGenerator => _streamIdGenerator;

        public string InvokeGetDefaultStreamId(DataSelectionItem item) => GetDefaultStreamId(item);

        public DataSourceConfiguration GetActiveDataSource() => ActiveDataSource;

        public Task InvokeProcessDataSourceUpdate(DataSourceConfiguration oldValue, DataSourceConfiguration newValue) =>
            ProcessDataSourceUpdateAsync(oldValue, newValue);

        public Task InvokeSampleDataAsync(
            string scheduleId,
            IReadOnlyList<DataSelectionItem> items,
            CancellationToken cancellationToken) =>
            SampleDataAsync(scheduleId, items, cancellationToken);

        public Task InvokeDiscoverDataSourceAsync(
            string discoveryQuery,
            DataSourceConfiguration dataSourceConfiguration,
            IDataSourceDiscoveryService<DataSelectionItem> discoveryService,
            CancellationToken cancellationToken) =>
            DiscoverDataSourceAsync(discoveryQuery, dataSourceConfiguration, discoveryService, cancellationToken);

        public Task InvokeStartAdapterAsync(DataSourceConfiguration config, CancellationToken cancellationToken = default) =>
            StartAdapterAsync(config, cancellationToken);

        // Schedules the GetConfiguredSchedules seam returns. Defaults to one existing schedule so
        // StartAdapterAsync's seeding step short-circuits (ResolveSchedulesToSeed returns null) and
        // never reaches the framework CommonService, keeping the lifecycle test self-contained.
        public ScheduleConfiguration[] ConfiguredSchedules { get; set; } =
            [new ScheduleConfiguration { Id = "1", Period = TimeSpan.FromMinutes(1), Offset = TimeSpan.Zero }];

        protected override ScheduleConfiguration[] GetConfiguredSchedules() => ConfiguredSchedules;

        // Captures the schedules the adapter writes to the framework, so the seeding path can be
        // asserted without a live CommonService. Null until ApplySchedules is called.
        public ScheduleConfiguration[] SeededSchedules { get; private set; }

        protected override void ApplySchedules(ScheduleConfiguration[] schedules) => SeededSchedules = schedules;

        public Task InvokeRegisterAdapterAsync(CancellationToken cancellationToken = default) =>
            RegisterAdapterAsync(cancellationToken);

        public Task InvokeStopAdapterAsync(DataSourceConfiguration config, CancellationToken cancellationToken = default) =>
            StopAdapterAsync(config, cancellationToken);

        public Task InvokeProcessGeneralConfigurationUpdate(
            IAdapterGeneralConfiguration oldValue,
            IAdapterGeneralConfiguration newValue) =>
            ProcessGeneralConfigurationUpdateAsync(oldValue, newValue);
    }

    // Records the framework writes the adapter makes through IMetadataService, so metadata interactions
    // can be asserted without a live CommonService.
    private sealed class RecordingMetadataService : IMetadataService
    {
        public int EnsureTypeRegisteredCount { get; private set; }

        public List<(DataSelectionItem Item, WeatherObservationMeasurement Measurement)> Writes { get; } = [];

        public void EnsureTypeRegistered() => EnsureTypeRegisteredCount++;

        public void Write(DataSelectionItem item, WeatherObservationMeasurement measurement, ILogger logger = null) =>
            Writes.Add((item, measurement));
    }

    /// <summary>
    /// Verifies that each selectable schedule ID maps to its documented collection cadence.
    /// </summary>
    [Fact]
    public void SchedulePeriods_MapEachScheduleIdToDocumentedCadence()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), AdapterConstants.SchedulePeriods[AdapterConstants.DefaultScheduleId]);
        Assert.Equal(TimeSpan.FromMinutes(5), AdapterConstants.SchedulePeriods[AdapterConstants.ScheduleIdFiveMinutes]);
        Assert.Equal(TimeSpan.FromMinutes(10), AdapterConstants.SchedulePeriods[AdapterConstants.ScheduleIdTenMinutes]);
    }

    /// <summary>
    /// Verifies that the selectable schedule IDs are exactly the keys of the schedule-period map.
    /// </summary>
    [Fact]
    public void SelectableScheduleIds_MatchSchedulePeriodKeys()
    {
        Assert.Equal(
            AdapterConstants.SchedulePeriods.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase),
            AdapterConstants.SelectableScheduleIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that BuildDefaultSchedules produces one valid schedule per selectable ID with the
    /// documented period and a zero offset.
    /// </summary>
    [Fact]
    public void BuildDefaultSchedules_ProducesValidScheduleForEachSelectableId()
    {
        var schedules = AdapterMain.BuildDefaultSchedules();

        Assert.Equal(AdapterConstants.SelectableScheduleIds.Count, schedules.Length);

        foreach (var schedule in schedules)
        {
            Assert.Contains(schedule.Id, AdapterConstants.SelectableScheduleIds);
            Assert.Equal(AdapterConstants.SchedulePeriods[schedule.Id], schedule.Period);
            Assert.Equal(TimeSpan.Zero, schedule.Offset);
            Assert.Empty(schedule.Validate());
        }
    }

    /// <summary>
    /// Verifies that the default schedules are seeded when none are already configured.
    /// </summary>
    [Fact]
    public void ResolveSchedulesToSeed_NoSchedulesConfigured_ReturnsDefaults()
    {
        Assert.Equal(AdapterMain.BuildDefaultSchedules(), AdapterMain.ResolveSchedulesToSeed(null));
        Assert.Equal(AdapterMain.BuildDefaultSchedules(), AdapterMain.ResolveSchedulesToSeed(Array.Empty<ScheduleConfiguration>()));
    }

    /// <summary>
    /// Verifies that no schedules are seeded when the user has already configured some.
    /// </summary>
    [Fact]
    public void ResolveSchedulesToSeed_SchedulesConfigured_ReturnsNull()
    {
        var existing = new[]
        {
            new ScheduleConfiguration { Id = "1", Period = TimeSpan.FromSeconds(30), Offset = TimeSpan.Zero },
        };

        Assert.Null(AdapterMain.ResolveSchedulesToSeed(existing));
    }

    /// <summary>
    /// Verifies that AddComponent rejects a null service collection.
    /// </summary>
    [Fact]
    public void AddComponent_NullServices_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => AdapterMain.AddComponent(null, new Dictionary<Type, object>()));

        Assert.Equal("services", exception.ParamName);
    }

    /// <summary>
    /// Verifies that the public framework constructor (the one the DI container selects) builds the
    /// adapter, delegating to the injectable constructor with the real metadata service.
    /// </summary>
    [Fact]
    public void PublicConstructor_BuildsAdapter()
    {
        using var adapter = new AdapterMain(
            new Mock<ILogManager>().Object,
            new Mock<IConfigurationProvider>().Object,
            new Mock<IMessageProcessor>().Object,
            new Mock<IApplicationManifest>().Object,
            new Mock<IRuntimeConfigurationRegistry>().Object,
            new Mock<IEdgeDataProtector>().Object,
            new Mock<IComponentIdService>().Object,
            new Mock<IHealthMessageProcessor>().Object,
            new Mock<IDiagnosticsMessageProcessor>().Object,
            new Mock<IRuntimeAdministrationRegistry>().Object,
            new Mock<IHttpClientFactory>().Object);

        Assert.Equal(AdapterConstants.ComponentType, adapter.ComponentType);
    }

    /// <summary>
    /// Verifies that the public framework constructor rejects a null HTTP client factory.
    /// </summary>
    [Fact]
    public void PublicConstructor_NullHttpClientFactory_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new AdapterMain(
            new Mock<ILogManager>().Object,
            new Mock<IConfigurationProvider>().Object,
            new Mock<IMessageProcessor>().Object,
            new Mock<IApplicationManifest>().Object,
            new Mock<IRuntimeConfigurationRegistry>().Object,
            new Mock<IEdgeDataProtector>().Object,
            new Mock<IComponentIdService>().Object,
            new Mock<IHealthMessageProcessor>().Object,
            new Mock<IDiagnosticsMessageProcessor>().Object,
            new Mock<IRuntimeAdministrationRegistry>().Object,
            httpClientFactory: null));

        Assert.Equal("httpClientFactory", exception.ParamName);
    }

    // ----- StartAdapterAsync: insecure base URL warning -----

    /// <summary>
    /// Verifies that starting with AllowInsecureBaseUrl enabled and a plaintext http BaseUrl logs a
    /// warning naming the insecure URL, giving an operator an auditable signal that TLS is off, while
    /// the pooled client is still built.
    /// </summary>
    [Fact]
    public async Task StartAdapterAsync_InsecureHttpBaseUrl_LogsWarning()
    {
        var (logger, logManager) = CreateLogger();
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object, logManager: logManager.Object);
        var config = CreateConfig();
        config.BaseUrl = "http://localhost:8080";
        config.AllowInsecureBaseUrl = true;

        await adapter.InvokeStartAdapterAsync(config);

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString().Contains("plaintext HTTP") &&
                    state.ToString().Contains("http://localhost:8080")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
        // The client is still built from the pooled factory even when the base URL is insecure.
        factory.Verify(f => f.CreateClient(AdapterConstants.ClientName), Times.Once);
    }

    /// <summary>
    /// Verifies that a secure https base URL, or a plaintext http URL without the insecure flag, starts
    /// without logging the plaintext-HTTP warning (the flag-off http case is separately rejected by
    /// DataSourceConfiguration validation before start).
    /// </summary>
    [Theory]
    [InlineData("https://api.weather.gov", true)]   // https is secure even with the flag on
    [InlineData("https://api.weather.gov", false)]  // https, flag off
    [InlineData("http://localhost:8080", false)]    // http but insecure flag off, so no warning is surfaced here
    public async Task StartAdapterAsync_SecureOrFlagOff_DoesNotWarn(string baseUrl, bool allowInsecureBaseUrl)
    {
        var (logger, logManager) = CreateLogger();
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object, logManager: logManager.Object);
        var config = CreateConfig();
        config.BaseUrl = baseUrl;
        config.AllowInsecureBaseUrl = allowInsecureBaseUrl;

        await adapter.InvokeStartAdapterAsync(config);

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString().Contains("plaintext HTTP")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that starting with no schedules configured seeds the adapter's default schedules into
    /// the framework (one per selectable ID), so a fresh component collects at the documented cadences.
    /// </summary>
    [Fact]
    public async Task StartAdapterAsync_NoSchedulesConfigured_SeedsDefaultSchedules()
    {
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        adapter.ConfiguredSchedules = Array.Empty<ScheduleConfiguration>();

        await adapter.InvokeStartAdapterAsync(CreateConfig());

        Assert.Equal(AdapterMain.BuildDefaultSchedules(), adapter.SeededSchedules);
    }

    /// <summary>
    /// Verifies that starting when schedules already exist writes nothing, so user-configured schedules
    /// are respected.
    /// </summary>
    [Fact]
    public async Task StartAdapterAsync_SchedulesAlreadyConfigured_SeedsNothing()
    {
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        adapter.ConfiguredSchedules =
            [new ScheduleConfiguration { Id = "1", Period = TimeSpan.FromSeconds(30), Offset = TimeSpan.Zero }];

        await adapter.InvokeStartAdapterAsync(CreateConfig());

        Assert.Null(adapter.SeededSchedules);
    }

    // ----- Lifecycle: Register / Stop / GeneralConfig / Dispose -----

    /// <summary>
    /// Verifies that registering the adapter installs the default stream-id pattern and its keywords
    /// on the stream-id generator.
    /// </summary>
    [Fact]
    public async Task RegisterAdapterAsync_InstallsDefaultStreamIdPattern()
    {
        var generator = new Mock<IDefaultStreamIdGenerator>();
        var adapter = CreateAdapter(generator.Object);

        await adapter.InvokeRegisterAdapterAsync();

        generator.Verify(
            g => g.SetDefaultStreamIdPattern(
                AdapterConstants.DefaultStreamIdPattern,
                AdapterConstants.DefaultStreamIdKeywords),
            Times.Once);
    }

    /// <summary>
    /// Verifies that registering the adapter ensures the measurement type is registered with the
    /// framework via the metadata service.
    /// </summary>
    [Fact]
    public async Task RegisterAdapterAsync_RegistersMeasurementType()
    {
        var metadata = new RecordingMetadataService();
        var adapter = CreateAdapter(metadataService: metadata);

        await adapter.InvokeRegisterAdapterAsync();

        Assert.Equal(1, metadata.EnsureTypeRegisteredCount);
    }

    /// <summary>
    /// Verifies that a successful observation is written to the framework via the metadata service,
    /// carrying the sampled selection item and its mapped measurement.
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_SuccessfulObservation_WritesMeasurement()
    {
        var metadata = new RecordingMetadataService();
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object, metadataService: metadata);
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig());
        var item = new DataSelectionItem { StationId = "KSEA", StreamId = "s" };

        await adapter.InvokeSampleDataAsync("1", new[] { item }, CancellationToken.None);

        var write = Assert.Single(metadata.Writes);
        Assert.Same(item, write.Item);
        Assert.Equal("Clear", write.Measurement.text_description);
    }

    /// <summary>
    /// Verifies that stopping the adapter clears the active client, so a subsequent sampling pass skips
    /// and issues no request rather than reusing a stale client.
    /// </summary>
    [Fact]
    public async Task StopAdapterAsync_ClearsClient_SubsequentSampleIssuesNoRequest()
    {
        var factory = CreateFactory(out var handler);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig());

        await adapter.InvokeStopAdapterAsync(CreateConfig());
        await adapter.InvokeSampleDataAsync(
            "1",
            new[] { new DataSelectionItem { StationId = "KSEA" } },
            CancellationToken.None);

        Assert.Empty(handler.RequestUris);
    }

    /// <summary>
    /// Verifies that a general-configuration update is a no-op that completes without error.
    /// </summary>
    [Fact]
    public async Task ProcessGeneralConfigurationUpdateAsync_IsNoOp()
    {
        var adapter = CreateAdapter();

        var exception = await Record.ExceptionAsync(
            () => adapter.InvokeProcessGeneralConfigurationUpdate(null, null));

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that Dispose is idempotent: a second call after the first is a safe no-op.
    /// </summary>
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var adapter = CreateAdapter();

        adapter.Dispose();
        var exception = Record.Exception(() => adapter.Dispose());

        Assert.Null(exception);
    }

    // ----- ProcessDataSourceUpdate -----

    /// <summary>
    /// Verifies that presenting a value-equal configuration does not rebuild the HTTP client, so a
    /// benign facet refresh does not churn pooled connections.
    /// </summary>
    [Fact]
    public async Task ProcessDataSourceUpdate_UnchangedConfig_KeepsCurrentClient()
    {
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var config = CreateConfig();

        // Install the initial client; oldValue=null forces the rebuild branch.
        await adapter.InvokeProcessDataSourceUpdate(null, config);

        // Present a value-equal configuration; the equal-value branch must not rebuild the client.
        await adapter.InvokeProcessDataSourceUpdate(config, CreateConfig());

        factory.Verify(f => f.CreateClient(AdapterConstants.ClientName), Times.Once);
    }

    /// <summary>
    /// Verifies that a genuinely changed configuration rebuilds the client and swaps the active
    /// data source so a subsequent sampling pass observes the new configuration.
    /// </summary>
    [Fact]
    public async Task ProcessDataSourceUpdate_ChangedConfig_RebuildsClientAndSwapsActive()
    {
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig(userAgent: "different-agent/1.0 (a@b)");

        await adapter.InvokeProcessDataSourceUpdate(null, oldConfig);
        await adapter.InvokeProcessDataSourceUpdate(oldConfig, newConfig);

        factory.Verify(f => f.CreateClient(AdapterConstants.ClientName), Times.Exactly(2));
        Assert.Same(newConfig, adapter.GetActiveDataSource());
    }

    /// <summary>
    /// Verifies that changing only AllowInsecureBaseUrl is treated as a real data-source change,
    /// so the client is rebuilt and the new config is active for subsequent requests.
    /// </summary>
    [Fact]
    public async Task ProcessDataSourceUpdate_AllowInsecureBaseUrlChanged_RebuildsClient()
    {
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var oldConfig = CreateConfig();
        var newConfig = CreateConfig();
        newConfig.AllowInsecureBaseUrl = true;

        await adapter.InvokeProcessDataSourceUpdate(null, oldConfig);
        await adapter.InvokeProcessDataSourceUpdate(oldConfig, newConfig);

        factory.Verify(f => f.CreateClient(AdapterConstants.ClientName), Times.Exactly(2));
        Assert.Same(newConfig, adapter.GetActiveDataSource());
    }

    // ----- SampleDataAsync: early returns -----

    /// <summary>
    /// Verifies that a null or empty item list short-circuits before any observation request is issued.
    /// </summary>
    [Theory]
    [InlineData(true)]   // null item list
    [InlineData(false)]  // empty item list
    public async Task SampleDataAsync_NullOrEmptyItems_ReturnsWithoutError(bool nullItems)
    {
        var factory = CreateFactory(out var handler);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig());

        var items = nullItems ? null : Array.Empty<DataSelectionItem>();
        await adapter.InvokeSampleDataAsync("1", items, CancellationToken.None);

        Assert.Empty(handler.RequestUris);
    }

    /// <summary>
    /// Verifies that a sample pass which fires before the adapter has processed its first data-source
    /// configuration skips silently rather than dereferencing a null client.
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_ClientNotReady_SkipsWithoutError()
    {
        var factory = new Mock<IHttpClientFactory>();
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        // No ProcessDataSourceUpdate call, so _client stays null and the sample pass must skip.

        await adapter.InvokeSampleDataAsync(
            "1",
            new[] { new DataSelectionItem { StationId = "KSEA" } },
            CancellationToken.None);

        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    // ----- SampleDataAsync: fan-out and ProcessStation error handling -----

    /// <summary>
    /// Verifies that each selection item drives exactly one observation request, so the framework's
    /// selection-to-schedule routing translates one-for-one into station requests.
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_FansOutOneRequestPerItem()
    {
        var factory = CreateFactory(out var handler);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig());

        var items = new[]
        {
            new DataSelectionItem { StationId = "KSEA" },
            new DataSelectionItem { StationId = "KJFK" },
        };

        await adapter.InvokeSampleDataAsync("1", items, CancellationToken.None);

        Assert.Equal(2, handler.RequestUris.Count);
    }

    /// <summary>
    /// Verifies that the configured <see cref="DataSourceConfiguration.MaxConcurrentRequests"/> caps how
    /// many station requests run at once: with more items than the cap, the number observed concurrently
    /// in flight reaches the cap and never exceeds it, proving the configured value (not a hardcoded
    /// default) sizes the limiter.
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_RespectsConfiguredConcurrencyCap()
    {
        const int cap = 2;
        var concurrency = new ConcurrencyCounter();
        var capReached = new TaskCompletionSource();
        using var release = new SemaphoreSlim(0);

        var factory = CreateFactory(out _, async _ =>
        {
            if (concurrency.Enter() >= cap)
            {
                capReached.TrySetResult();
            }

            // Hold each request in flight until the test releases it, so concurrency is observable.
            await release.WaitAsync();
            concurrency.Exit();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ObservationJson, Encoding.UTF8, "application/json"),
            };
        });
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var config = CreateConfig();
        config.MaxConcurrentRequests = cap;
        await adapter.InvokeProcessDataSourceUpdate(null, config);

        var items = Enumerable.Range(0, cap + 2)
            .Select(i => new DataSelectionItem { StationId = $"S{i}", StreamId = "s" })
            .ToArray();

        var sampling = adapter.InvokeSampleDataAsync("1", items, CancellationToken.None);

        // Wait until the cap is reached; fail fast rather than hang if a regression sizes it too small.
        var reached = await Task.WhenAny(capReached.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(capReached.Task, reached);

        // Release every held request so the sampling pass can finish.
        release.Release(items.Length);
        await sampling;

        // The limiter allowed exactly `cap` concurrent requests: it reached the cap but never exceeded it.
        Assert.Equal(cap, concurrency.Max);
    }

    // Tracks the current and peak number of concurrent callers, for the concurrency-cap test.
    private sealed class ConcurrencyCounter
    {
        private int _current;
        private int _max;

        public int Max => Volatile.Read(ref _max);

        public int Enter()
        {
            var current = Interlocked.Increment(ref _current);
            int observed;
            while (current > (observed = Volatile.Read(ref _max)))
            {
                if (Interlocked.CompareExchange(ref _max, current, observed) == observed)
                {
                    break;
                }
            }

            return current;
        }

        public void Exit() => Interlocked.Decrement(ref _current);
    }

    /// <summary>
    /// Verifies that a per-station failure is contained to its own station and does not fail the overall
    /// sampling pass, and that the failure is logged once at Error level naming the station so the
    /// operator keeps a signal (a regression to a silent return would be caught here).
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_PerStationFailure_IsSwallowedButLogged()
    {
        var logger = new Mock<ILogger>();
        var logManager = new Mock<ILogManager>();
        logManager
            .Setup(m => m.GetOrCreateLogger(It.IsAny<string>(), It.IsAny<LoggerConfiguration>()))
            .Returns(logger.Object);
        var factory = CreateFactory(
            out _,
            _ => throw new HttpRequestException("simulated transport failure"));
        var adapter = CreateAdapter(httpClientFactory: factory.Object, logManager: logManager.Object);
        // MaxRetries=0 so the failure surfaces on the first attempt rather than looping.
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig(maxRetries: 0));

        await adapter.InvokeSampleDataAsync(
            "1",
            new[] { new DataSelectionItem { StationId = "KSEA" } },
            CancellationToken.None);

        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString().Contains("KSEA")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a caller-initiated cancellation propagates out of the sampling pass rather than
    /// being swallowed by the per-station error catch.
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_CallerCancels_Throws()
    {
        var factory = CreateFactory(out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.InvokeSampleDataAsync(
                "1",
                new[] { new DataSelectionItem { StationId = "KSEA" } },
                cts.Token));
    }

    /// <summary>
    /// Verifies that a station whose observation payload has no properties is skipped with an
    /// "invalid payload" warning rather than throwing or writing a value.
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_NullObservationProperties_LogsInvalidPayload()
    {
        var (logger, logManager) = CreateLogger();
        var factory = CreateFactory(out _, StubHttpMessageHandler.RespondJson("{}")); // no "properties"
        var adapter = CreateAdapter(httpClientFactory: factory.Object, logManager: logManager.Object);
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig());

        await adapter.InvokeSampleDataAsync(
            "1",
            new[] { new DataSelectionItem { StationId = "KSEA", StreamId = "s" } },
            CancellationToken.None);

        VerifyLog(logger, LogLevel.Warning, "Invalid observation payload", Times.Once());
    }

    /// <summary>
    /// Verifies that an observation missing its timestamp maps to no measurement and is skipped (the
    /// mapper warns), so the station is not written.
    /// </summary>
    [Fact]
    public async Task SampleDataAsync_ObservationMissingTimestamp_SkipsStation()
    {
        var (logger, logManager) = CreateLogger();
        var factory = CreateFactory(out _, StubHttpMessageHandler.RespondJson("{\"properties\":{\"textDescription\":\"Clear\"}}"));
        var adapter = CreateAdapter(httpClientFactory: factory.Object, logManager: logManager.Object);
        await adapter.InvokeProcessDataSourceUpdate(null, CreateConfig());

        await adapter.InvokeSampleDataAsync(
            "1",
            new[] { new DataSelectionItem { StationId = "KSEA", StreamId = "s" } },
            CancellationToken.None);

        // MapObservation drops the timestamp-less observation and warns; ProcessStation then returns.
        VerifyLog(logger, LogLevel.Warning, "no timestamp", Times.Once());
    }

    /// <summary>
    /// Verifies that a timestamp-only measurement (no usable field values) is dropped with a debug log
    /// when DropNullMeasurements is enabled, and kept (no drop log) when it is disabled.
    /// </summary>
    [Theory]
    [InlineData(true)]   // drop enabled -> the empty measurement is dropped and logged
    [InlineData(false)]  // drop disabled -> the empty measurement is kept, no drop log
    public async Task SampleDataAsync_EmptyMeasurement_DropsWhenEnabled(bool dropNullMeasurements)
    {
        var (logger, logManager) = CreateLogger();
        var factory = CreateFactory(out _, StubHttpMessageHandler.RespondJson("{\"properties\":{\"timestamp\":\"2026-01-01T00:00:00Z\"}}"));
        var adapter = CreateAdapter(httpClientFactory: factory.Object, logManager: logManager.Object);
        var config = CreateConfig();
        config.DropNullMeasurements = dropNullMeasurements;
        await adapter.InvokeProcessDataSourceUpdate(null, config);

        await adapter.InvokeSampleDataAsync(
            "1",
            new[] { new DataSelectionItem { StationId = "KSEA", StreamId = "s" } },
            CancellationToken.None);

        VerifyLog(
            logger,
            LogLevel.Debug,
            "Dropping empty measurement",
            dropNullMeasurements ? Times.Once() : Times.Never());
    }

    /// <summary>
    /// Verifies that a discovery run with no seed points completes and reports full
    /// progress without building an HTTP client.
    /// </summary>
    [Fact]
    public async Task DiscoverDataSourceAsync_NoSeeds_CompletesWithoutBuildingClient()
    {
        var factory = new Mock<IHttpClientFactory>();
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var (service, surfaced, progress) = CreateDiscoveryService();

        await adapter.InvokeDiscoverDataSourceAsync(string.Empty, CreateConfig(), service.Object, CancellationToken.None);

        Assert.Empty(surfaced);
        Assert.Equal(100, Assert.Single(progress));
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    // ----- DiscoverDataSourceAsync: seed-point resolution (stubbed network) -----

    /// <summary>
    /// Verifies that a seed point resolves to its nearby stations and surfaces those that report
    /// observations, reporting full progress for the single seed.
    /// </summary>
    [Fact]
    public async Task DiscoverDataSourceAsync_ValidSeedPoint_SurfacesReportingStations()
    {
        var factory = CreateRoutingFactory(DiscoveryRoute(StationsJson("KSEA", "KJFK")), out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var (service, surfaced, progress) = CreateDiscoveryService();

        await adapter.InvokeDiscoverDataSourceAsync(SeattleSeed, CreateConfig(), service.Object, CancellationToken.None);

        Assert.Equal(new[] { "KSEA", "KJFK" }, surfaced.Select(item => item.StationId));
        Assert.Equal(100, Assert.Single(progress));
    }

    /// <summary>
    /// Verifies that a seed-resolved station whose observation endpoint returns Not Found is skipped, so
    /// stations that would fail every sampling pass are never scheduled.
    /// </summary>
    [Fact]
    public async Task DiscoverDataSourceAsync_NonReportingStation_IsSkipped()
    {
        var factory = CreateRoutingFactory(DiscoveryRoute(StationsJson("KSEA"), "KSEA"), out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var (service, surfaced, progress) = CreateDiscoveryService();

        await adapter.InvokeDiscoverDataSourceAsync(SeattleSeed, CreateConfig(), service.Object, CancellationToken.None);

        Assert.Empty(surfaced);
        Assert.Equal(100, Assert.Single(progress));
    }

    /// <summary>
    /// Verifies that a failure resolving one seed point does not abort the run: the remaining seed still
    /// surfaces its station, and progress is reported for every seed.
    /// </summary>
    [Fact]
    public async Task DiscoverDataSourceAsync_SeedFailure_IsContainedToOwnSeed()
    {
        var factory = CreateRoutingFactory(
            request =>
            {
                var path = request.RequestUri.AbsolutePath;
                if (path.StartsWith("/points/", StringComparison.Ordinal))
                {
                    // The first seed's point lookup fails; the second seed's must still be resolved.
                    if (request.RequestUri.ToString().Contains("47.6", StringComparison.Ordinal))
                    {
                        throw new HttpRequestException("simulated points failure");
                    }

                    return Json(PointJson(StationsCollectionUrl));
                }

                if (path.EndsWith("/observations/latest", StringComparison.Ordinal))
                {
                    return Status(HttpStatusCode.OK);
                }

                return Json(StationsJson("KJFK"));
            },
            out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var (service, surfaced, progress) = CreateDiscoveryService();

        // MaxRetries=0 so the failing point lookup surfaces on the first attempt rather than looping.
        await adapter.InvokeDiscoverDataSourceAsync(
            $"{SeattleSeed};40.7,-74.05", CreateConfig(maxRetries: 0), service.Object, CancellationToken.None);

        Assert.Equal("KJFK", Assert.Single(surfaced).StationId);
        Assert.Equal(new[] { 50, 100 }, progress);
    }

    /// <summary>
    /// Verifies that a caller-initiated cancellation propagates out of discovery rather than being
    /// swallowed by the per-seed error catch.
    /// </summary>
    [Fact]
    public async Task DiscoverDataSourceAsync_CallerCancels_Throws()
    {
        var factory = CreateRoutingFactory(DiscoveryRoute(StationsJson("KSEA")), out _);
        var adapter = CreateAdapter(httpClientFactory: factory.Object);
        var (service, _, _) = CreateDiscoveryService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.InvokeDiscoverDataSourceAsync(SeattleSeed, CreateConfig(), service.Object, cts.Token));
    }

    // ----- Helpers -----

    // A well-formed latest-observation payload; the sampling pass only reads Properties and Timestamp
    // here, so nothing beyond those two is populated.
    private const string ObservationJson =
        "{\"properties\":{\"timestamp\":\"2026-01-01T00:00:00Z\",\"textDescription\":\"Clear\"}}";

    // Builds a mock logger (with IsEnabled true so source-generated logging fires) wired through a mock
    // ILogManager, matching how the adapter resolves its logger during the lifecycle.
    private static (Mock<ILogger> Logger, Mock<ILogManager> LogManager) CreateLogger() =>
        MockLoggerHelpers.CreateLoggerWithManager();

    // Verifies a log entry at the given level whose formatted message contains the given text was
    // written the expected number of times.
    private static void VerifyLog(Mock<ILogger> logger, LogLevel level, string contains, Times times) =>
        MockLoggerHelpers.VerifyLog(logger, level, contains, times);

    private static DataSourceConfiguration CreateConfig(
        int maxRetries = 2,
        int retryBackoffMs = 0,
        int requestTimeoutMs = 30000,
        string userAgent = "WeatherGovAdapterTests/1.0 (test@example.com)") =>
        WeatherGovTestData.CreateDataSourceConfiguration(
            maxRetries, retryBackoffMs, requestTimeoutMs, userAgent);

    // Builds an IHttpClientFactory whose CreateClient always returns an HttpClient backed by a shared
    // stub handler, so a test can drive canned responses (or a thrower) and observe request fan-out.
    private static Mock<IHttpClientFactory> CreateFactory(
        out StubHttpMessageHandler handler,
        Func<CancellationToken, Task<HttpResponseMessage>> responder = null)
    {
        handler = new StubHttpMessageHandler(responder ?? StubHttpMessageHandler.RespondJson(ObservationJson));
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);
        return factory;
    }

    // ----- Discovery helpers -----

    // A single well-formed seed point (Seattle). Its longitude is chosen so that the invariant
    // "0.####" formatting the client applies leaves a value the routing helpers can match on.
    private const string SeattleSeed = "47.6,-122.3";

    // The observation-stations collection URL a point lookup resolves to. Its path is distinct from the
    // point and per-station probe paths so the routing helper can tell the three endpoints apart.
    private const string StationsCollectionUrl = "https://api.weather.gov/gridpoints/SEW/116,68/stations";

    // Creates a discovery service that records surfaced items and progress updates for assertions.
    private static (Mock<IDataSourceDiscoveryService<DataSelectionItem>> Service, List<DataSelectionItem> Surfaced, List<int> Progress) CreateDiscoveryService()
    {
        var surfaced = new List<DataSelectionItem>();
        var progress = new List<int>();
        var service = new Mock<IDataSourceDiscoveryService<DataSelectionItem>>();
        service
            .Setup(s => s.AddOrUpdateSelectionItem(It.IsAny<DataSelectionItem>()))
            .Callback<DataSelectionItem>(surfaced.Add);
        service
            .Setup(s => s.UpdateProgress(It.IsAny<int>()))
            .Callback<int>(progress.Add);
        return (service, surfaced, progress);
    }

    // Builds an IHttpClientFactory whose client routes each request through the supplied responder, and
    // exposes the handler so a test can assert which endpoints were (or were not) called.
    private static Mock<IHttpClientFactory> CreateRoutingFactory(
        Func<HttpRequestMessage, HttpResponseMessage> route,
        out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(route);
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return factory;
    }

    // The standard discovery response flow: a point lookup resolves to the stations collection, the
    // collection returns the given stations JSON, and each station's observation probe returns OK unless
    // its id is listed as non-reporting (in which case the probe returns Not Found).
    private static Func<HttpRequestMessage, HttpResponseMessage> DiscoveryRoute(
        string stationsJson,
        params string[] nonReportingStationIds)
    {
        var nonReporting = new HashSet<string>(nonReportingStationIds, StringComparer.OrdinalIgnoreCase);
        return request =>
        {
            var path = request.RequestUri.AbsolutePath;
            if (path.StartsWith("/points/", StringComparison.Ordinal))
            {
                return Json(PointJson(StationsCollectionUrl));
            }

            if (path.EndsWith("/observations/latest", StringComparison.Ordinal))
            {
                // Path shape is /stations/{id}/observations/latest.
                var stationId = path.Split('/')[2];
                return Status(nonReporting.Contains(stationId) ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            }

            return Json(stationsJson);
        };
    }

    private static string PointJson(string observationStationsUrl) =>
        $"{{\"properties\":{{\"observationStations\":\"{observationStationsUrl}\"}}}}";

    private static string StationsJson(params string[] stationIds)
    {
        var features = string.Join(
            ",",
            stationIds.Select(id => $"{{\"properties\":{{\"stationIdentifier\":\"{id}\"}}}}"));
        return $"{{\"features\":[{features}]}}";
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Status(HttpStatusCode statusCode) => new(statusCode);
}
