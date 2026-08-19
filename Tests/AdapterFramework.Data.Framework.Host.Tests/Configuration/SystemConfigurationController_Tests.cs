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
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Common;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Registry;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;
using ConfigurationProtector = AdapterFramework.Data.Framework.DataProtector.ConfigurationProtector;

namespace AdapterFramework.Data.Framework.Host.Tests.Configuration;

public class SystemConfigurationController_Tests
{
    private const string DiscoveriesFacetName = "Discoveries";
    private const string HistoryRecoveriesFacetName = "HistoryRecoveries";
    private const string ComponentId = "SampleComponent";
    private const string NonExistentComponentId = "NonExistent";
    private const string FacetName1 = "SomeFacet1";
    private const string FacetName2 = "SomeFacet2";
    private const string ConfigurationId = "Index0";
    private const string SampleValidationMessageString = "value is out of range. Supported range is 0-50.";

    private static JsonSerializerOptions _jsonSerializerOptions;
    private static JsonDocumentOptions _jsonDocumentOptions;
    private bool _callbackActionCalled;
    private ConfigurationChangedEventArgs _configurationChangedArgs;

    public SystemConfigurationController_Tests()
    {
        _configurationChangedArgs = null;
        _callbackActionCalled = false;

        _jsonSerializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        _jsonDocumentOptions = new JsonDocumentOptions();
    }

    private delegate void TrySaveConfigurationCallback(string id, string facet, object config, out ICollection<string> errors);
    private delegate void MockTrySaveCallback(string componentId, string configurationName, object configuration, out ICollection<string> err);
    private delegate void MockTryGetCallback(string componentId, string configurationName, Type configType, out object configuration, out ICollection<string> err);

    private interface ISampleConfiguration
    {
        string Index { get; set; }

        int IntProperty { get; set; }

        string Secret { get; set; }
    }

    [Fact]
    public void GetConfiguration_Component_NotFound()
    {
        using var configurationController = new SystemConfigurationController(null, new RuntimeConfigurationRegistry(), new ConfigurationProtector(null));
        var result = configurationController.GetConfiguration("NonExistentComponent", "NonExistentFacet", null, 0, 0);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public void GetConfiguration_Facet_NotFound()
    {
        IList<string> facets = new List<string> { FacetName1 };

        var mockRegistry = new Mock<IRuntimeConfigurationRegistry>();
        mockRegistry.Setup(registry => registry.TryGetAvailableFacets(It.IsAny<string>(), out facets)).Returns(true);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, mockRegistry.Object, null);
        var result = configurationController.GetConfiguration(ComponentId, FacetName1, null, 0, 0);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public void GetConfiguration_DataSelection_Discovery_Diff_Test()
    {
        const string DataSelectionFacetName = "dataSelection";
        var receivedDiscoveryId = string.Empty;
        var objectToReturn = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var expectedResult = new MvcResult(200, objectToReturn);

        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.GetDataSelectionDifference(It.IsAny<string>())).Returns(new MvcResult(200, objectToReturn))
            .Callback((string discoveryId) =>
            {
                receivedDiscoveryId = discoveryId;
            });

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.GetConfiguration(ComponentId, DataSelectionFacetName, ConfigurationId, 0, 0);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.Equal(expectedResult.Content, objectResult.Value);
        Assert.Equal(ConfigurationId, receivedDiscoveryId);
    }

