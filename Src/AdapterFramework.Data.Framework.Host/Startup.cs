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
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.CancellationTokenService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.Common.Security;
using AdapterFramework.Data.Framework.ComponentIdProvider;
using AdapterFramework.Data.Framework.Compression;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.ConfigurationProvider.Converters;
using AdapterFramework.Data.Framework.DataProtectionProvider;
using AdapterFramework.Data.Framework.DataProtector;
using AdapterFramework.Data.Framework.Diagnostics;
using AdapterFramework.Data.Framework.EndpointManager;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Host.ComponentServices;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Host.Helpers;
using AdapterFramework.Data.Framework.Host.Interfaces;
using AdapterFramework.Data.Framework.Host.SystemMiddleware;
using AdapterFramework.Data.Framework.Logger;
using AdapterFramework.Data.Framework.MessageProcessor;
using AdapterFramework.Data.Framework.Registry;
using AdapterFramework.Data.Framework.Serialization;
using IConfigurationProvider = AdapterFramework.Data.Framework.Abstractions.Configuration.IConfigurationProvider;

namespace AdapterFramework.Data.Framework.Host;

public class Startup
{
    #region Private Constants

    private const int FailFastDelay = 500;
    private const string FailFastMessage = "System level reset is being performed.";

    #endregion

    #region Private Fields

    private JsonConfigurationProvider _configurationProvider;
    private IEnumerable<Type> _sinkImplementations;
    private IEnumerable<Type> _adapterImplementations;
    private IEnumerable<Type> _edgeServiceImplementations;

    #endregion

    #region Public Methods

    public void ConfigureServices(IServiceCollection services)
    {
        _configurationProvider = new JsonConfigurationProvider(SystemHost.ApplicationDataDirectory);

        if (_configurationProvider.TryGetConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.SystemLevelResetMarkerFile,
            typeof(object), out _, out _))
        {
            EdgeSystemHelper.PerformSystemReset(_configurationProvider);
        }

        string healthPrefix = null;

        if (_configurationProvider.TryGetConfiguration<GeneralConfiguration>(EdgeSystemConstants.SystemComponentId,
            EdgeSystemConstants.GeneralFacetName, out var generalConfiguration, out _))
        {
            healthPrefix = generalConfiguration.HealthPrefix;
        }

        var serviceName = _configurationProvider.GetCommonApplicationDataDirectoryName();
        var applicationManifest = new ApplicationManifest(SystemHost.Port, SystemHost.BaseAddress, healthPrefix, SystemHost.MachineName, serviceName, SystemHost.OmfVersion);

        services.Configure<KestrelServerOptions>(options =>
        {
            options.AllowSynchronousIO = true;
        });

