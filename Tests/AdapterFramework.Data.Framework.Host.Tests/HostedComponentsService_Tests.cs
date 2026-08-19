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
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.Host.Interfaces;
using AdapterFramework.Data.Framework.Host.SystemMiddleware;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests;

public class HostedComponentsService_Tests
{
    private const long DefaultLogFileSizeLimitBytes = 1073741824 / 31;
    private const int DefaultLogFileCountLimit = 31;
    private const LogLevel DefaultLogLevel = LogLevel.Information;
    private const string TestAdapterType = "TestAdapter";
    private const string TestAdapterId = "TestAdapterId";

    private readonly Mock<IComponentIdService> _mockComponentIdService = new();
    private readonly Mock<ILoggerConfigurator> _mockLoggerConfigurator = new();
    private readonly Mock<IOmfHealthEndpointManager> _mockHealthEndpointManager = new();
    private readonly Mock<IOmfDataEndpointManager> _mockOmfDataEndpointManager = new();
    private readonly Mock<IRuntimeConfigurationRegistry> _mockRuntimeConfigurationRegistry = new();
    private readonly Mock<IRuntimeManagementRegistry> _mockRuntimeManagementRegistry = new();
    private readonly Mock<IConfigurationProvider> _mockConfigurationProvider = new();
    private readonly Mock<IRequestMetricsTracker> _mockRequestMetricsTracker = new();
    private readonly Mock<IAllowRequestsManager> _mockAllowRequestManager = new();
    private readonly Mock<IEdgeComponentsRepository> _mockComponentsRepository = new();
    private readonly Mock<IEdgeComponentsOperationService> _mockComponentsOperationService = new();
    private readonly Mock<IEdgeDiagnosticsService> _mockEdgeDiagnosticsService = new();
    private readonly Mock<ISecretsManager> _mockSecretsManager = new();
    private readonly Mock<IApplicationManifest> _mockApplicationManifest = new();
    private readonly Mock<IFailoverServiceProvider> _mockFailoverServiceProvider = new();
    private readonly Mock<IDiagnosticsMessageProcessor> _mockDiagnosticsMessageProcessor = new();
    private readonly Mock<ISinkProvider> _mockSinkProvider = new();

    private delegate void TryGetComponentConfigurationCallback(string id, string facet, out EdgeComponentConfig[] configs, out ICollection<string> errors);
    private delegate void TryGetGeneralConfigurationCallback(string id, string facet, out GeneralConfiguration config, out ICollection<string> errors);
    private delegate void TrySaveGeneralConfigurationCallback(string id, string facet, GeneralConfiguration config, out ICollection<string> errors);