    [Fact]
    public void GetConfiguration_Configuration_NotFound()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.GetConfiguration(ComponentId, FacetName1, null, 0, 0);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(JsonSerializer.Deserialize<JsonElement>("[]").GetRawText(), objectResult.Value.ToString());
    }

    [Fact]
    public void GetConfiguration_Configuration_Create_Update_Delete_NotAllowed()
    {
        var expectedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1);
        var expectedErrorMessage = $"is not supported on '{FacetName1}' configuration.";

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(expectedConfiguration);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1), null, null, null, Operations.Get);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.GetConfiguration(ComponentId, FacetName1, null, 0, 0);

        var goodObjectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, goodObjectResult.StatusCode);
        Assert.Equal(expectedConfiguration, goodObjectResult.Value);

        result = configurationController.PatchConfiguration(ComponentId, FacetName1, new JsonElement());
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(objectResult.StatusCode, StatusCodes.Status400BadRequest);
        Assert.Contains(expectedErrorMessage, ((RestApiErrorResponse)objectResult.Value).Error, StringComparison.InvariantCultureIgnoreCase);

        result = configurationController.PutConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(expectedConfiguration), _jsonSerializerOptions));
        objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(objectResult.StatusCode, StatusCodes.Status400BadRequest);
        Assert.Contains(expectedErrorMessage, ((RestApiErrorResponse)objectResult.Value).Error, StringComparison.InvariantCultureIgnoreCase);

        result = configurationController.DeleteConfiguration(ComponentId, FacetName1);
        objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(objectResult.StatusCode, StatusCodes.Status400BadRequest);
        Assert.Contains(expectedErrorMessage, ((RestApiErrorResponse)objectResult.Value).Error, StringComparison.InvariantCultureIgnoreCase);

        result = configurationController.DeleteConfiguration(ComponentId, FacetName1);
        objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(objectResult.StatusCode, StatusCodes.Status400BadRequest);
        Assert.Contains(expectedErrorMessage, ((RestApiErrorResponse)objectResult.Value).Error, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void GetConfiguration_Configuration_Array_Found()
    {
        var expectedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1);
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>()))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(expectedConfiguration);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.GetConfiguration(ComponentId, FacetName1, null, 0, 0);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(expectedConfiguration, objectResult.Value);
        Assert.True(maskSecretsCalled);
    }

    [Fact]
    public void GetConfiguration_Configuration_Simple_Found()
    {
        var expectedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>()))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration))).Returns(expectedConfiguration);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.GetConfiguration(ComponentId, FacetName1, null, 0, 0);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(expectedConfiguration, objectResult.Value);
        Assert.True(maskSecretsCalled);
    }

    [Fact]
    public void GetConfiguration_Configuration_Paging_Test()
    {
        var configuration = CreateConfigurationCollection<SampleIdConfiguration>(1000);
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration))).Returns(configuration);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.GetConfiguration(ComponentId, FacetName1, null, 0, 0);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(configuration.Length, ((object[])objectResult.Value).Length);

        const int Skip = 100;
        const int Count = 100;

        result = configurationController.GetConfiguration(ComponentId, FacetName1, null, Skip, Count);

        objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        var typedObject = Array.ConvertAll(((IEnumerable<object>)objectResult.Value).ToArray(), item => (SampleIdConfiguration)item);
        Assert.Equal(Count, typedObject.Length);
        Assert.Equal($"Index{Skip}", typedObject[0].Index);
    }

    [Fact]
    public void GetDiscoveryStates_DiscoveryManager_Registered_Unregistered_Test()
    {
        var objectToReturn = CreateConfigurationCollection<SampleIdConfiguration>(1);
        var expectedResult = new MvcResult(200, objectToReturn);

        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.GetDiscoveryStates()).Returns(new MvcResult(200, objectToReturn));

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.GetDiscoveryStates(ComponentId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.Equal(expectedResult.Content, objectResult.Value);

        configurationRegistry.UnregisterComponent(ComponentId);
        result = configurationController.GetDiscoveryStates(ComponentId);

        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCodeResult.StatusCode);
    }

    [Fact]
    public void GetDiscoveryStateById_DiscoveryManager_Registered_Unregistered_Test()
    {
        var objectToReturn = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var expectedResult = new MvcResult(200, objectToReturn);

        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.GetDiscoveryState(ConfigurationId)).Returns(new MvcResult(200, objectToReturn));

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.GetDiscoveryStateById(ComponentId, ConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.Equal(expectedResult.Content, objectResult.Value);

        configurationRegistry.UnregisterComponent(ComponentId);

        result = configurationController.GetDiscoveryStateById(ComponentId, ConfigurationId);

        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCodeResult.StatusCode);
    }

    [Fact]
    public void GetDiscoveryResultById_DiscoveryManager_Registered_Unregistered_Test()
    {
        const string ConfigurationBId = "ConfigurationIdB";
        const int SkipValue = 42;
        const int CountValue = 21;
        var receivedSkipValue = 0;
        var receivedCountValue = 0;
        var receivedDiscoveryIdA = string.Empty;
        var receivedDiscoveryIdB = string.Empty;
        var objectToReturn = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var expectedResult = new MvcResult(200, objectToReturn);

        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.GetDiscoveryResult(ConfigurationId, It.IsAny<DiscoveryOptions>())).Returns(new MvcResult(200, objectToReturn))
            .Callback((string componentId, DiscoveryOptions options) =>
            {
                receivedCountValue = options.Count;
                receivedSkipValue = options.Skip;
            });

        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.GetDiscoveriesDifference(It.IsAny<string>(), It.IsAny<string>())).Returns(new MvcResult(200, objectToReturn))
            .Callback((string discoveryIdA, string discoveryIdB) =>
            {
                receivedDiscoveryIdA = discoveryIdA;
                receivedDiscoveryIdB = discoveryIdB;
            });

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.GetDiscoveryResultById(ComponentId, ConfigurationId, null, CountValue, SkipValue);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.Equal(expectedResult.Content, objectResult.Value);
        Assert.Equal(SkipValue, receivedSkipValue);
        Assert.Equal(CountValue, receivedCountValue);

        result = configurationController.GetDiscoveryResultById(ComponentId, ConfigurationId, ConfigurationBId, CountValue, SkipValue);
        objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.Equal(expectedResult.Content, objectResult.Value);
        Assert.Equal(ConfigurationId, receivedDiscoveryIdA);
        Assert.Equal(ConfigurationBId, receivedDiscoveryIdB);

        configurationRegistry.UnregisterComponent(ComponentId);

        result = configurationController.GetDiscoveryResultById(ComponentId, ConfigurationId, null, CountValue, SkipValue);

        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCodeResult.StatusCode);
    }

    [Theory]
    [InlineData("accept-verbosity", "Verbose", true)]
    [InlineData("Accept-Verbosity", "verbose", true)]
    [InlineData("accept-verbosity", "none", false)]
    [InlineData("some-header", "verbose", false)]
    public void GetComponentConfigurations_Configuration_Found(string headerKey, string headerValue, bool verboseOutput)
    {
        var expectedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1);
        var maskSecretsCalled = false;
        var historyRecoveryResult = "Hello!";
        var expectedVerboseConfiguration = new Dictionary<string, object>
        {
            { FacetName1, expectedConfiguration },
            { FacetName2, expectedConfiguration },
            { DiscoveriesFacetName, expectedConfiguration },
            { HistoryRecoveriesFacetName, historyRecoveryResult },
        };

        var expectedDefaultConfiguration = new Dictionary<string, object>
        {
            { FacetName1, expectedConfiguration },
        };

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>()))
                    .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(expectedConfiguration);
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName2, typeof(SampleIdConfiguration[]))).Returns(expectedConfiguration);
        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.GetDiscoveryStates())
                    .Returns(new MvcResult(HttpStatusCode.OK, expectedConfiguration));

        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor
            .Setup(recoveryProcessor => recoveryProcessor.GetHistoryRecoveryStates())
                    .Returns(new MvcResult(HttpStatusCode.OK, historyRecoveryResult));

        var mockHistoryRecoveryManager = new Mock<IHistoryRecoveryManager>();
        mockHistoryRecoveryManager.Setup(recoveryManager => recoveryManager.OnDemandHistoryRecoveryProcessor)
                    .Returns(mockOnDemandHistoryRecoveryProcessor.Object);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);
        configurationRegistry.RegisterHistoryRecoveryManager(ComponentId, mockHistoryRecoveryManager.Object);
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1), null, null);
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName2,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName2), null, null, null, Operations.Get);

        var testLogger = new TestLogger();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[headerKey] = headerValue;

        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            },
        };

        var result = configurationController.GetComponentConfigurations(ComponentId);
        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(verboseOutput ? expectedVerboseConfiguration : expectedDefaultConfiguration, objectResult.Value);
        Assert.True(maskSecretsCalled);
    }

    [Theory]
    [InlineData("accept-verbosity", "Verbose", true)]
    [InlineData("Accept-Verbosity", "verbose", true)]
    [InlineData("accept-verbosity", "none", false)]
    [InlineData("some-header", "verbose", false)]
    public void GetAllConfigurations_Configuration_Found(string headerKey, string headerValue, bool verboseOutput)
    {
        var expectedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1);
        var maskSecretsCalled = false;
        var historyRecoveryResult = "Hello!";
        var expectedVerboseConfiguration = new Dictionary<string, Dictionary<string, object>>
        {
            {
                ComponentId, new Dictionary<string, object>
                {
                    { FacetName1, expectedConfiguration },
                    { FacetName2, expectedConfiguration },
                    { DiscoveriesFacetName, expectedConfiguration },
                    { HistoryRecoveriesFacetName, historyRecoveryResult },
                }
            },
        };

        var expectedDefaultConfiguration = new Dictionary<string, Dictionary<string, object>>
        {
            {
                ComponentId, new Dictionary<string, object>
                {
                    { FacetName1, expectedConfiguration },
                }
            },
        };

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector =>
                configProtector.MaskSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>()))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(expectedConfiguration);

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName2, typeof(SampleIdConfiguration[])))
            .Returns(expectedConfiguration);

        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.GetDiscoveryStates())
            .Returns(new MvcResult(HttpStatusCode.OK, expectedConfiguration));

        var mockOnDemandHistoryRecoveryProcessor = new Mock<IOnDemandHistoryRecoveryProcessor>();
        mockOnDemandHistoryRecoveryProcessor
            .Setup(recoveryProcessor => recoveryProcessor.GetHistoryRecoveryStates())
            .Returns(new MvcResult(HttpStatusCode.OK, historyRecoveryResult));

        var mockHistoryRecoveryManager = new Mock<IHistoryRecoveryManager>();
        mockHistoryRecoveryManager.Setup(recoveryManager => recoveryManager.OnDemandHistoryRecoveryProcessor)
            .Returns(mockOnDemandHistoryRecoveryProcessor.Object);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);
        configurationRegistry.RegisterHistoryRecoveryManager(ComponentId, mockHistoryRecoveryManager.Object);
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1), null, null);
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName2,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName2), null, null, null, Operations.Get);

        var testLogger = new TestLogger();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[headerKey] = headerValue;

        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            },
        };

        var result = configurationController.GetAllConfigurations();
        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(verboseOutput ? expectedVerboseConfiguration : expectedDefaultConfiguration, objectResult.Value);
        Assert.True(maskSecretsCalled);
    }

    [Fact]
    public void GetConfigurationById_Configuration_IdNotFound()
    {
        const string NonExistentConfigurationId = "nonExistent";
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(3);
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, typeof(SampleIdConfiguration)))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(existingConfiguration);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1), null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.GetConfigurationById(ComponentId, FacetName1, NonExistentConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.False(maskSecretsCalled);
    }

    [Fact]
    public void GetConfigurationById_Configuration_IncompatibleType()
    {
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(3);
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>())).Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleConfiguration[])))
            .Returns(existingConfiguration);

        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1);
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleConfiguration[]>(ComponentId, FacetName1, commandGenerator, null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.GetConfigurationById(ComponentId, FacetName1, ConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.False(maskSecretsCalled);
    }

    [Fact]
    public void GetConfigurationById_Configuration_Found()
    {
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(3);
        var expectedConfigurationEntry = existingConfiguration[0];
        var maskSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.MaskSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>()))
            .Callback(() => maskSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(existingConfiguration);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.GetConfigurationById(ComponentId, FacetName1, ConfigurationId);

        var objectResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(expectedConfigurationEntry, objectResult.Value);
        Assert.True(maskSecretsCalled);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void GetConfiguration_InvalidConfiguration()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Throws(new Exception());

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.GetConfiguration(ComponentId, FacetName1, null, 0, 0);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public void GetAllConfigurations_InvalidConfiguration()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Throws(new Exception());

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = configurationController.GetAllConfigurations();
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public void GetComponentConfigurations_InvalidConfiguration()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Throws(new Exception());

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            null, null);

        var testLogger = new TestLogger();

        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = configurationController.GetComponentConfigurations(ComponentId);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public void GetConfigurationById_InvalidConfiguration()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Throws(new Exception());

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1), null, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.GetConfigurationById(ComponentId, FacetName1, ConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
    }

    [Theory]
    [InlineData(JsonValueKind.Object, true, "test")]
    [InlineData(JsonValueKind.Object, true, null)]
    [InlineData(JsonValueKind.Array, false, null)]
    public void StartDiscovery_Test(JsonValueKind inputValueKind, bool shouldSucceed, string scheduleId)
    {
        var expectedResult = new MvcResult(200);
        var expectedInvalidResult = new MvcResult(400);
        var expectedDiscoveryState = new DiscoveryState { Id = ConfigurationId, AutoSelect = true, };
        var invalidDiscoveryState = new[] { new DiscoveryState(), new DiscoveryState(), };

        DiscoveryOptions receivedDiscoveryOptions = null;
        DiscoveryState receivedDiscoveryState = null;
        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.StartDiscovery(It.IsAny<DiscoveryState>(), It.IsAny<DiscoveryOptions>()))
            .Callback((DiscoveryState discoveryState, DiscoveryOptions options) =>
            {
                receivedDiscoveryState = discoveryState;
                receivedDiscoveryOptions = options;
            }).Returns(expectedResult);

        ICollection<string> errors = new List<string>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.IsConfigurationValid(It.IsAny<object>(), out errors))
            .Returns(true);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.StartDiscovery(mockConfigurationProvider.Object, ComponentId, new JsonElement(), scheduleId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.NotNull(receivedDiscoveryState);
        Assert.NotNull(receivedDiscoveryOptions);
        Assert.Equal(scheduleId, receivedDiscoveryOptions.ScheduleId);

        result = configurationController.StartDiscovery(mockConfigurationProvider.Object, ComponentId, (inputValueKind == JsonValueKind.Array)
                ? JsonSerializer.Deserialize<JsonElement>(Serialize(invalidDiscoveryState), _jsonSerializerOptions)
                : JsonSerializer.Deserialize<JsonElement>(Serialize(expectedDiscoveryState), _jsonSerializerOptions),
            scheduleId);

        objectResult = Assert.IsType<ObjectResult>(result);

        if (shouldSucceed)
        {
            Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
            Assert.Equal(expectedDiscoveryState.AutoSelect, receivedDiscoveryState.AutoSelect);
            Assert.Equal(expectedDiscoveryState.Id, receivedDiscoveryState.Id);
            Assert.Equal(scheduleId, receivedDiscoveryOptions.ScheduleId);
        }
        else
        {
            Assert.Equal(expectedInvalidResult.StatusCode, objectResult.StatusCode);
        }
    }

    [Theory]
    [InlineData("2026-02-06T01:00:23.6068069-05:00", null, 0, 0, 0, null, OperationStatus.Active, null)]
    [InlineData(null, "2026-02-06T01:00:23.6068069-05:00", 0, 0, 0, null, OperationStatus.Active, null)]
    [InlineData(null, null, 100, 0, 0, null, OperationStatus.Active, null)]
    [InlineData(null, null, 0, 100, 0, null, OperationStatus.Active, null)]
    [InlineData(null, null, 0, 0, 100, null, OperationStatus.Active, null)]
    [InlineData(null, null, 0, 0, 0, "fakeUri", OperationStatus.Active, null)]
    [InlineData(null, null, 0, 0, 0, null, OperationStatus.Failed, null)]
    [InlineData(null, null, 0, 0, 0, null, OperationStatus.Active, "FakeErrors")]
    public void StartDiscovery_ReadonlyFields_Test(string startTime, string endTime, int progress, int itemsFound, int newItems, string resultUri, OperationStatus status, string discoveryErrors)
    {
        var expectedInvalidResult = new MvcResult(400);

        DateTime? realStartTime;
        DateTime? realEndTime;

        if (DateTime.TryParse(startTime, out var parsedStartTime))
        {
            realStartTime = parsedStartTime;
        }
        else
        {
            realStartTime = null;
        }

        if (DateTime.TryParse(endTime, out var parsedEndTime))
        {
            realEndTime = parsedEndTime;
        }
        else
        {
            realEndTime = null;
        }

        var invalidDiscoveryState = new DiscoveryState
        {
            StartTime = realStartTime,
            EndTime = realEndTime,
            Progress = progress,
            ItemsFound = itemsFound,
            NewItems = newItems,
            ResultUri = resultUri,
            Status = status,
            Errors = discoveryErrors,
        };

        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();

        ICollection<string> errors = new List<string>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.IsConfigurationValid(It.IsAny<object>(), out errors))
            .Returns(true);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.StartDiscovery(mockConfigurationProvider.Object, ComponentId, JsonSerializer.SerializeToElement(invalidDiscoveryState, _jsonSerializerOptions), null);
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.True(objectResult.StatusCode.Equals(expectedInvalidResult.StatusCode));
    }

    [Theory]
    [InlineData("2026-02-06T01:00:23.6068069-05:00", 0, 0, 0, OperationStatus.Active, null)]
    [InlineData(null, 100, 0, 0, OperationStatus.Active, null)]
    [InlineData(null, 0, 100, 0, OperationStatus.Active, null)]
    [InlineData(null, 0, 0, 100, OperationStatus.Active, null)]
    [InlineData(null, 0, 0, 0, OperationStatus.Failed, null)]
    [InlineData(null, 0, 0, 0, OperationStatus.Active, "FakeErrors")]
    public void StartOnDemandHistoryRecovery_ReadonlyFields_Test(string checkpoint, int progress, int items, int recoveredEvents, OperationStatus status, string historyRecoveryErrors)
    {
        var expectedInvalidResult = new MvcResult(400);

        DateTime? realCheckpoint;

        if (DateTime.TryParse(checkpoint, out var parsedCheckpoint))
        {
            realCheckpoint = parsedCheckpoint;
        }
        else
        {
            realCheckpoint = null;
        }

        var invalidHistoryRecoveryState = new HistoryRecoveryState
        {
            StartTime = DateTime.UtcNow.AddSeconds(-1),
            Checkpoint = realCheckpoint,
            Progress = progress,
            Items = items,
            RecoveredEvents = recoveredEvents,
            Status = status,
            Errors = historyRecoveryErrors,
        };

        var mockHistoryRecoveryManager = new Mock<IHistoryRecoveryManager>();

        ICollection<string> errors = new List<string>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterHistoryRecoveryManager(ComponentId, mockHistoryRecoveryManager.Object);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(configurationProvider => configurationProvider.IsConfigurationValid(It.IsAny<object>(), out errors))
            .Returns(true);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.StartOnDemandHistoryRecovery(mockConfigurationProvider.Object, ComponentId, JsonSerializer.SerializeToElement(invalidHistoryRecoveryState, _jsonSerializerOptions));
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.True(objectResult.StatusCode.Equals(expectedInvalidResult.StatusCode));
    }

    [Fact]
    public void DataSelectionOperation_Test()
    {
        const string SelectOperationString = "Select";
        const string UnselectOperationString = "Unselect";
        const string UnsupportedOperationString = "Unsupported";
        var expectedResult = new MvcResult(200);
        var receivedDiscoveryId = string.Empty;
        var receivedSelectedFlag = false;
        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.MergeWithDataSelection(It.IsAny<string>(), It.IsAny<bool>()))
            .Callback((string discoveryId, bool selected) =>
            {
                receivedDiscoveryId = discoveryId;
                receivedSelectedFlag = selected;
            }).Returns(expectedResult);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.DataSelectionOperation(ComponentId, SelectOperationString, ConfigurationId);
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.Equal(ConfigurationId, receivedDiscoveryId);
        Assert.True(receivedSelectedFlag);

        receivedDiscoveryId = string.Empty;

        result = configurationController.DataSelectionOperation(ComponentId, UnselectOperationString, ConfigurationId);
        objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedResult.StatusCode, objectResult.StatusCode);
        Assert.Equal(ConfigurationId, receivedDiscoveryId);
        Assert.False(receivedSelectedFlag);

        result = configurationController.DataSelectionOperation(ComponentId, UnsupportedOperationString, ConfigurationId);
        objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public void PostConfiguration_PutConfiguration_InvalidInput()
    {
        const string InvalidInputString = "{\"Index\" : -1}";

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);

        var result = configurationController.PostConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(InvalidInputString, _jsonSerializerOptions));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);

        result = configurationController.PutConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(InvalidInputString, _jsonSerializerOptions));

        objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void PutConfiguration_Simple_Success()
    {
        object savedConfig = null;
        ICollection<string> errors = new List<string>();
        var postedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var protectSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration)))
            .Returns(postedConfiguration);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(postedConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.True(_callbackActionCalled);
        Assert.NotNull(savedConfig);
    }

    [Theory]
    [InlineData("discoveries")]
    [InlineData("historyrecoveries")]
    public void PutEdgeSystemConfigurations_FacetIgnored_Success(string facetName)
    {
        const string ReadOnlyFacetName = "ReadOnlyFacet";
        var localCallbackCalled = false;
        var readOnlyCallbackCalled = false;
        var readOnlyValidationCalled = false;
        object savedConfig = null;
        ICollection<string> errors = new List<string>();
        var configuration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];

        var configurationToPut = new Dictionary<string, Dictionary<string, JsonElement>>
        {
            {
                ComponentId, new Dictionary<string, JsonElement>
                {
                    { FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions) },
                    { FacetName2, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions) },
                    { ReadOnlyFacetName, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions) },
                    { facetName, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions) },
                }
            },
        };

        var protectSecretsCalled = false;
        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration)))
            .Returns(null);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1);
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1,
            commandGenerator, SampleCallbackAction, null);

        // register the other facet and use the same command generator (to get the same config)
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName2,
            commandGenerator, args => localCallbackCalled = true, null);

        // register the read-only facet, callback method should NOT be called as this facet should be ignored.
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, ReadOnlyFacetName,
            commandGenerator, args => readOnlyCallbackCalled = true, null, args =>
            {
                readOnlyValidationCalled = true;
                return new List<string> { "This should not be called!" };
            }, Operations.Get | Operations.Delete);

        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockComponentIdService = new Mock<IComponentIdService>();

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutEdgeSystemConfigurations(configurationToPut, mockServiceProvider.Object, mockComponentIdService.Object);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Exactly(2));
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.True(_callbackActionCalled);
        Assert.True(localCallbackCalled);
        Assert.False(readOnlyCallbackCalled);
        Assert.False(readOnlyValidationCalled);
        Assert.NotNull(savedConfig);
    }

    [Fact]
    public void PutEdgeSystemConfigurations_ComponentNotFound()
    {
        var configuration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var configurationToPut = new Dictionary<string, Dictionary<string, JsonElement>>
        {
            {
                ComponentId, new Dictionary<string, JsonElement>
                {
                    { FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions) },
                }
            },
        };

        var configurationRegistry = new RuntimeConfigurationRegistry();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockComponentIdService = new Mock<IComponentIdService>();

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.PutEdgeSystemConfigurations(configurationToPut, mockServiceProvider.Object, mockComponentIdService.Object);

        var noContentResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, noContentResult.StatusCode);
    }

    [Fact]
    public void PutEdgeSystemConfigurations_ComponentGetsAdded()
    {
        var saveConfigurationCounter = 0;
        ICollection<string> errors = new List<string>();

        var configuration1 = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var configuration2 = new[] { new EdgeComponentConfig { ComponentType = ComponentId, ComponentId = ComponentId } };
        var configurationToPut = new Dictionary<string, Dictionary<string, JsonElement>>
        {
            {
                ComponentId, new Dictionary<string, JsonElement>
                {
                    { FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration1), _jsonSerializerOptions) },
                }
            },
            {
                EdgeSystemConstants.SystemComponentId, new Dictionary<string, JsonElement>
                {
                    { EdgeSystemConstants.ComponentsFacetName, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration2), _jsonSerializerOptions) },
                }
            },
        };

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = null;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                errorMessages = new List<string>();
                saveConfigurationCounter++;
            })).Returns(true);

        var mockComponentIdService = new Mock<IComponentIdService>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1);

        var mockAdapterInstance = new Mock<IEdgeAdapter>();
        mockAdapterInstance.Setup(adapter => adapter.ComponentType).Returns(ComponentId);
        mockAdapterInstance.Setup(adapter => adapter.ComponentId).Returns(ComponentId);
        mockAdapterInstance.Setup(adapter => adapter.Unregister(It.IsAny<CancellationToken>())).Callback(() => configurationRegistry.UnregisterComponent(ComponentId));
        mockAdapterInstance.Setup(adapter => adapter.Register(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId,
                    FacetName1, commandGenerator, null, null);
            });

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(m => m.GetService(typeof(IEnumerable<IEdgeAdapter>))).Returns(new List<IEdgeAdapter> { mockAdapterInstance.Object });

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()));

        configurationRegistry.RegisterComponentConfiguration<EdgeComponentConfig[]>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName, commandGenerator,
            args =>
            {
                configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, commandGenerator, null, null);
            }, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);

        var result = configurationController.PutEdgeSystemConfigurations(configurationToPut, mockServiceProvider.Object, mockComponentIdService.Object);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.Equal(2, saveConfigurationCounter);
    }

    [Fact]
    public void PutEdgeSystemConfigurations_ComponentRegistered_ConfigInvalid()
    {
        var saveConfigurationCounter = 0;
        ICollection<string> errors = new List<string> { "Validation of the configuration failed." };
        var configuration1 = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var configuration2 = new[] { new EdgeComponentConfig { ComponentType = ComponentId, ComponentId = ComponentId } };
        var configurationToPut = new Dictionary<string, Dictionary<string, JsonElement>>
        {
            {
                ComponentId, new Dictionary<string, JsonElement>
                {
                    { FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration1), _jsonSerializerOptions) },
                }
            },
            {
                EdgeSystemConstants.SystemComponentId, new Dictionary<string, JsonElement>
                {
                    { EdgeSystemConstants.ComponentsFacetName, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration2), _jsonSerializerOptions) },
                }
            },
        };

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration))).Returns(null);
        mockConfigurationProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(false);
        mockConfigurationProvider.Setup(cp => cp.SaveConfiguration(ComponentId, FacetName1, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                saveConfigurationCounter++;
            });

        var mockComponentIdService = new Mock<IComponentIdService>();
        var configurationRegistry = new RuntimeConfigurationRegistry();
        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1);
        var mockAdapterInstance = new Mock<IEdgeAdapter>();
        mockAdapterInstance.Setup(adapter => adapter.ComponentType).Returns(ComponentId);
        mockAdapterInstance.Setup(adapter => adapter.ComponentId).Returns(ComponentId);
        mockAdapterInstance.Setup(adapter => adapter.Unregister(It.IsAny<CancellationToken>())).Callback(() => configurationRegistry.UnregisterComponent(ComponentId));
        mockAdapterInstance.Setup(adapter => adapter.Register(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId,
                    FacetName1, commandGenerator, null, null);
            });

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(m => m.GetService(typeof(IEnumerable<IEdgeAdapter>))).Returns(new List<IEdgeAdapter> { mockAdapterInstance.Object });

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()));

        configurationRegistry.RegisterComponentConfiguration<EdgeComponentConfig[]>(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.ComponentsFacetName, commandGenerator,
            args =>
            {
                configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, commandGenerator, null, null);
            }, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutEdgeSystemConfigurations(configurationToPut, mockServiceProvider.Object, mockComponentIdService.Object);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        testLogger.AreErrorsWarningsInLog();
        Assert.Equal(0, saveConfigurationCounter);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Never);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), false), Times.Once);

        // Only the System facet should remain in the registry
        Assert.Single(configurationRegistry.GetRegisteredComponentIds());
    }

    [Fact]
    public void PutEdgeSystemConfigurations_ConfigurationInvalid()
    {
        var saveConfigurationCounter = 0;
        var localCallbackCalled = false;
        object savedConfig = null;
        var protectSecretsCalled = false;
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockComponentIdService = new Mock<IComponentIdService>();
        ICollection<string> errors = new List<string>();

        var invalidConfiguration = new SampleIdConfiguration { Index = ConfigurationId, Secret = "Secret1", BoolProperty = false, IntProperty = 52, };
        var configurationToPut = new Dictionary<string, Dictionary<string, JsonElement>>()
        {
            {
                ComponentId, new Dictionary<string, JsonElement>
                {
                    { FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(invalidConfiguration), _jsonSerializerOptions) },
                    { FacetName2, JsonSerializer.Deserialize<JsonElement>(Serialize(invalidConfiguration), _jsonSerializerOptions) },
                }
            },
        };

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration))).Returns(null);
        mockConfigurationProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);
        mockConfigurationProvider.Setup(cp => cp.SaveConfiguration(ComponentId, FacetName1, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                savedConfig = config;
                saveConfigurationCounter++;
            });

        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1);
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, commandGenerator, SampleCallbackAction, null);

        // register the other facet and use the same command generator (to get the same config)
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName2, commandGenerator,
            args => localCallbackCalled = true, null, CustomValidationFunction);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutEdgeSystemConfigurations(configurationToPut, mockServiceProvider.Object, mockComponentIdService.Object);

        var noContentResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Never);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), false), Times.Exactly(3));
        Assert.Equal(StatusCodes.Status400BadRequest, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.False(_callbackActionCalled);
        Assert.False(localCallbackCalled);
        Assert.Null(savedConfig);
        Assert.Equal(0, saveConfigurationCounter);
    }

    [Fact]
    public void PutEdgeSystemConfiguration_ConfigurationInvalid_ConfigErrorMessages()
    {
        const string FacetName3 = "SomeFacet3";
        var saveConfigurationCounter = 0;
        var localCallbackCalled = false;
        object savedConfig = null;
        ICollection<string> errors = new List<string>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockComponentIdService = new Mock<IComponentIdService>();
        var protectSecretsCalled = false;
        var validConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var invalidConfiguration = new SampleIdConfiguration { Index = ConfigurationId, Secret = "Secret1", BoolProperty = false, IntProperty = 52, };
        var invalidConfiguration2 = new SampleIdConfiguration { Index = ConfigurationId, Secret = "Secret2", BoolProperty = true, IntProperty = 123, };

        var configurationToPut = new Dictionary<string, Dictionary<string, JsonElement>>
        {
            {
                ComponentId, new Dictionary<string, JsonElement>
                {
                    { FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(validConfiguration), _jsonSerializerOptions) },
                    { FacetName2, JsonSerializer.Deserialize<JsonElement>(Serialize(invalidConfiguration), _jsonSerializerOptions) },
                    { FacetName3, JsonSerializer.Deserialize<JsonElement>(Serialize(invalidConfiguration2), _jsonSerializerOptions) },
                }
            },
        };

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration))).Returns(null);
        mockConfigurationProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);
        mockConfigurationProvider.Setup(cp => cp.SaveConfiguration(ComponentId, FacetName1, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                savedConfig = config;
                saveConfigurationCounter++;
            });

        var commandGenerator = new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1);
        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, commandGenerator, SampleCallbackAction, null);

        // register the other facet and use the same command generator (to get the same config)
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName2,
            commandGenerator, args => localCallbackCalled = true, null, CustomValidationFunction);

        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName3,
            commandGenerator, args => localCallbackCalled = true, null, CustomValidationFunction);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutEdgeSystemConfigurations(configurationToPut, mockServiceProvider.Object, mockComponentIdService.Object);

        var contentResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Never);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), false), Times.Exactly(5));
        Assert.Equal(StatusCodes.Status400BadRequest, contentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.False(_callbackActionCalled);
        Assert.False(localCallbackCalled);
        Assert.Null(savedConfig);
        Assert.Equal(0, saveConfigurationCounter);

        var configurationError = (ConfigurationErrors)contentResult.Value;
        Assert.False(configurationError == null);
        Assert.Equal(2, configurationError.ErrorCount);
        Assert.StartsWith(
            $"Input validation failed for Component Id: {ComponentId} on Facet: {FacetName2}; IntProperty {SampleValidationMessageString}",
            configurationError.ConfigurationErrorResponses.First().Error,
            StringComparison.InvariantCultureIgnoreCase);
        Assert.StartsWith(
            $"Input validation failed for Component Id: {ComponentId} on Facet: {FacetName3}; IntProperty {SampleValidationMessageString}",
            configurationError.ConfigurationErrorResponses.Last().Error,
            StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void PostConfiguration_Simple_ConfigNotFound_Success()
    {
        object savedConfig = null;
        ICollection<string> errors = new List<string>();
        var configuration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var protectSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration)))
            .Returns(null);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PostConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PostConfiguration_Simple_ConfigFound_Fails()
    {
        ICollection<string> errors = new List<string>();
        var configuration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var protectSecretsCalled = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = configuration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out errors))
            .Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PostConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions));

        var objectResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Never);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), false), Times.Once);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.True(testLogger.AreErrorsWarningsInLog());
        Assert.True(protectSecretsCalled);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void PutConfiguration_Collection_Success()
    {
        ICollection<string> errors = new List<string>();
        var postedConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = postedConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(postedConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PostConfiguration_Collection_EntriesAdded_Success()
    {
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var configurationToPost = CreateConfigurationCollection<SampleIdConfiguration>(4, 2);
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1,
            new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PostConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configurationToPost), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.Equal(4, ((object[])savedConfig).Length);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PostConfiguration_PutConfiguration_Collection_SameIdsRefused()
    {
        ICollection<string> errors = new List<string>();
        var configuration = CreateConfigurationCollection<SampleIdConfiguration>(2);

        // add the same index twice
        configuration[1].Index = ConfigurationId;
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = configuration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out errors))
            .Callback(new MockTrySaveCallback(
                (string id, string facet, object config, out ICollection<string> err) =>
                {
                    err = new List<string>();
                    savedConfig = config;
                }))
             .Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PostConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions));

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.True(testLogger.AreErrorsWarningsInLog());
        Assert.True(protectSecretsCalled);
        Assert.Null(savedConfig);
        Assert.False(_callbackActionCalled);

        _callbackActionCalled = false;
        protectSecretsCalled = false;

        result = configurationController.PutConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions));

        objectResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Never);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), false), Times.AtLeastOnce);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.Null(savedConfig);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void PostConfiguration_PutConfiguration_Collection_NoIdAttributeDecoration()
    {
        ICollection<string> errors = new List<string>();
        var configuration = CreateConfigurationCollection<SampleConfiguration>(2);
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = configuration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PostConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        _callbackActionCalled = false;
        protectSecretsCalled = false;

        result = configurationController.PutConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configuration), _jsonSerializerOptions));

        noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Exactly(2));
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PutConfiguration_WithId_Collection()
    {
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var replacementConfiguration = new SampleIdConfiguration { Index = ConfigurationId, Secret = "SuperSecret", BoolProperty = true, IntProperty = 42 };
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutConfiguration(ComponentId, FacetName1, replacementConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(replacementConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Equal(2, convertedSavedConfig.Length);
    }

    [Fact]
    public void PutConfiguration_WithId_InsertIfNotPresent()
    {
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        var replacementConfiguration = new SampleIdConfiguration { Index = ConfigurationId, Secret = "SuperSecret", BoolProperty = true, IntProperty = 42 };
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutConfiguration(ComponentId, FacetName1, replacementConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(replacementConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Equal(2, convertedSavedConfig.Length);

        savedConfig = null;
        protectSecretsCalled = false;
        _callbackActionCalled = false;

        var newConfiguration = new SampleIdConfiguration { Index = "SomethingNew", Secret = "NoSecret", BoolProperty = true, IntProperty = 33 };
        result = configurationController.PutConfiguration(ComponentId, FacetName1, newConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(newConfiguration), _jsonSerializerOptions));

        noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Exactly(2));
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PutConfiguration_WithId_InsertIfNotPresent_EmptyFacet()
    {
        ICollection<string> errors = new List<string>();
        SampleIdConfiguration[] existingConfiguration = null;
        var replacementConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutConfiguration(ComponentId, FacetName1, replacementConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(replacementConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Single(convertedSavedConfig);
    }

    [Fact]
    public void PutConfiguration_WithId_FailIfIdsDoNotMatch()
    {
        ICollection<string> errors = new List<string>();
        const int ConfigurationEntryCount = 3;
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(ConfigurationEntryCount);
        var replacementConfiguration = new SampleIdConfiguration { Index = ConfigurationId, Secret = "SuperSecret", BoolProperty = true, IntProperty = 42 };
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        object outputConfigurationStructure = null;
        mockConfigurationProvider
            .Setup(cp => cp.TryGetConfiguration(ComponentId, FacetName1, It.IsAny<Type>(), out outputConfigurationStructure, out errors))
            .Callback(new MockTryGetCallback((string id, string facet, Type configType, out object configs, out ICollection<string> errs) =>
            {
                configs = existingConfiguration;
                errs = new List<string>();
            })).Returns(true);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PutConfiguration(ComponentId, FacetName1, replacementConfiguration.Index, JsonSerializer.Deserialize<JsonElement>(Serialize(replacementConfiguration), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);

        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Equal(ConfigurationEntryCount, convertedSavedConfig.Length);

        savedConfig = null;
        protectSecretsCalled = false;
        _callbackActionCalled = false;

        var newConfiguration = new SampleIdConfiguration { Index = "SomethingNew", Secret = "NoSecret", BoolProperty = true, IntProperty = 33 };
        result = configurationController.PutConfiguration(ComponentId, FacetName1, "NonMatchingId", JsonSerializer.Deserialize<JsonElement>(Serialize(newConfiguration), _jsonSerializerOptions));

        var objectResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), false), Times.Once);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.Null(savedConfig);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void PostConfiguration_NotFound_Collection_JObject_Success()
    {
        ICollection<string> errors = new List<string>();
        var configurationToPost = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var protectSecretsCalled = false;
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(configProtector => configProtector.ProtectSecrets(ref It.Ref<object>.IsAny, It.IsAny<Type>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => protectSecretsCalled = true);

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(null);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PostConfiguration(ComponentId, FacetName1, JsonSerializer.Deserialize<JsonElement>(Serialize(configurationToPost), _jsonSerializerOptions));

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(protectSecretsCalled);
        Assert.NotNull(savedConfig);
        Assert.True(_callbackActionCalled);
        var convertedSavedConfig = (SampleIdConfiguration[])savedConfig;
        Assert.Single(convertedSavedConfig);
    }

    [Fact]
    public void PatchConfiguration_Simple_Success()
    {
        const string PatchesConfig = "{\r\n  \"intProperTy\": 42\r\n, \"secret\": null\r\n}";
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var expectedConfiguration = new SampleConfiguration { Index = ConfigurationId, Secret = null, BoolProperty = false, IntProperty = 42 };
        object savedConfig = null;
        var protectSecretsCalled = false;

        using var patches = JsonDocument.Parse(PatchesConfig, _jsonDocumentOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(It.IsAny<string>())).Callback(() => { protectSecretsCalled = true; });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleConfiguration)))
            .Returns(existingConfiguration);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleConfiguration>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PatchConfiguration(ComponentId, FacetName1, patches.RootElement);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);
        var convertedConfig = (SampleConfiguration)savedConfig;

        // make sure the patched configuration is correct
        Assert.Equal(expectedConfiguration.IntProperty, convertedConfig.IntProperty);
        Assert.Equal(expectedConfiguration.Index, convertedConfig.Index);
        Assert.Equal(expectedConfiguration.Secret, convertedConfig.Secret);
        Assert.Equal(expectedConfiguration.BoolProperty, convertedConfig.BoolProperty);
        Assert.False(protectSecretsCalled);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PatchConfiguration_CustomConverter_Success()
    {
        const string PatchesConfig = "{\r\n  \"PropertyWithConverter\": \"0x42\"\r\n, \"secret\": null\r\n}";
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1)[0];
        var expectedConfiguration = new SampleConfiguration { Index = ConfigurationId, Secret = null, BoolProperty = false, PropertyWithConverter = 66 };
        object savedConfig = null;
        var protectSecretsCalled = false;

        using var patches = JsonDocument.Parse(PatchesConfig, _jsonDocumentOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(It.IsAny<string>())).Callback(() => { protectSecretsCalled = true; });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleConfiguration)))
            .Returns(existingConfiguration);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleConfiguration>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PatchConfiguration(ComponentId, FacetName1, patches.RootElement);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);
        var convertedConfig = (SampleConfiguration)savedConfig;

        // make sure the patched configuration is correct
        Assert.Equal(expectedConfiguration.IntProperty, convertedConfig.IntProperty);
        Assert.Equal(expectedConfiguration.Index, convertedConfig.Index);
        Assert.Equal(expectedConfiguration.Secret, convertedConfig.Secret);
        Assert.Equal(expectedConfiguration.BoolProperty, convertedConfig.BoolProperty);
        Assert.Equal(expectedConfiguration.PropertyWithConverter, convertedConfig.PropertyWithConverter);
        Assert.False(protectSecretsCalled);
        Assert.True(_callbackActionCalled);
    }

    [Fact]
    public void PatchConfiguration_Simple_InvalidDataType()
    {
        const string PatchesConfig = "{\r\n  \"intProperTy\": \"NotInteger\"\r\n}";
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1);
        object savedConfig = null;
        var protectSecretsCalled = false;

        using var patches = JsonDocument.Parse(PatchesConfig, _jsonDocumentOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(It.IsAny<string>())).Callback(() => { protectSecretsCalled = true; });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleConfiguration))).Returns(existingConfiguration);
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out errors))
            .Callback(new MockTrySaveCallback(
                (string id, string facet, object config, out ICollection<string> err) =>
                {
                    err = new List<string>();
                    savedConfig = config;
                })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleConfiguration>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PatchConfiguration(ComponentId, FacetName1, patches.RootElement);

        var objectResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Never);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), false), Times.Once);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.True(testLogger.AreErrorsWarningsInLog());
        Assert.Null(savedConfig);
        Assert.False(protectSecretsCalled);
        Assert.False(_callbackActionCalled);
    }

    [Fact]
    public void PatchConfiguration_Collection_Success()
    {
        const string PatchFacet = "{\r\n  \"intProperty\": 42,\r\n  \"secret\": \"Test\"\r\n}";
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(1);
        var expectedConfiguration = new[] { new SampleIdConfiguration { Index = ConfigurationId, Secret = "Test", BoolProperty = false, IntProperty = 42 } };
        var protectSecretsCalled = false;
        object savedConfig = null;

        using var patches = JsonDocument.Parse(PatchFacet, _jsonDocumentOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns("Test").Callback(() => { protectSecretsCalled = true; });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();

        mockConfigurationProvider
            .Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[])))
            .Returns(existingConfiguration);

        mockConfigurationProvider
            .Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PatchConfigurationEntry(ComponentId, FacetName1, existingConfiguration[0].Index, patches.RootElement);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);
        var convertedConfig = (SampleIdConfiguration[])savedConfig;

        // make sure the patched configuration is correct
        Assert.Equal(expectedConfiguration[0].IntProperty, convertedConfig[0].IntProperty);
        Assert.Equal(expectedConfiguration[0].Index, convertedConfig[0].Index);
        Assert.Equal(expectedConfiguration[0].Secret, convertedConfig[0].Secret);
        Assert.Equal(expectedConfiguration[0].BoolProperty, convertedConfig[0].BoolProperty);
        Assert.True(_callbackActionCalled);
        Assert.True(protectSecretsCalled);
        Assert.Equal(Serialize(existingConfiguration), Serialize(_configurationChangedArgs.OldValue));
        Assert.Equal(savedConfig, _configurationChangedArgs.NewValue);

        protectSecretsCalled = false;
        _callbackActionCalled = false;
        savedConfig = null;

        result = configurationController.PatchConfigurationEntry(ComponentId, FacetName1, "NonExistent", patches.RootElement);

        var objectResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Once);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.True(testLogger.AreErrorsWarningsInLog());
        Assert.Null(savedConfig);
        Assert.False(_callbackActionCalled);
        Assert.False(protectSecretsCalled);
    }

    [Fact]
    public void PatchConfiguration_Collection_IdCannotBeChanged()
    {
        const string PatchesFacet = "{\r\n  \"Index\": \"ChangedIndex\",\r\n  \"secret\": \"Test\"\r\n}";
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(4);
        var protectSecretsCalled = false;
        object savedConfig = null;

        using var patches = JsonDocument.Parse(PatchesFacet, _jsonDocumentOptions);

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        mockConfigurationProtector.Setup(protector => protector.SecretsManager.Protect(It.IsAny<string>())).Returns("Test").Callback(() => { protectSecretsCalled = true; });

        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(existingConfiguration);
        mockConfigurationProvider.Setup(cp => cp.SaveConfiguration(ComponentId, FacetName1, It.IsAny<object>()))
            .Callback((string id, string facet, object config) =>
            {
                savedConfig = config;
            });

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.PatchConfigurationEntry(ComponentId, FacetName1, existingConfiguration[0].Index, patches.RootElement);

        var objectResult = Assert.IsType<ObjectResult>(result);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), true), Times.Never);
        mockConfigurationProtector.Verify(x => x.ReconcileSecretsChange(ref It.Ref<object[]>.IsAny, It.IsAny<Type>(), false), Times.Once);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.True(testLogger.AreErrorsWarningsInLog());
        Assert.Null(savedConfig);
        Assert.False(protectSecretsCalled);
        Assert.Contains("is ID and cannot be changed.", ((RestApiErrorResponse)objectResult.Value).Error, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void DeleteDiscoveryState_Test()
    {
        var receivedDiscoveryId = string.Empty;
        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.DeleteDiscovery(It.IsAny<string>()))
            .Callback((string discoveryId) =>
            {
                receivedDiscoveryId = discoveryId;
            }).Returns(new MvcResult(200));

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.DeleteDiscovery(ComponentId, ConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(ConfigurationId, receivedDiscoveryId);

        result = configurationController.DeleteDiscovery(NonExistentComponentId, ConfigurationId);
        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCodeResult.StatusCode);
    }

    [Fact]
    public void DeleteDiscoveryStates_Test()
    {
        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.DeleteDiscoveries()).Returns(new MvcResult(200));

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.DeleteDiscoveries(ComponentId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);

        result = configurationController.DeleteDiscovery(NonExistentComponentId, ConfigurationId);
        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCodeResult.StatusCode);
    }

    [Fact]
    public void DeleteDiscoveryResult_Test()
    {
        var receivedDiscoveryId = string.Empty;
        var mockDiscoveryManager = new Mock<IDataSourceDiscoveryManager>();
        mockDiscoveryManager.Setup(discoveryManager => discoveryManager.DeleteDiscoveryResult(It.IsAny<string>()))
            .Callback((string discoveryId) =>
            {
                receivedDiscoveryId = discoveryId;
            }).Returns(new MvcResult(200));

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterDataSourceDiscoveryManager(ComponentId, mockDiscoveryManager.Object);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, null);
        var result = configurationController.DeleteDiscoveryResult(ComponentId, ConfigurationId);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        Assert.Equal(ConfigurationId, receivedDiscoveryId);

        result = configurationController.DeleteDiscoveryResult(NonExistentComponentId, ConfigurationId);
        var statusCodeResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCodeResult.StatusCode);
    }

    [Fact]
    public void DeleteConfiguration_EntriesFrom_Collection_Success()
    {
        ICollection<string> errors = new List<string>();
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(2);
        object savedConfig = null;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.IsConfigurationValid(It.IsAny<object>(), out errors)).Returns(true);
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(existingConfiguration);

        ICollection<string> trySaveErrors = null;
        mockConfigurationProvider.Setup(cp => cp.TrySaveConfiguration(ComponentId, FacetName1, It.IsAny<object>(), out trySaveErrors))
            .Callback(new TrySaveConfigurationCallback((string id, string facet, object config, out ICollection<string> errorMessages) =>
            {
                savedConfig = config;
                errorMessages = new List<string>();
            })).Returns(true);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.DeleteConfiguration(ComponentId, FacetName1, existingConfiguration[1].Index);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);

        var savedConfigCollection = (SampleIdConfiguration[])savedConfig;

        Assert.Single(savedConfigCollection);
        Assert.Equal(existingConfiguration[0].Index, savedConfigCollection[0].Index);
        Assert.Equal(existingConfiguration[0].BoolProperty, savedConfigCollection[0].BoolProperty);
        Assert.Equal(existingConfiguration[0].IntProperty, savedConfigCollection[0].IntProperty);
        Assert.Equal(existingConfiguration[0].Secret, savedConfigCollection[0].Secret);
        Assert.True(_callbackActionCalled);

        var notifyConfiguration = (SampleIdConfiguration[])_configurationChangedArgs.NewValue;
        Assert.Equal(savedConfig, notifyConfiguration);

        var oldConfig = (SampleIdConfiguration[])_configurationChangedArgs.OldValue;
        Assert.Equal(existingConfiguration, oldConfig);

        _configurationChangedArgs = null;

        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(savedConfigCollection);

        result = configurationController.DeleteConfiguration(ComponentId, FacetName1, savedConfigCollection[0].Index);

        noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(savedConfig);
        Assert.NotNull(_configurationChangedArgs);

        var newEmptyConfiguration = (SampleIdConfiguration[])_configurationChangedArgs.NewValue;
        Assert.Empty(newEmptyConfiguration);
    }

    [Fact]
    public void DeleteConfiguration_Success()
    {
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(4);
        var configurationDeleted = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.DeleteConfiguration(ComponentId, FacetName1)).Callback((string id, string facet) => { configurationDeleted = true; });
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(existingConfiguration);

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.DeleteConfiguration(ComponentId, FacetName1);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.NotNull(_configurationChangedArgs);

        var oldConfiguration = (SampleIdConfiguration[])_configurationChangedArgs.OldValue;

        Assert.Equal(existingConfiguration, oldConfiguration);
        Assert.Null(_configurationChangedArgs.NewValue);
        Assert.True(_callbackActionCalled);
        Assert.True(configurationDeleted);
    }

    [Fact]
    public void DeleteConfiguration_OperationNotDefined()
    {
        var existingConfiguration = CreateConfigurationCollection<SampleIdConfiguration>(4);
        var configurationDeleted = false;

        var mockConfigurationProtector = new Mock<IConfigurationProtector>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(cp => cp.GetConfiguration(ComponentId, FacetName1, typeof(SampleIdConfiguration[]))).Returns(existingConfiguration);
        mockConfigurationProvider.Setup(cp => cp.DeleteConfiguration(ComponentId, FacetName1)).Callback((string id, string facet) => { configurationDeleted = true; });

        var configurationRegistry = new RuntimeConfigurationRegistry();
        configurationRegistry.RegisterComponentConfiguration<SampleIdConfiguration[]>(ComponentId, FacetName1, new ConfigurationCommandGenerator(mockConfigurationProvider.Object, ComponentId, FacetName1),
            SampleCallbackAction, null, null, Operations.Create | Operations.Update);

        var testLogger = new TestLogger();
        using var configurationController = new SystemConfigurationController(testLogger, configurationRegistry, mockConfigurationProtector.Object);
        var result = configurationController.DeleteConfiguration(ComponentId, FacetName1);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        Assert.False(configurationDeleted);
    }

    private static T[] CreateConfigurationCollection<T>(int count, int skip = 0) where T : ISampleConfiguration, new()
    {
        if (count - skip < 1)
        {
            throw new InvalidOperationException();
        }

        var collection = new T[count - skip];
        for (int i = 0 + skip; i < count; i++)
        {
            collection[i - skip] = new T { Index = $"Index{i}", Secret = $"Secret{i}", IntProperty = i };
        }

        return collection;
    }

    private static string Serialize<T>(T instance) => JsonSerializer.Serialize(instance, typeof(T), _jsonSerializerOptions);

    private static ICollection<string> CustomValidationFunction(ConfigurationChangedEventArgs configurationChangedEvent)
    {
        var errors = new List<string>();

        if (configurationChangedEvent.NewValue is SampleIdConfiguration sampleConfiguration)
        {
            if (sampleConfiguration.IntProperty > 50)
            {
                errors.Add($"{nameof(sampleConfiguration.IntProperty)} {SampleValidationMessageString}");
            }
        }

        return errors;
    }

    private void SampleCallbackAction(ConfigurationChangedEventArgs configurationChangeEvent)
    {
        _callbackActionCalled = true;
        _configurationChangedArgs = configurationChangeEvent;
    }

    internal class SampleConfiguration : EdgeConfigurationBase, ISampleConfiguration
    {
        private const int MaxValue = 42;

        public string Index { get; set; }

        public int IntProperty { get; set; }

        public bool BoolProperty { get; set; }

        [JsonConverter(typeof(DecOrHexJsonConverter))]
        public ushort PropertyWithConverter { get; set; }

        public string Secret { get; set; }

        public override IEnumerable<ValidationResult> Validate()
        {
            if (IntProperty > MaxValue)
            {
                yield return new ValidationResult($"{nameof(IntProperty)} value cannot be more than {MaxValue}!");
            }
        }
    }

    internal class SampleIdConfiguration : EdgeConfigurationBase, ISampleConfiguration
    {
        private const int MaxValue = 42;

        [Id]
        public string Index { get; set; }

        public int IntProperty { get; set; }

        public bool BoolProperty { get; set; }

        [Protected]
        public string Secret { get; set; }

        public override IEnumerable<ValidationResult> Validate()
        {
            if (IntProperty > MaxValue)
            {
                yield return new ValidationResult($"{nameof(IntProperty)} value cannot be more than {MaxValue}!");
            }
        }
    }

    internal class DecOrHexJsonConverter : JsonConverter<ushort>
    {
        public override ushort Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                string stringValue = reader.GetString().Trim();

                if (stringValue.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase) && stringValue.Length > 2)
                {
                    if (ushort.TryParse(stringValue.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort hexValue))
                    {
                        return hexValue;
                    }
                }
                else if (ushort.TryParse(stringValue, out ushort decValue))
                {
                    return decValue;
                }
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetUInt16();
            }

            throw new JsonException();
        }

        public override void Write(Utf8JsonWriter writer, ushort value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteNumberValue(value);
        }
    }
}