        services.AddSingleton<IRequestMetricsTracker, RequestMetricsTracker>();
        services.AddRouting();
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyHeader();
                policy.AllowAnyMethod();
                policy.AllowAnyOrigin();
            });
        });

        var logManager = new LogManager(_configurationProvider);

        // Here are configuration provider, system logger, and log manager that will be registered.
        // These are created here rather than by DI because they're needed .
        var edgeSystemLogger = logManager.GetOrCreateLogger(EdgeSystemConstants.SystemLogSourceName);

        if (SystemHost.EnforceBetaTimeout)
        {
            EdgeSystemHelper.ExitWhenBetaTimeoutExpired(SystemHost.BetaTimeoutDate, edgeSystemLogger);
        }

        var loggerConfigurator = logManager.GetLoggerConfigurator(EdgeSystemConstants.SystemLogSourceName);
        var componentIdService = new ComponentIdService(_configurationProvider, applicationManifest);

        // Register services for objects already instantiated
        services.AddSingleton<IConfigurationProvider>(_configurationProvider);
        services.AddSingleton<IApplicationManifest>(applicationManifest);

        // System logger, classes should only have an ILogger in their constructor if they need the system logger.
        // Otherwise they should use ILogManager and get their logger from that.
        services.AddSingleton(edgeSystemLogger);
        services.AddSingleton(loggerConfigurator);
        services.AddSingleton<ILogManager>(logManager);
        services.AddSingleton<IComponentIdService>(componentIdService);

        // Register services for classes that do not require explicit instantiation
        services.AddSingleton<IRuntimeConfigurationRegistry, RuntimeConfigurationRegistry>();
        services.AddSingleton<IRuntimeAdministrationRegistry, RuntimeAdministrationRegistry>();
        services.AddSingleton<IRuntimeManagementRegistry, RuntimeManagementRegistry>();
        services.AddSingleton<IDataProtectionProvider, ProtectionProvider>();
        services.AddSingleton<IConfigurationProtector, ConfigurationProtector>();
        services.AddSingleton<IInternalDataProtector, EdgeDataProtector>();
        services.AddSingleton<ICompressor, GZipCompressor>();
        services.AddSingleton<ISerializer, OmfJsonSerializer>();
        services.AddSingleton<IOmfWriterFactory, OmfWriterFactory>();
        services.AddSingleton<OmfEndpointManager>();
        services.AddSingleton<IOmfHealthEndpointManager>(sp => sp.GetRequiredService<OmfEndpointManager>());
        services.AddSingleton<IOmfEndpointManager>(sp => sp.GetRequiredService<OmfEndpointManager>());
        services.AddSingleton<IHealthMessageProcessor, HealthMessageProcessor>();
        services.AddSingleton<IAllowRequestsManager, AllowRequestsManager>();
        services.AddSingleton<IEdgeDiagnosticsService, EdgeDiagnosticsService>();
        services.AddSingleton<ICancellationTokenService, CancellationTokenProvider>();
        services.AddSingleton<IEdgeComponentsRepository, EdgeComponentsRepository>();
        services.AddSingleton<IEdgeComponentsOperationService, EdgeComponentsOperationService>();
        services.AddSingleton<SecretsManager>();
        services.AddSingleton<IEdgeDataProtector>(x => x.GetRequiredService<SecretsManager>());
        services.AddSingleton<ISecretsManager>(x => x.GetRequiredService<SecretsManager>());

        // Add System configuration controller
        var mvcBuilder = services.AddMvc(option => option.EnableEndpointRouting = false);
        mvcBuilder.AddApplicationPart(typeof(SystemConfigurationController).Assembly);
        mvcBuilder.AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new FlagEnumConverter<StreamProperties>(() => StreamProperties.All));
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true));
            options.JsonSerializerOptions.Converters.Add(new StringToTimeSpanConverter());
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

        var componentRegistrationDependencies = new Dictionary<Type, object>()
        {
            { typeof(ILogger), edgeSystemLogger },
            { typeof(ILogManager), logManager },
            { typeof(IConfigurationProvider), _configurationProvider },
            { typeof(IComponentIdService), componentIdService },
        };

        // Register available adapters
        _adapterImplementations = Services.GetComponentImplementations<IEdgeAdapter>(_configurationProvider,
            EdgeSystemConstants.AdapterDllNameBase, edgeSystemLogger);

        Services.AddTransient<IEdgeAdapter>(services, _adapterImplementations);

        // Register failover service provider if available.
        var failoverServiceProviderImplementation = Services.GetComponentImplementations<IFailoverServiceProvider>(_configurationProvider,
            EdgeSystemConstants.FailoverServiceDllFullName, edgeSystemLogger);

        Services.AddSingleton<IFailoverServiceProvider>(services, failoverServiceProviderImplementation);

        // Register Storage Sinks - based upon system configuration
        _sinkImplementations = Services.GetSinkProviderImplementations(_configurationProvider, edgeSystemLogger, applicationManifest);
        Services.AddSingleton<ISinkProvider>(services, _sinkImplementations);

        // Register Edge Service implementations when available
        _edgeServiceImplementations = Services.GetComponentImplementations<IEdgeService>(_configurationProvider, EdgeSystemConstants.AdapterFrameworkDllNameBase, edgeSystemLogger);
        Services.AddSingleton<IEdgeService>(services, _edgeServiceImplementations);

        // Invoke AddComponent method to allow components to register custom dependencies
        Services.Add(services, _sinkImplementations, componentRegistrationDependencies);
        Services.Add(services, _adapterImplementations, componentRegistrationDependencies);
        Services.Add(services, failoverServiceProviderImplementation, componentRegistrationDependencies);
        Services.Add(services, _edgeServiceImplementations, componentRegistrationDependencies);

        // Register background activities
        services.AddHostedService<HostedComponentsService>();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app)
    {
        ThrowHelper.ThrowIfArgumentNull(app, nameof(app));

        app.UseAllowRequestsMiddleware();
        app.UseRequestMetricsMiddleware();
        app.UseContentSecurityPolicyMiddleware();

        app.UseCors();

        // Invoke UseComponent method to allow components to perform custom setup
        Services.Use(app, _sinkImplementations);
        Services.Use(app, _adapterImplementations);
        Services.Use(app, _edgeServiceImplementations);

        RegisterSystemAdministrationActions(app.ApplicationServices.GetRequiredService<IRuntimeAdministrationRegistry>());

        app.UseMvc();
    }

    #endregion

    #region Private Methods

    private static async Task FailFastAsync()
    {
        await Task.Delay(FailFastDelay);

        Environment.FailFast(FailFastMessage);
    }

    private void RegisterSystemAdministrationActions(IRuntimeAdministrationRegistry runtimeAdministrationRegistry)
    {
        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ResetFunctionName, PerformSystemLevelResetAsync);
    }

    private Task PerformSystemLevelResetAsync()
    {
        _configurationProvider.TrySaveConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.SystemLevelResetMarkerFile,
            new object(), out _);

        _ = FailFastAsync();

        return Task.CompletedTask;
    }

    #endregion
}