    [Fact]
    public async Task HostedComponentsService_StartAsync_Test()
    {
        var componentId = "Test";
        var facetName = "TestFacet";
        var secretId = "{{Placeholder}}";
        var scrubbedSecretId = "Placeholder";
        var arrayComponentId = "Test1";
        var arrayFacetName = "TestFacet2";
        var arrayConfigurationId = "Index";

        var storageStarted = false;
        var adapterStarted = false;
        var diagnosticsServiceStarted = false;
        var testLogger = new TestLogger();

        var platformConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = TestAdapterType,
                ComponentId = TestAdapterId,
            },
        };

        var tryGetComponentCalled = false;
        EdgeComponentConfig[] outComponentConfigs = null;
        ICollection<string> getComponentErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out outComponentConfigs, out getComponentErrors))
        .Callback(new TryGetComponentConfigurationCallback((string id, string facet, out EdgeComponentConfig[] configs, out ICollection<string> errors) =>
        {
            tryGetComponentCalled = true;
            configs = platformConfiguration;
            errors = null;
        })).Returns(true);

        var tryGetGeneralCalled = false;
        GeneralConfiguration outGeneralConfig = null;
        ICollection<string> getGeneralErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out outGeneralConfig, out getGeneralErrors))
        .Callback(new TryGetGeneralConfigurationCallback((string id, string facet, out GeneralConfiguration config, out ICollection<string> errors) =>
        {
            tryGetGeneralCalled = true;
            config = new GeneralConfiguration();
            errors = null;
        })).Returns(true);

        var trySaveGeneralCalled = false;
        ICollection<string> saveGeneralErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeneralConfiguration>(), out saveGeneralErrors))
        .Callback(new TrySaveGeneralConfigurationCallback((string id, string facet, GeneralConfiguration configs, out ICollection<string> errors) =>
        {
            trySaveGeneralCalled = true;
            errors = null;
        })).Returns(true);

        var protectedProperties = ConfigurationHelper.GetProtectedPropertyInfos(typeof(SampleConfiguration)).ToList();
        var propertyInfos = new Dictionary<(string ComponentId, string Facet), IList<PropertyInfo>>
        {
            { (componentId, facetName), protectedProperties },
            { (arrayComponentId, arrayFacetName), protectedProperties },
        };

        var sampleConfiguration = (object)new SampleConfiguration
        {
            Password = secretId,
        };

        var sampleArrayConfiguration = (object)new SampleConfiguration[]
        {
            new()
            {
                Id = arrayConfigurationId,
                Password = secretId,
            },
            new()
            {
                Id = "Hello",
                Password = string.Empty,
            },
        };

        ICollection<string> configurationErrors = new List<string>();
        var mockSimpleGetCommand = new Mock<IConfigurationGetCommand>();
        var mockArrayGetCommand = new Mock<IConfigurationGetCommand>();

        mockSimpleGetCommand.Setup(getCommand => getCommand.TryExecute(out sampleConfiguration, out configurationErrors)).Returns(true);
        mockArrayGetCommand.Setup(getCommand => getCommand.TryExecute(out sampleArrayConfiguration, out configurationErrors)).Returns(true);

        var mockCommandGenerator = new Mock<IConfigurationCommandGenerator>();
        mockCommandGenerator.Setup(commandGenerator => commandGenerator.GenerateConfigurationGetCommand(typeof(SampleConfiguration), It.IsAny<string>()))
            .Returns(mockSimpleGetCommand.Object);

        mockCommandGenerator.Setup(commandGenerator => commandGenerator.GenerateConfigurationGetCommand(typeof(SampleConfiguration[]), It.IsAny<string>()))
            .Returns(mockArrayGetCommand.Object);

        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) simpleConfigurationTuple = default;

        simpleConfigurationTuple.CommandGenerator = mockCommandGenerator.Object;
        simpleConfigurationTuple.ConfigurationType = typeof(SampleConfiguration);

        (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunction,
            Type ConfigurationType, Operations SupportedOperations) arrayConfigurationTuple = default;

        arrayConfigurationTuple.CommandGenerator = mockCommandGenerator.Object;
        arrayConfigurationTuple.ConfigurationType = typeof(SampleConfiguration[]);

        _mockRuntimeConfigurationRegistry.Setup(configurationRegistry => configurationRegistry.GetProtectedPropertyInfos()).Returns(propertyInfos);

        var simpleConfigurationTupleKeys = (componentId, facetName);
        var arrayConfigurationTupleKeys = (arrayComponentId, arrayFacetName);

        _mockRuntimeConfigurationRegistry.Setup(configurationRegistry => configurationRegistry.TryGetCommandGeneratorTuple(simpleConfigurationTupleKeys, out simpleConfigurationTuple)).Returns(true);
        _mockRuntimeConfigurationRegistry.Setup(configurationRegistry => configurationRegistry.TryGetCommandGeneratorTuple(arrayConfigurationTupleKeys, out arrayConfigurationTuple)).Returns(true);

        _mockEdgeDiagnosticsService
            .Setup(diagnosticsService => diagnosticsService.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => diagnosticsServiceStarted = true)
            .Returns(Task.CompletedTask);

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => adapterStarted = true)
            .Returns(Task.CompletedTask);

        mockAdapter.Setup(adapter => adapter.ComponentType).Returns(TestAdapterType);
        mockAdapter.Setup(adapter => adapter.ComponentId).Returns(TestAdapterId);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(mockAdapter.Object);

        _mockSinkProvider.Setup(sinkProvider => sinkProvider.StartAsync())
                        .Callback(() => storageStarted = true)
                        .Returns(Task.CompletedTask);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection);

        await hostedComponentsService.StartAsync(CancellationToken.None);

        Assert.True(tryGetComponentCalled);
        Assert.True(tryGetGeneralCalled);
        Assert.False(trySaveGeneralCalled);
        Assert.True(storageStarted);
        Assert.True(adapterStarted);
        Assert.True(diagnosticsServiceStarted);
        Assert.False(testLogger.AreErrorsWarningsInLog());

        _mockRuntimeManagementRegistry.Verify(managementRegistry => managementRegistry.AddOrUpdateSecretIdFacetsMapping(scrubbedSecretId, componentId, facetName, null), Times.Once);
        _mockRuntimeManagementRegistry.Verify(managementRegistry => managementRegistry.AddOrUpdateSecretIdFacetsMapping(scrubbedSecretId, arrayComponentId, arrayFacetName, arrayConfigurationId), Times.Once);
    }

    [Fact]
    public async Task HostedComponentsService_StartAsync_MissingGeneralConfig_Test()
    {
        var storageStarted = false;
        var adapterStarted = false;
        var diagnosticsServiceStarted = false;
        var testLogger = new TestLogger();

        var platformConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = TestAdapterType,
                ComponentId = TestAdapterId,
            },
        };

        var tryGetComponentCalled = false;
        EdgeComponentConfig[] outComponentConfigs = null;
        ICollection<string> getComponentErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out outComponentConfigs, out getComponentErrors))
        .Callback(new TryGetComponentConfigurationCallback((string id, string facet, out EdgeComponentConfig[] configs, out ICollection<string> errors) =>
        {
            tryGetComponentCalled = true;
            configs = platformConfiguration;
            errors = null;
        })).Returns(true);

        var tryGetGeneralCalled = false;
        GeneralConfiguration outGeneralConfig = null;
        ICollection<string> getGeneralErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out outGeneralConfig, out getGeneralErrors))
        .Callback(new TryGetGeneralConfigurationCallback((string id, string facet, out GeneralConfiguration config, out ICollection<string> errors) =>
        {
            tryGetGeneralCalled = true;
            config = null;
            errors = null;
        })).Returns(false);

        var trySaveGeneralCalled = false;
        ICollection<string> saveGeneralErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeneralConfiguration>(), out saveGeneralErrors))
        .Callback(new TrySaveGeneralConfigurationCallback((string id, string facet, GeneralConfiguration configs, out ICollection<string> errors) =>
        {
            trySaveGeneralCalled = true;
            errors = null;
        })).Returns(true);

        _mockEdgeDiagnosticsService
            .Setup(diagnosticsService => diagnosticsService.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => diagnosticsServiceStarted = true)
            .Returns(Task.CompletedTask);

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => adapterStarted = true)
            .Returns(Task.CompletedTask);

        mockAdapter.Setup(adapter => adapter.ComponentType).Returns(TestAdapterType);
        mockAdapter.Setup(adapter => adapter.ComponentId).Returns(TestAdapterId);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(mockAdapter.Object);

        _mockSinkProvider.Setup(sinkProvider => sinkProvider.StartAsync())
                        .Callback(() => storageStarted = true)
                        .Returns(Task.CompletedTask);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection);

        await hostedComponentsService.StartAsync(CancellationToken.None);

        Assert.True(tryGetComponentCalled);
        Assert.True(tryGetGeneralCalled);
        Assert.True(trySaveGeneralCalled);
        Assert.True(storageStarted);
        Assert.True(adapterStarted);
        Assert.True(diagnosticsServiceStarted);
        Assert.False(testLogger.AreErrorsWarningsInLog());
    }

    [Fact]
    public async Task HostedComponentsService_StartAsync_InvalidGeneralConfig_Test()
    {
        var storageStarted = false;
        var adapterStarted = false;
        var diagnosticsServiceStarted = false;
        var testLogger = new TestLogger();

        var platformConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = TestAdapterType,
                ComponentId = TestAdapterId,
            },
        };

        var tryGetComponentCalled = false;
        EdgeComponentConfig[] outComponentConfigs = null;
        ICollection<string> getComponentErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out outComponentConfigs, out getComponentErrors))
        .Callback(new TryGetComponentConfigurationCallback((string id, string facet, out EdgeComponentConfig[] configs, out ICollection<string> errors) =>
        {
            tryGetComponentCalled = true;
            configs = platformConfiguration;
            errors = null;
        })).Returns(true);

        var tryGetGeneralCalled = false;
        GeneralConfiguration outGeneralConfig = null;
        ICollection<string> getGeneralErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out outGeneralConfig, out getGeneralErrors))
        .Callback(new TryGetGeneralConfigurationCallback((string id, string facet, out GeneralConfiguration config, out ICollection<string> errors) =>
        {
            tryGetGeneralCalled = true;
            config = null;
            errors = new List<string>() { "Some errors" };
        })).Returns(false);

        var trySaveGeneralCalled = false;
        ICollection<string> saveGeneralErrors = null;
        _mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GeneralConfiguration>(), out saveGeneralErrors))
        .Callback(new TrySaveGeneralConfigurationCallback((string id, string facet, GeneralConfiguration configs, out ICollection<string> errors) =>
        {
            trySaveGeneralCalled = true;
            errors = null;
        })).Returns(true);

        _mockEdgeDiagnosticsService
            .Setup(diagnosticsService => diagnosticsService.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => diagnosticsServiceStarted = true)
            .Returns(Task.CompletedTask);

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.StartAsync(It.IsAny<CancellationToken>()))
            .Callback(() => adapterStarted = true)
            .Returns(Task.CompletedTask);

        mockAdapter.Setup(adapter => adapter.ComponentType).Returns(TestAdapterType);
        mockAdapter.Setup(adapter => adapter.ComponentId).Returns(TestAdapterId);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(mockAdapter.Object);

        _mockSinkProvider.Setup(sinkProvider => sinkProvider.StartAsync())
                        .Callback(() => storageStarted = true)
                        .Returns(Task.CompletedTask);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection);

        await hostedComponentsService.StartAsync(CancellationToken.None);

        Assert.True(tryGetComponentCalled);
        Assert.True(tryGetGeneralCalled);
        Assert.False(trySaveGeneralCalled);
        Assert.True(storageStarted);
        Assert.True(adapterStarted);
        Assert.True(diagnosticsServiceStarted);
        Assert.True(testLogger.AreErrorsWarningsInLog());
    }

    [Fact]
    public async Task HostedComponentsService_GeneralConfigMetadataChanged_Test()
    {
        var edsExists = true;
        var testLogger = new TestLogger();
        var diagnosticsResendDataCalled = false;
        var adapterResendDataCalled = false;
        _mockEdgeDiagnosticsService.Setup(x => x.ResendTypesAndStreams()).Callback(() => diagnosticsResendDataCalled = true);
        _mockComponentsOperationService.Setup(x => x.ResendHealthMetadata()).Callback(() => adapterResendDataCalled = true);

        var generalConfigFunction = await GetGeneralConfigurationOnChangeFunctionAsync(testLogger, edsExists);

        var oldConfiguration = new GeneralConfiguration
        {
            EnableDiagnostics = false,
            MetadataLevel = MetadataInfo.None,
        };

        var newConfiguration = new GeneralConfiguration
        {
            EnableDiagnostics = false,
            MetadataLevel = MetadataInfo.Medium,
        };

        var configChange = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        _mockApplicationManifest.Setup(applicationManifest =>
            applicationManifest.IsEdgeDataStore).Returns(edsExists);

        generalConfigFunction.Invoke(configChange);

        Assert.False(diagnosticsResendDataCalled);
        Assert.True(adapterResendDataCalled);

        diagnosticsResendDataCalled = adapterResendDataCalled = false;
        oldConfiguration = null;
        configChange = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        _mockApplicationManifest.Setup(applicationManifest =>
           applicationManifest.IsEdgeDataStore).Returns(edsExists);

        generalConfigFunction.Invoke(configChange);

        Assert.False(diagnosticsResendDataCalled);
        Assert.True(adapterResendDataCalled);
    }

    [Fact]
    public async Task HostedComponentsService_GeneralConfigDiagnosticsChanged_Test()
    {
        var edsExists = true;
        var testLogger = new TestLogger();
        var diagnosticsResendDataCalled = false;
        var adapterResendDataCalled = false;
        _mockEdgeDiagnosticsService.Setup(x => x.ResendTypesAndStreams()).Callback(() => diagnosticsResendDataCalled = true);
        _mockComponentsOperationService.Setup(x => x.ResendHealthMetadata()).Callback(() => adapterResendDataCalled = true);

        var generalConfigFunction = await GetGeneralConfigurationOnChangeFunctionAsync(testLogger, edsExists);

        // old config to new config test
        var oldConfiguration = new GeneralConfiguration
        {
            EnableDiagnostics = false,
            MetadataLevel = MetadataInfo.Medium,
        };

        var newConfiguration = new GeneralConfiguration
        {
            EnableDiagnostics = true,
            MetadataLevel = MetadataInfo.Medium,
        };

        var configChange = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        _mockApplicationManifest.Setup(applicationManifest =>
            applicationManifest.IsEdgeDataStore).Returns(edsExists);

        generalConfigFunction.Invoke(configChange);

        Assert.True(diagnosticsResendDataCalled);
        Assert.True(adapterResendDataCalled);

        // no config to new config test
        diagnosticsResendDataCalled = adapterResendDataCalled = false;
        oldConfiguration = null;
        configChange = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        _mockApplicationManifest.Setup(applicationManifest =>
           applicationManifest.IsEdgeDataStore).Returns(edsExists);

        generalConfigFunction.Invoke(configChange);

        Assert.True(diagnosticsResendDataCalled);
        Assert.True(adapterResendDataCalled);
    }

    [Fact]
    public async Task HostedComponentsService_GeneralConfigNoChange_Test()
    {
        var edsExists = true;
        var testLogger = new TestLogger();
        var diagnosticsResendDataCalled = false;
        var adapterResendDataCalled = false;
        _mockEdgeDiagnosticsService.Setup(x => x.ResendTypesAndStreams()).Callback(() => diagnosticsResendDataCalled = true);
        _mockComponentsOperationService.Setup(x => x.ResendHealthMetadata()).Callback(() => adapterResendDataCalled = true);

        var generalConfigFunction = await GetGeneralConfigurationOnChangeFunctionAsync(testLogger, edsExists);

        // old config to new config test
        var oldConfiguration = new GeneralConfiguration
        {
            EnableDiagnostics = true,
            MetadataLevel = MetadataInfo.Medium,
        };

        var newConfiguration = new GeneralConfiguration
        {
            EnableDiagnostics = true,
            MetadataLevel = MetadataInfo.Medium,
        };

        var configChange = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        _mockApplicationManifest.Setup(applicationManifest =>
            applicationManifest.IsEdgeDataStore).Returns(edsExists);

        generalConfigFunction.Invoke(configChange);

        Assert.False(diagnosticsResendDataCalled);
        Assert.False(adapterResendDataCalled);
    }

    [Fact]
    public async Task HostedComponentsService_StopAsync_Test()
    {
        var storageStopped = false;
        var adapterStopped = false;
        var diagnosticsServiceStopped = false;
        var testLogger = new TestLogger();

        var platformConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = TestAdapterType,
                ComponentId = TestAdapterId,
            },
        };

        ICollection<string> errors;
        _mockConfigurationProvider
            .Setup(configurationProvider =>
                configurationProvider.TryGetConfiguration<EdgeComponentConfig[]>(It.IsAny<string>(),
                    It.IsAny<string>(), out platformConfiguration, out errors)).Returns(true);

        _mockEdgeDiagnosticsService
            .Setup(diagnosticsService => diagnosticsService.StopAsync(It.IsAny<CancellationToken>()))
            .Callback(() => diagnosticsServiceStopped = true)
            .Returns(Task.CompletedTask);

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.StopAsync(It.IsAny<CancellationToken>()))
            .Callback(() => adapterStopped = true)
            .Returns(Task.CompletedTask);

        mockAdapter.Setup(adapter => adapter.ComponentType).Returns(TestAdapterType);
        mockAdapter.Setup(adapter => adapter.ComponentId).Returns(TestAdapterId);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(mockAdapter.Object);

        _mockSinkProvider.Setup(sinkProvider => sinkProvider.StopAsync())
                        .Callback(() => storageStopped = true)
                        .Returns(Task.CompletedTask);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetAdapters())
            .Returns(new List<IEdgeAdapter> { mockAdapter.Object });

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection);

        await hostedComponentsService.StopAsync(CancellationToken.None);

        Assert.True(storageStopped);
        Assert.True(adapterStopped);
        Assert.True(diagnosticsServiceStopped);
        Assert.False(testLogger.AreErrorsWarningsInLog());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedComponentsService_ComponentsValidationFunction_TwoSinkComponents(bool edsComponentPresent)
    {
        var multipleEgressMessage = "Cannot add another component of 'OmfEgress' type.";
        var multipleEDSMessage = "Cannot add another component of 'Storage' type.";
        var testLogger = new TestLogger();

        var componentsValidationFunction = await GetComponentsValidationFunctionAsync(testLogger, edsComponentPresent);

        var oldConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = EdgeSystemConstants.OmfEgressComponentType,
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
            },
        };

        var newConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
                ComponentType = edsComponentPresent ? EdgeSystemConstants.StorageComponentType : EdgeSystemConstants.OmfEgressComponentType,
            },
            new EdgeComponentConfig
            {
                ComponentId = "AnotherEgress",
                ComponentType = edsComponentPresent ? EdgeSystemConstants.StorageComponentType : EdgeSystemConstants.OmfEgressComponentType,
            },
        };

        var invalidConfiguration = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        _mockApplicationManifest.Setup(applicationManifest =>
            applicationManifest.IsEdgeDataStore).Returns(edsComponentPresent);

        var result = componentsValidationFunction.Invoke(invalidConfiguration);

        Assert.NotEmpty(result);
        Assert.Equal(edsComponentPresent ? multipleEDSMessage : multipleEgressMessage, result.ToList()[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedComponentsService_ComponentsValidationFunction_NoSinkComponent(bool edsComponentPresent)
    {
        var missingEDSMessage = "Cannot delete Storage component, since exactly one Storage component must be registered.";
        var missingEgressMessage = "Cannot delete OmfEgress component, since exactly one OmfEgress component must be registered.";
        var testLogger = new TestLogger();

        var componentsValidationFunction = await GetComponentsValidationFunctionAsync(testLogger, edsComponentPresent);

        var oldConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = EdgeSystemConstants.OmfEgressComponentType,
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
            },
        };

        var newConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = TestAdapterType, ComponentId = "SomeAdapterId",
            },
        };

        var invalidConfiguration = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        _mockApplicationManifest.Setup(applicationManifest =>
            applicationManifest.IsEdgeDataStore).Returns(edsComponentPresent);

        var result = componentsValidationFunction.Invoke(invalidConfiguration);

        Assert.NotEmpty(result);
        Assert.Equal(edsComponentPresent ? missingEDSMessage : missingEgressMessage, result.ToList()[0]);

        newConfiguration = Array.Empty<EdgeComponentConfig>();

        invalidConfiguration = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        result = componentsValidationFunction.Invoke(invalidConfiguration);

        Assert.NotEmpty(result);
        Assert.Equal(edsComponentPresent ? missingEDSMessage : missingEgressMessage, result.ToList()[0]);
    }

    [Fact]
    public async Task HostedComponentsService_ComponentsValidationFunction_ConfigurationDeleted()
    {
        var cannotDeleteComponentsConfigurationMessage = "Entire components configuration cannot be deleted. Individual components can be deleted by ID.";
        var testLogger = new TestLogger();

        var componentsValidationFunction = await GetComponentsValidationFunctionAsync(testLogger, false);

        var oldConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = EdgeSystemConstants.OmfEgressComponentType,
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
            },
        };

        EdgeComponentConfig[] newConfiguration = null;

        var invalidConfiguration = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        var result = componentsValidationFunction.Invoke(invalidConfiguration);

        Assert.NotEmpty(result);
        Assert.Equal(cannotDeleteComponentsConfigurationMessage, result.ToList()[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HostedComponentsService_ComponentsValidationFunction_InvalidAdapterType(bool edsComponentPresent)
    {
        var nonExistentAdapterTypeWithEgressMessage = "Adapter type 'NonExistentAdapterType' isn't registered. Please use the registered adapter type: TestAdapter.";
        var nonExistentAdapterTypeWithEDSMessage = "Adapter type 'NonExistentAdapterType' isn't registered. Please use one of the registered adapter types: TestAdapter.";
        var testLogger = new TestLogger();

        var componentsValidationFunction = await GetComponentsValidationFunctionAsync(testLogger, edsComponentPresent);

        var oldConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = EdgeSystemConstants.OmfEgressComponentType,
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
            },
        };

        var newConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
                ComponentType = EdgeSystemConstants.OmfEgressComponentType,
            },
            new EdgeComponentConfig
            {
                ComponentId = "NonExistentAdapter",
                ComponentType = "NonExistentAdapterType",
            },
        };

        var invalidConfiguration = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);
        var result = componentsValidationFunction.Invoke(invalidConfiguration);

        Assert.NotEmpty(result);
        Assert.Equal(edsComponentPresent ? nonExistentAdapterTypeWithEDSMessage : nonExistentAdapterTypeWithEgressMessage, result.ToList()[0]);
    }

    [Fact]
    public async Task HostedComponentsService_ComponentsValidationFunction_ValidConfiguration()
    {
        var testLogger = new TestLogger();

        var componentsValidationFunction = await GetComponentsValidationFunctionAsync(testLogger, false);

        var oldConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = EdgeSystemConstants.OmfEgressComponentType,
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
            },
        };

        var newConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentId = EdgeSystemConstants.OmfEgressComponentId,
                ComponentType = EdgeSystemConstants.OmfEgressComponentType,
            },
            new EdgeComponentConfig
            {
                ComponentId = "ValidAdapter",
                ComponentType = TestAdapterType,
            },
        };

        var validConfiguration = new ConfigurationChangedEventArgs(oldConfiguration, newConfiguration);

        var result = componentsValidationFunction.Invoke(validConfiguration);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_DeleteWithPreviousConfig_WarnsRestart()
    {
        const string expectedWarning = "Buffering configuration change will take effect only on Adapter restart.";
        var testLogger = new TestLogger();
        var oldBufferingConfig = new BufferingConfiguration();

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(oldBufferingConfig, null));

        var logMessages = testLogger.GetLogMessages();
        var warningLog = logMessages.FirstOrDefault(log => log.LogMessage == expectedWarning);

        Assert.NotNull(warningLog);
        Assert.Equal(LogLevel.Warning, warningLog.LogLevel);
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_DeleteWithoutPreviousConfig_NoAction()
    {
        var testLogger = new TestLogger();

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(null, null));

        var logMessages = testLogger.GetLogMessages();
        Assert.DoesNotContain(logMessages, log => log.LogMessage.Contains("Buffering configuration change", StringComparison.Ordinal));
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_InitialConfig_NoAction()
    {
        var testLogger = new TestLogger();
        var newBufferingConfig = new BufferingConfiguration
        {
            BufferLocation = "C:/Buffers",
            MaxBufferSizeMB = 32,
            EnablePersistentBuffering = true,
            MaxDataBulkTime = TimeSpan.FromSeconds(2),
        };

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(null, newBufferingConfig));

        var logMessages = testLogger.GetLogMessages();
        Assert.DoesNotContain(logMessages, log => log.LogMessage.Contains("Buffering configuration change", StringComparison.Ordinal));
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_RuntimeUpdateOnly_UpdatesBufferSize()
    {
        var testLogger = new TestLogger();
        var oldBufferingConfig = new BufferingConfiguration();
        var newBufferingConfig = new BufferingConfiguration { MaxBufferSizeMB = 20, EnablePersistentBuffering = true };

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(oldBufferingConfig, newBufferingConfig));

        var logMessages = testLogger.GetLogMessages();
        Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Information && log.LogMessage.Contains("Buffering configuration change applied. New MaxBufferSizeMB: 20", StringComparison.Ordinal));
        Assert.DoesNotContain(logMessages, log => log.LogMessage.Contains("Adapter restart for:", StringComparison.Ordinal));
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(20), Times.Once);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_RuntimeUpdateWithOtherChanges_WarnsRestart()
    {
        var testLogger = new TestLogger();
        var oldBufferingConfig = new BufferingConfiguration();
        var newBufferingConfig = new BufferingConfiguration
        {
            MaxBufferSizeMB = 20,
            EnablePersistentBuffering = true,
            BufferLocation = "C:/NewPath",
        };

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(oldBufferingConfig, newBufferingConfig));

        var logMessages = testLogger.GetLogMessages();
        Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Information && log.LogMessage.Contains("Buffering configuration change applied. New MaxBufferSizeMB: 20", StringComparison.Ordinal));
        Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Warning && log.LogMessage == "Buffering configuration change will take effect only on Adapter restart for: BufferLocation.");
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(20), Times.Once);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_RuntimeUpdateWithoutDataEndpointManager_WarnsAndRequiresRestart()
    {
        var testLogger = new TestLogger();
        var oldBufferingConfig = new BufferingConfiguration();
        var newBufferingConfig = new BufferingConfiguration
        {
            MaxBufferSizeMB = 20,
            EnablePersistentBuffering = true,
            BufferLocation = "C:/NewPath",
        };

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger, includeDataEndpointManager: false);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(oldBufferingConfig, newBufferingConfig));

        var logMessages = testLogger.GetLogMessages();
        Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Warning && log.LogMessage.Contains("requires data endpoint manager initialization", StringComparison.Ordinal));
        Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Warning && log.LogMessage == "Buffering configuration change will take effect only on Adapter restart for: BufferLocation, MaxBufferSizeMB.");
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_MaxBufferSizeChangedWithPersistentDisabled_RequiresRestart()
    {
        var testLogger = new TestLogger();
        var oldBufferingConfig = new BufferingConfiguration
        {
            EnablePersistentBuffering = false,
            MaxBufferSizeMB = 10,
        };

        var newBufferingConfig = new BufferingConfiguration
        {
            EnablePersistentBuffering = false,
            MaxBufferSizeMB = 25,
        };

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(oldBufferingConfig, newBufferingConfig));

        var logMessages = testLogger.GetLogMessages();
        Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Warning && log.LogMessage == "Buffering configuration change will take effect only on Adapter restart for: MaxBufferSizeMB.");
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false, false, "BufferLocation")]
    [InlineData(false, true, false, "EnablePersistentBuffering")]
    [InlineData(false, false, true, "MaxDataBulkTime")]
    [InlineData(true, true, true, "BufferLocation, EnablePersistentBuffering, MaxDataBulkTime")]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_OtherPropertiesChanged_RequiresRestart(
        bool changeBufferLocation,
        bool changePersistentBuffering,
        bool changeMaxDataBulkTime,
        string expectedProperties)
    {
        var testLogger = new TestLogger();
        var oldBufferingConfig = new BufferingConfiguration
        {
            BufferLocation = "C:/BuffersA",
            EnablePersistentBuffering = true,
            MaxBufferSizeMB = 10,
            MaxDataBulkTime = TimeSpan.FromSeconds(2),
        };

        var newBufferingConfig = new BufferingConfiguration
        {
            BufferLocation = changeBufferLocation ? "C:/BuffersB" : oldBufferingConfig.BufferLocation,
            EnablePersistentBuffering = changePersistentBuffering ? !oldBufferingConfig.EnablePersistentBuffering : oldBufferingConfig.EnablePersistentBuffering,
            MaxBufferSizeMB = oldBufferingConfig.MaxBufferSizeMB,
            MaxDataBulkTime = changeMaxDataBulkTime ? TimeSpan.FromSeconds(5) : oldBufferingConfig.MaxDataBulkTime,
        };

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(oldBufferingConfig, newBufferingConfig));

        var logMessages = testLogger.GetLogMessages();
        var expectedMessage = $"Buffering configuration change will take effect only on Adapter restart for: {expectedProperties}.";

        Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Warning && log.LogMessage == expectedMessage);
        Assert.DoesNotContain(logMessages, log => log.LogLevel == LogLevel.Information && log.LogMessage.Contains("Buffering configuration change applied", StringComparison.Ordinal));
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task HostedComponentsService_ValidationFunction_BufferingChange_NoEffectiveChange_NoAction()
    {
        var testLogger = new TestLogger();
        var oldBufferingConfig = new BufferingConfiguration
        {
            BufferLocation = "C:/BuffersA",
            EnablePersistentBuffering = true,
            MaxBufferSizeMB = 10,
            MaxDataBulkTime = TimeSpan.FromSeconds(2),
        };

        var newBufferingConfig = new BufferingConfiguration
        {
            BufferLocation = oldBufferingConfig.BufferLocation,
            EnablePersistentBuffering = oldBufferingConfig.EnablePersistentBuffering,
            MaxBufferSizeMB = oldBufferingConfig.MaxBufferSizeMB,
            MaxDataBulkTime = oldBufferingConfig.MaxDataBulkTime,
        };

        var bufferingChangedAction = await BufferingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        bufferingChangedAction.Invoke(new ConfigurationChangedEventArgs(oldBufferingConfig, newBufferingConfig));

        var logMessages = testLogger.GetLogMessages();
        Assert.DoesNotContain(logMessages, log => log.LogMessage.Contains("Buffering configuration change", StringComparison.Ordinal));
        _mockOmfDataEndpointManager.Verify(m => m.UpdateBufferSize(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(DefaultLogLevel, 20, DefaultLogFileSizeLimitBytes)]
    [InlineData(DefaultLogLevel, DefaultLogFileCountLimit, 34636822)]
    [InlineData(LogLevel.Critical, DefaultLogFileCountLimit, DefaultLogFileSizeLimitBytes)]
    public async Task HostedComponentsService_ValidationFunction_LoggingChange(LogLevel logLevel, int logFileCountLimit, long logFileSizeLimit)
    {
        const string ConfigChangeMessage = "Logging configuration change will take effect only on Adapter restart.";
        var testLogger = new TestLogger();
        var oldLoggerConfiguration = new LoggerConfiguration();
        var newLoggerConfiguration = new LoggerConfiguration
        {
            LogLevel = logLevel, LogFileCountLimit = logFileCountLimit, LogFileSizeLimitBytes = logFileSizeLimit,
        };

        var componentsChangedActionFunctionAsync = await LoggingConfigurationChangedActionAsync(testLogger);
        testLogger.ClearLog();

        var configurationChangedEvent = new ConfigurationChangedEventArgs(oldLoggerConfiguration, newLoggerConfiguration);
        componentsChangedActionFunctionAsync.Invoke(configurationChangedEvent);

        var logMessages = testLogger.GetLogMessages();
        _mockLoggerConfigurator.Verify(loggerConfigurator => loggerConfigurator.SetMinimumLogLevel(logLevel), Times.Once);

        if (logFileCountLimit != DefaultLogFileCountLimit || logFileSizeLimit != DefaultLogFileSizeLimitBytes)
        {
            Assert.Contains(logMessages, log => log.LogLevel == LogLevel.Warning && log.LogMessage == ConfigChangeMessage);
        }
        else
        {
            Assert.DoesNotContain(logMessages, log => log.LogLevel == LogLevel.Warning && log.LogMessage == ConfigChangeMessage);
        }
    }

    private async Task<Action<ConfigurationChangedEventArgs>> BufferingConfigurationChangedActionAsync(ILogger testLogger, bool includeDataEndpointManager = true)
    {
        Action<ConfigurationChangedEventArgs> callAction = null;

        var serviceCollection = new ServiceCollection();

        _mockRuntimeConfigurationRegistry.Setup(runtimeConfigRegistry => runtimeConfigRegistry.RegisterComponentConfiguration<BufferingConfiguration>(EdgeSystemConstants.SystemComponentId,
                EdgeSystemConstants.BufferingFacetName, It.IsAny<IConfigurationCommandGenerator>(), It.IsAny<Action<ConfigurationChangedEventArgs>>(),
                It.IsAny<Func<string>>(), It.IsAny<Func<ConfigurationChangedEventArgs, ICollection<string>>>(), It.IsAny<Operations>()))
            .Callback<string, string, IConfigurationCommandGenerator, Action<ConfigurationChangedEventArgs>, Func<string>, Func<ConfigurationChangedEventArgs, ICollection<string>>, Operations>(
                (componentId, facet, commandGenerator, callback, cmdHelp, customValidationFunction, operation) =>
                {
                    callAction = callback;
                });

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection, includeDataEndpointManager);

        await hostedComponentsService.StartAsync(CancellationToken.None);

        return callAction;
    }

    private async Task<Action<ConfigurationChangedEventArgs>> LoggingConfigurationChangedActionAsync(ILogger testLogger)
    {
        Action<ConfigurationChangedEventArgs> callAction = null;

        var serviceCollection = new ServiceCollection();

        _mockRuntimeConfigurationRegistry.Setup(runtimeConfigRegistry => runtimeConfigRegistry.RegisterComponentConfiguration<LoggerConfiguration>(EdgeSystemConstants.SystemComponentId,
                EdgeSystemConstants.LoggingFacetName, It.IsAny<IConfigurationCommandGenerator>(), It.IsAny<Action<ConfigurationChangedEventArgs>>(),
                It.IsAny<Func<string>>(), It.IsAny<Func<ConfigurationChangedEventArgs, ICollection<string>>>(), It.IsAny<Operations>()))
            .Callback<string, string, IConfigurationCommandGenerator, Action<ConfigurationChangedEventArgs>, Func<string>, Func<ConfigurationChangedEventArgs, ICollection<string>>, Operations>(
                (componentId, facet, commandGenerator, callback, cmdHelp, customValidationFunction, operation) =>
                {
                    callAction = callback;
                });

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection);

        await hostedComponentsService.StartAsync(CancellationToken.None);

        return callAction;
    }

    private HostedComponentsService CreateHostedComponentsService(ILogger logger, IServiceCollection serviceCollection, bool includeDataEndpointManager = true)
    {
        return new HostedComponentsService(logger,
            _mockLoggerConfigurator.Object,
            _mockComponentIdService.Object,
            _mockHealthEndpointManager.Object,
            _mockRuntimeConfigurationRegistry.Object,
            _mockRuntimeManagementRegistry.Object,
            _mockConfigurationProvider.Object,
            serviceCollection.BuildServiceProvider(),
            _mockRequestMetricsTracker.Object,
            _mockAllowRequestManager.Object,
            _mockComponentsRepository.Object,
            _mockComponentsOperationService.Object,
            _mockEdgeDiagnosticsService.Object,
            _mockSecretsManager.Object,
            _mockApplicationManifest.Object,
            _mockDiagnosticsMessageProcessor.Object,
            _mockSinkProvider.Object,
            _mockFailoverServiceProvider.Object,
            null,
            null,
            includeDataEndpointManager ? _mockOmfDataEndpointManager.Object : null);
    }

    private async Task<Func<ConfigurationChangedEventArgs, ICollection<string>>> GetComponentsValidationFunctionAsync(ILogger testLogger, bool edsComponentPresent)
    {
        Func<ConfigurationChangedEventArgs, ICollection<string>> componentsCustomValidationFunction = null;

        var platformConfiguration = new[]
        {
            new EdgeComponentConfig
            {
                ComponentType = TestAdapterType,
                ComponentId = TestAdapterId,
            },
        };

        ICollection<string> errors;
        _mockConfigurationProvider
            .Setup(configurationProvider =>
                configurationProvider.TryGetConfiguration<EdgeComponentConfig[]>(It.IsAny<string>(),
                    It.IsAny<string>(), out platformConfiguration, out errors)).Returns(true);

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.StopAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockAdapter.Setup(adapter => adapter.ComponentType).Returns(TestAdapterType);
        mockAdapter.Setup(adapter => adapter.ComponentId).Returns(TestAdapterId);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(mockAdapter.Object);

        _mockSinkProvider.Setup(sinkProvider => sinkProvider.StopAsync())
            .Returns(Task.CompletedTask);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetAdapters())
            .Returns(new List<IEdgeAdapter> { mockAdapter.Object });

        _mockRuntimeConfigurationRegistry.Setup(runtimeConfigRegistry => runtimeConfigRegistry.RegisterComponentConfiguration<EdgeComponentConfig[]>(It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<IConfigurationCommandGenerator>(), It.IsAny<Action<ConfigurationChangedEventArgs>>(),
                It.IsAny<Func<string>>(), It.IsAny<Func<ConfigurationChangedEventArgs, ICollection<string>>>(), It.IsAny<Operations>()))
            .Callback<string, string, IConfigurationCommandGenerator, Action<ConfigurationChangedEventArgs>, Func<string>, Func<ConfigurationChangedEventArgs, ICollection<string>>, Operations>(
                (componentId, facet, commandGenerator, callback, cmdHelp, customValidationFunction, operation) =>
                {
                    componentsCustomValidationFunction = customValidationFunction;
                });

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        _mockApplicationManifest.Setup(applicationManifest =>
            applicationManifest.IsEdgeDataStore).Returns(edsComponentPresent);

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection);

        await hostedComponentsService.StartAsync(CancellationToken.None);

        return componentsCustomValidationFunction;
    }

    private async Task<Action<ConfigurationChangedEventArgs>> GetGeneralConfigurationOnChangeFunctionAsync(ILogger testLogger, bool edsComponentPresent)
    {
        Action<ConfigurationChangedEventArgs> componentsCustomValidationFunction = null;

        var generalConfig = new GeneralConfiguration
        {
            EnableDiagnostics = true,
            MetadataLevel = MetadataInfo.Medium,
        };

        ICollection<string> errors;
        _mockConfigurationProvider
            .Setup(configurationProvider =>
                configurationProvider.TryGetConfiguration<GeneralConfiguration>(It.IsAny<string>(),
                    It.IsAny<string>(), out generalConfig, out errors)).Returns(true);

        var mockAdapter = new Mock<IEdgeAdapter>();
        mockAdapter.Setup(adapter => adapter.StopAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockAdapter.Setup(adapter => adapter.ComponentType).Returns(TestAdapterType);
        mockAdapter.Setup(adapter => adapter.ComponentId).Returns(TestAdapterId);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(mockAdapter.Object);

        _mockSinkProvider.Setup(sinkProvider => sinkProvider.StopAsync())
            .Returns(Task.CompletedTask);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetAdapters())
            .Returns(new List<IEdgeAdapter> { mockAdapter.Object });

        _mockRuntimeConfigurationRegistry.Setup(runtimeConfigRegistry => runtimeConfigRegistry.RegisterComponentConfiguration<GeneralConfiguration>(It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<IConfigurationCommandGenerator>(), It.IsAny<Action<ConfigurationChangedEventArgs>>(),
                It.IsAny<Func<string>>(), It.IsAny<Func<ConfigurationChangedEventArgs, ICollection<string>>>(), It.IsAny<Operations>()))
            .Callback<string, string, IConfigurationCommandGenerator, Action<ConfigurationChangedEventArgs>, Func<string>, Func<ConfigurationChangedEventArgs, ICollection<string>>, Operations>(
                (componentId, facet, commandGenerator, callback, cmdHelp, customValidationFunction, operation) =>
                {
                    componentsCustomValidationFunction = callback;
                });

        _mockComponentsRepository.Setup(componentsRepo => componentsRepo.GetSinkProvider())
            .Returns(_mockSinkProvider.Object);

        _mockApplicationManifest.Setup(applicationManifest =>
            applicationManifest.IsEdgeDataStore).Returns(edsComponentPresent);

        var hostedComponentsService = CreateHostedComponentsService(testLogger, serviceCollection);

        await hostedComponentsService.StartAsync(CancellationToken.None);

        return componentsCustomValidationFunction;
    }

    private class SampleConfiguration
    {
        [Id]
        public string Id { get; set; }

        [Protected]
        public string Password { get; set; }
    }
}
