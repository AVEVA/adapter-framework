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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using AdapterFramework.Data.Framework.EndpointManager;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Host.Helpers;

namespace AdapterFramework.Data.Framework.Host.Configuration;

// The route below translates to "api/v1/Configuration"
[Route(RouteConstants.BaseConfigurationRoute)]
public class SystemConfigurationController : ControllerHelper
{
    #region Private Constants

    private const string EmptyObjectString = "{}";
    private const string EmptyArrayString = "[]";
    private const string DataSelectionSelectString = "select";
    private const string DataSelectionUnSelectString = "unselect";
    private const string DataSelectionFacetName = "DataSelection";
    private const string DiscoveriesFacetName = "Discoveries";
    private const string HistoryRecoveriesFacetName = "HistoryRecoveries";
    private const string InvalidInputTypeErrorString = "Invalid input. Configuration value for component ID: '{0}' and facet: '{1}' is of unsupported type. {2}";
    private const string UnableToSerializeConfig = "Unable to read configuration for component ID {0} and facet {1}: {2}";

    #endregion

    #region Private Fields

    private static readonly JsonSerializerOptions _jsonSerializerOptions = ConfigurationCommandHelper.SerializerOptionsWithCustomEnumConverter;

    private readonly ILogger _systemLogger;
    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;

    #endregion

    #region Public Constructor

    public SystemConfigurationController(ILogger systemLogger, IRuntimeConfigurationRegistry runtimeConfigurationRegistry, IConfigurationProtector configurationProtector)
        : base(configurationProtector, systemLogger, runtimeConfigurationRegistry)
    {
        _systemLogger = systemLogger;
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
    }

    #endregion

    #region HTTP GET

    [HttpGet]
    public IActionResult GetAllConfigurations()
    {
        var returnReadOnlyFacets = IsVerboseResponseRequested();
        var registeredComponentIds = _runtimeConfigurationRegistry.GetRegisteredComponentIds();

        var configurations = new Dictionary<string, Dictionary<string, object>>();

        foreach (var componentId in registeredComponentIds)
        {
            if (_runtimeConfigurationRegistry.TryGetAvailableFacets(componentId, out var facets))
            {
                if (!TryGetComponentConfigurations(facets, componentId, returnReadOnlyFacets, out var componentConfigurations, out var result))
                {
                    return result;
                }

                configurations.Add(componentId, componentConfigurations);
            }
        }

        return Ok(configurations);
    }

    [HttpGet("{componentId}")]
    public IActionResult GetComponentConfigurations([FromRoute] string componentId)
    {
        ThrowHelper.ThrowIfArgumentNull(componentId, nameof(componentId));

        var returnReadOnlyFacets = IsVerboseResponseRequested();
        if (_runtimeConfigurationRegistry.TryGetAvailableFacets(componentId.ToUpperInvariant(), out var facets))
        {
            if (!TryGetComponentConfigurations(facets, componentId, returnReadOnlyFacets, out var componentConfigurations, out var result))
            {
                return result;
            }

            return Ok(componentConfigurations);
        }

        return SetHttpResponse(StatusCodes.Status404NotFound, string.Format(CultureInfo.InvariantCulture, ComponentNotFoundString, componentId));
    }

    [HttpGet("{componentId}/{facet}")]
    public IActionResult GetConfiguration([FromRoute] string componentId, [FromRoute] string facet, string diff, int skip, int count)
    {
        if (!string.IsNullOrEmpty(diff) && facet != null && facet.Equals(DataSelectionFacetName, StringComparison.OrdinalIgnoreCase))
        {
            if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryManager))
            {
                var result = discoveryManager.GetDataSelectionDifference(diff);

                return StatusCode(result.StatusCode, result.Content);
            }

            return StatusCode(StatusCodes.Status404NotFound);
        }

        return GetFacetConfiguration(componentId, facet, null, skip, count);
    }

    [HttpGet("{componentId}/discoveries")]
    public IActionResult GetDiscoveryStates([FromRoute] string componentId)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryManager))
        {
            var result = discoveryManager.GetDiscoveryStates();

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpGet("{componentId}/historyRecoveries")]
    public IActionResult GetHistoryRecoveryStates([FromRoute] string componentId)
    {
        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            var result = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.GetHistoryRecoveryStates();

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpGet("{componentId}/{facet}/{id}")]
    public IActionResult GetConfigurationById([FromRoute] string componentId, [FromRoute] string facet, [FromRoute] string id)
    {
        return GetFacetConfiguration(componentId, facet, id);
    }

    [HttpGet("{componentId}/discoveries/{id}")]
    public IActionResult GetDiscoveryStateById([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryManager))
        {
            var result = discoveryManager.GetDiscoveryState(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpGet("{componentId}/discoveries/{id}/result")]
    public IActionResult GetDiscoveryResultById([FromRoute] string componentId, [FromRoute] string id, string diff, int count, int skip)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryResultsManager))
        {
            var result = !string.IsNullOrEmpty(diff)
                ? discoveryResultsManager.GetDiscoveriesDifference(id, diff)
                : discoveryResultsManager.GetDiscoveryResult(id, new DiscoveryOptions(count, skip));

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpGet("{componentId}/historyRecoveries/{id}")]
    public IActionResult GetHistoryRecoveryStateById([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            var result = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.GetHistoryRecoveryState(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    #endregion

    #region HTTP POST

    [HttpPost("{componentId}/{facet}")]
    public IActionResult PostConfiguration([FromRoute] string componentId, [FromRoute] string facet, [FromBody] JsonElement configObject)
    {
        return PostFacetConfiguration(componentId, facet, configObject);
    }

    [HttpPost("{componentId}/discoveries")]
    public IActionResult StartDiscovery([FromServices] IConfigurationProvider configurationProvider, [FromRoute] string componentId, [FromBody] JsonElement configObject, string scheduleId)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));

        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryManager))
        {
            if (TryGetTypedValue<DiscoveryState>(componentId, DiscoveriesFacetName, configObject, configurationProvider, out var typedValue, out var actionResult))
            {
                var result = discoveryManager.StartDiscovery(typedValue, new DiscoveryOptions(scheduleId));

                return StatusCode(result.StatusCode, result.Content);
            }

            return actionResult;
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpPost("{componentId}/discoveries/{id}/cancel")]
    public IActionResult CancelDiscovery([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryManager))
        {
            var result = discoveryManager.CancelDiscovery(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpPost("{componentId}/historyRecoveries")]
    public IActionResult StartOnDemandHistoryRecovery([FromServices] IConfigurationProvider configurationProvider, [FromRoute] string componentId, [FromBody] JsonElement configObject)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));

        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            if (TryGetTypedValue<HistoryRecoveryState>(componentId, HistoryRecoveriesFacetName, configObject, configurationProvider, out var typedValue, out var actionResult))
            {
                var result = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.StartOnDemandHistoryRecovery(typedValue);

                return StatusCode(result.StatusCode, result.Content);
            }

            return actionResult;
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpPost("{componentId}/historyRecoveries/{id}/cancel")]
    public IActionResult CancelHistoryRecovery([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            var result = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.CancelHistoryRecovery(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpPost("{componentId}/historyRecoveries/{id}/resume")]
    public IActionResult ResumeHistoryRecovery([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            var result = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.ResumeHistoryRecovery(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpPost("{componentId}/dataSelection/{operation}")]
    public IActionResult DataSelectionOperation([FromRoute] string componentId, [FromRoute] string operation, string discoveryId)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryManager))
        {
            if (string.IsNullOrEmpty(operation))
            {
                return StatusCode(StatusCodes.Status404NotFound, "Operation must be specified.");
            }

            MvcResult result;
            if (operation.Equals(DataSelectionSelectString, StringComparison.OrdinalIgnoreCase))
            {
                result = discoveryManager.MergeWithDataSelection(discoveryId, true);
            }
            else if (operation.Equals(DataSelectionUnSelectString, StringComparison.OrdinalIgnoreCase))
            {
                result = discoveryManager.MergeWithDataSelection(discoveryId, false);
            }
            else
            {
                return SetHttpResponse(StatusCodes.Status404NotFound, $"Operation '{operation}' is not supported.");
            }

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    #endregion

    #region HTTP PUT

    [HttpPut]
    public IActionResult PutEdgeSystemConfigurations([FromBody] Dictionary<string, Dictionary<string, JsonElement>> edgeSystemConfigurations,
        [FromServices] IServiceProvider serviceProvider,
        [FromServices] IComponentIdService componentIdService)
    {
        ThrowHelper.ThrowIfArgumentNull(componentIdService, nameof(componentIdService));

        if (ModelState.IsValid)
        {
            ThrowHelper.ThrowIfArgumentNull(edgeSystemConfigurations, nameof(edgeSystemConfigurations));

            var actionResults = new List<IActionResult>();
            var validatedCommandTuples = new List<(IConfigurationSetCommand SetCommand, Action<ConfigurationChangedEventArgs> CallbackAction)>();
            var componentsToRegister = new Dictionary<string, IEdgeAdapter>();
            foreach (var componentId in edgeSystemConfigurations.Keys)
            {
                if (IsComponentRegistered(componentId))
                {
                    continue;
                }

                if (TryGetAdapterToRegister(componentId, edgeSystemConfigurations, serviceProvider, out var adapterComponent, out var actionResult))
                {
                    componentsToRegister.Add(componentId, adapterComponent);
                }
                else
                {
                    actionResults.Add(actionResult);
                }
            }

            foreach (var (componentId, component) in componentsToRegister)
            {
                componentIdService.AddEdgeComponentId(component.ComponentType, componentId);
                component.Register(CancellationToken.None);
            }

            var readOnlyFacetDictionary = new Dictionary<string, IEnumerable<string>>();
            foreach (var (componentId, facetsConfigurations) in edgeSystemConfigurations)
            {
                var readOnlyFacets = new List<string>();
                foreach (var (facet, configuration) in facetsConfigurations)
                {
                    if (IsReadonlyFacet(componentId, facet))
                    {
                        readOnlyFacets.Add(facet);
                        continue;
                    }

                    var isValid = ValidatePutConfigurationToken(componentId, facet, configuration, out var actionResult, out var setCommand, out var callbackAction);
                    if (!isValid)
                    {
                        actionResults.Add(actionResult);
                    }

                    if (!componentsToRegister.ContainsKey(componentId) && setCommand != null)
                    {
                        validatedCommandTuples.Add((setCommand, callbackAction));
                    }
                }

                if (readOnlyFacets.Count > 0)
                {
                    readOnlyFacetDictionary[componentId] = readOnlyFacets;
                }
            }

            RemoveComponents(componentsToRegister.Values);

            if (actionResults.Count > 0)
            {
                foreach (var (setCommand, _) in validatedCommandTuples)
                {
                    setCommand.RollbackChangesToSecrets();
                }

                var responses = new List<RestApiErrorResponse>();

                foreach (var actionResult in actionResults)
                {
                    responses.Add((RestApiErrorResponse)(actionResult as ObjectResult)?.Value);
                }

                var configErrors = new ConfigurationErrors(responses);

                return new ObjectResult(configErrors)
                {
                    StatusCode = (int)HttpStatusCode.BadRequest,
                };
            }

            foreach (var (setCommand, callbackAction) in validatedCommandTuples)
            {
                ConfigurationProcessor.TryExecuteCommand(setCommand, callbackAction, _systemLogger, out ICollection<string> _);
            }

            foreach (var (componentId, _) in componentsToRegister)
            {
                if (edgeSystemConfigurations.TryGetValue(componentId, out var facetsConfigurations))
                {
                    foreach (var (facet, configuration) in facetsConfigurations)
                    {
                        PutFacetConfiguration(componentId, facet, configuration);
                    }
                }
            }

            LogIgnoredReadOnlyFacets(readOnlyFacetDictionary);

            return NoContent();
        }

        LogEntryFromModelStateError(Request.Path.Value, ModelState);
        return new BadRequestObjectResult(ModelState);
    }

    [HttpPut("{componentId}/{facet}")]
    public IActionResult PutConfiguration([FromRoute] string componentId, [FromRoute] string facet, [FromBody] JsonElement configObject)
    {
        return PutFacetConfiguration(componentId, facet, configObject);
    }

    [HttpPut("{componentId}/{facet}/{id}")]
    public IActionResult PutConfiguration([FromRoute] string componentId, [FromRoute] string facet, [FromRoute] string id, [FromBody] JsonElement configObject)
    {
        return PutFacetConfiguration(componentId, facet, configObject, id);
    }

    #endregion

    #region HTTP PATCH

    [HttpPatch("{componentId}/{facet}")]
    public IActionResult PatchConfiguration([FromRoute] string componentId, [FromRoute] string facet, [FromBody] JsonElement configurationObject)
    {
        return PatchFacetConfiguration(componentId, facet, configurationObject);
    }

    [HttpPatch("{componentId}/{facet}/{id}")]
    public IActionResult PatchConfigurationEntry([FromRoute] string componentId, [FromRoute] string facet, [FromRoute] string id, [FromBody] JsonElement configurationObject)
    {
        return PatchFacetConfiguration(componentId, facet, configurationObject, id);
    }

    #endregion

    #region HTTP DELETE

    [HttpDelete("{componentId}/{facet}")]
    public IActionResult DeleteConfiguration([FromRoute] string componentId, [FromRoute] string facet)
    {
        return DeleteFacetConfiguration(componentId, facet);
    }

    [HttpDelete("{componentId}/discoveries")]
    public IActionResult DeleteDiscoveries([FromRoute] string componentId)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryResultsManager))
        {
            var result = discoveryResultsManager.DeleteDiscoveries();

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpDelete("{componentId}/discoveries/{id}")]
    public IActionResult DeleteDiscovery([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryResultsManager))
        {
            var result = discoveryResultsManager.DeleteDiscovery(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpDelete("{componentId}/historyRecoveries")]
    public IActionResult DeleteHistoryRecoveries([FromRoute] string componentId)
    {
        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            var result = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.DeleteHistoryRecoveries();

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpDelete("{componentId}/historyRecoveries/{id}")]
    public IActionResult DeleteHistoryRecovery([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            var result = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.DeleteHistoryRecovery(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    [HttpDelete("{componentId}/{facet}/{id}")]
    public IActionResult DeleteConfiguration([FromRoute] string componentId, [FromRoute] string facet, [FromRoute] string id)
    {
        return DeleteFacetConfiguration(componentId, facet, id);
    }

    [HttpDelete("{componentId}/discoveries/{id}/result")]
    public IActionResult DeleteDiscoveryResult([FromRoute] string componentId, [FromRoute] string id)
    {
        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryResultsManager))
        {
            var result = discoveryResultsManager.DeleteDiscoveryResult(id);

            return StatusCode(result.StatusCode, result.Content);
        }

        return StatusCode(StatusCodes.Status404NotFound);
    }

    #endregion

    #region Private Methods

    private static void RemoveComponents(IEnumerable<IEdgeAdapter> componentsToRemove)
    {
        foreach (var component in componentsToRemove)
        {
            component.Unregister(CancellationToken.None);
            component.Dispose();
        }
    }

    private static object GetEmptyConfiguration(Type configurationType)
    {
        return configurationType.IsArray ? JsonSerializer.Deserialize<JsonElement>(EmptyArrayString) : JsonSerializer.Deserialize<JsonElement>(EmptyObjectString);
    }

    private static bool TryGetSystemComponentsFacet(IReadOnlyDictionary<string, Dictionary<string, JsonElement>> edgeSystemConfigurations, out JsonElement componentsConfiguration)
    {
        componentsConfiguration = new JsonElement();

        foreach (var (componentId, facetsDictionary) in edgeSystemConfigurations)
        {
            if (!componentId.Equals(EdgeSystemConstants.SystemComponentId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (facetName, facetConfiguration) in facetsDictionary)
            {
                if (!facetName.Equals(EdgeSystemConstants.ComponentsFacetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                componentsConfiguration = facetConfiguration;
                return true;
            }
        }

        return false;
    }

    private bool IsVerboseResponseRequested()
    {
        if (!Request.Headers.TryGetValue(EndpointManagerConstants.AcceptVerbosityKey, out var acceptVerbosityHeader))
        {
            return false;
        }

        return EndpointManagerConstants.AcceptVerbosityValue.Equals(acceptVerbosityHeader, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetComponentConfigurations(IEnumerable<string> facets, string componentId, bool returnReadOnlyFacets, out Dictionary<string, object> componentConfigurations, out IActionResult result)
    {
        componentConfigurations = new Dictionary<string, object>();
        result = null;

        foreach (var facet in facets)
        {
            if (IsReadonlyFacet(componentId, facet) && !returnReadOnlyFacets)
            {
                continue;
            }

            if (TryGenerateGetCommand(componentId, facet, null, out _, out var getCommand, out var commandGeneratorTuple))
            {
                ConfigurationProcessor.TryExecuteCommand(getCommand, out var facetConfiguration, out var errors);

                if (errors.Count != 0)
                {
                    result = SetHttpResponse(StatusCodes.Status500InternalServerError, string.Format(CultureInfo.InvariantCulture, UnableToSerializeConfig, componentId, facet, string.Join(Environment.NewLine, errors)));
                    return false;
                }

                facetConfiguration = facetConfiguration != null ? MaskSecretsInConfiguration(commandGeneratorTuple, facetConfiguration) : GetEmptyConfiguration(commandGeneratorTuple.ConfigurationType);
                componentConfigurations.Add(facet, facetConfiguration);
            }
        }

        if (!returnReadOnlyFacets)
        {
            return true;
        }

        if (_runtimeConfigurationRegistry.TryGetDataSourceDiscoveryManager(componentId, out var discoveryManager))
        {
            var discoveryStatesResult = discoveryManager.GetDiscoveryStates();
            if (discoveryStatesResult.StatusCode == (int)HttpStatusCode.OK)
            {
                componentConfigurations.Add(DiscoveriesFacetName, discoveryStatesResult.Content);
            }
        }

        if (_runtimeConfigurationRegistry.TryGetHistoryRecoveryManager(componentId, out var historyRecoveryManager))
        {
            var historyRecoveryStatesResult = historyRecoveryManager.OnDemandHistoryRecoveryProcessor.GetHistoryRecoveryStates();
            if (historyRecoveryStatesResult.StatusCode == (int)HttpStatusCode.OK)
            {
                componentConfigurations.Add(HistoryRecoveriesFacetName, historyRecoveryStatesResult.Content);
            }
        }

        return true;
    }

    private bool ValidatePutConfigurationToken(string componentId, string facet, JsonElement configurationObject, out IActionResult actionResult, out IConfigurationSetCommand setCommand, out Action<ConfigurationChangedEventArgs> callbackAction)
    {
        callbackAction = null;

        if (TryGenerateSetCommand(componentId, facet, null, configurationObject, out actionResult, out setCommand, out var commandGeneratorTuple))
        {
            var isValid = setCommand.TryValidate(out var errors);
            if (isValid == false)
            {
                var errorsWithComponentIdFacet = new List<string> { $"Input validation failed for Component Id: {componentId} on Facet: {facet}" };
                errorsWithComponentIdFacet.AddRange(errors);
                actionResult = SetHttpResponse(StatusCodes.Status400BadRequest, errorsWithComponentIdFacet);
                return false;
            }

            callbackAction = commandGeneratorTuple.CallbackAction;
            return true;
        }

        return false;
    }

    private bool TryGetAdapterToRegister(string componentId, IReadOnlyDictionary<string, Dictionary<string, JsonElement>> edgeSystemConfigurations,
        IServiceProvider serviceProvider, out IEdgeAdapter adapterToRegister, out IActionResult result)
    {
        adapterToRegister = null;

        if (TryGetSystemComponentsFacet(edgeSystemConfigurations, out var componentsConfiguration))
        {
            if (!TryGetAdapterInstance(componentId, serviceProvider, componentsConfiguration, out adapterToRegister, out result))
            {
                return false;
            }
        }
        else
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest,
                $"Configuration payload contains configuration for a component with ID: '{componentId}' " +
                "that is not loaded to the system and the payload doesn't contain updated System Components configuration.");

            return false;
        }

        return true;
    }

    private bool TryGetAdapterInstance(string componentId, IServiceProvider serviceProvider, JsonElement componentsConfiguration, out IEdgeAdapter adapterToRegister, out IActionResult result)
    {
        adapterToRegister = null;
        result = null;

        try
        {
            var edgeComponentsConfiguration = JsonSerializer.Deserialize<EdgeComponentConfig[]>(componentsConfiguration.GetRawText(), _jsonSerializerOptions);

            var adapterType = string.Empty;
            var componentLoaded = false;
            var componentFound = false;
            foreach (var component in edgeComponentsConfiguration)
            {
                if (component.ComponentId.Equals(componentId, StringComparison.OrdinalIgnoreCase))
                {
                    componentFound = true;
                    var adapters = serviceProvider.GetServices<IEdgeAdapter>();
                    adapterType = component.ComponentType;

                    foreach (var adapter in adapters)
                    {
                        if (adapter.ComponentType.Equals(component.ComponentType, StringComparison.InvariantCultureIgnoreCase))
                        {
                            adapterToRegister = adapter;
                            componentLoaded = true;
                        }
                        else
                        {
                            adapter.Dispose();
                        }
                    }
                }
            }

            if (!componentFound)
            {
                result = SetHttpResponse(StatusCodes.Status400BadRequest,
                    $"Unable to find adapter component with ID '{componentId}' specified in System Components configuration. Please check your configuration payload.");

                return false;
            }

            if (!componentLoaded)
            {
                result = SetHttpResponse(StatusCodes.Status400BadRequest,
                    $"Failed to load adapter '{adapterType}'. Please check if the specified adapter type is correct.");

                return false;
            }
        }
        catch (Exception ex)
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest,
                $"Failed to update components registrations. {ex.GetExceptionTypeAndMessages()}");

            return false;
        }

        return true;
    }

    private bool TryGetTypedValue<T>(string componentId, string facet, JsonElement configObject, IConfigurationProvider configurationProvider, out T value, out IActionResult actionResult)
        where T : class
    {
        actionResult = null;
        value = null;

        if (configObject.ValueKind == JsonValueKind.Undefined)
        {
            configObject = JsonSerializer.Deserialize<JsonElement>(EmptyObjectString);
        }
        else if (configObject.ValueKind == JsonValueKind.Array)
        {
            var error = string.Format(CultureInfo.InvariantCulture, InvalidInputTypeErrorString, componentId, facet, "Input cannot be of array type.");
            _systemLogger.LogError(error);
            actionResult = SetHttpResponse(StatusCodes.Status400BadRequest, error);

            return false;
        }

        try
        {
            var readOnlyProperties = TypeDescriptor.GetProperties(typeof(T))
                .Cast<PropertyDescriptor>()
                .Where(p => p.IsReadOnly)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (configObject.EnumerateObject().Any(jp => readOnlyProperties.Contains(jp.Name)))
            {
                actionResult = SetHttpResponse(StatusCodes.Status400BadRequest, "Cannot accept request. One or more fields are read only.");
                return false;
            }

            value = (T)JsonSerializer.Deserialize(configObject.GetRawText(), typeof(T), _jsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            var error = string.Format(CultureInfo.InvariantCulture, InvalidInputTypeErrorString, componentId, facet, ex.Message);
            _systemLogger.LogError(ex, error);
            actionResult = SetHttpResponse(StatusCodes.Status400BadRequest, error);

            return false;
        }

        if (!configurationProvider.IsConfigurationValid(value, out var errors))
        {
            LogActionErrors("POST", facet, componentId, errors);
            actionResult = SetHttpResponse(StatusCodes.Status400BadRequest, errors);

            return false;
        }

        return true;
    }

    private bool IsReadonlyFacet(string componentId, string facet)
    {
        if (facet.Equals(DiscoveriesFacetName, StringComparison.OrdinalIgnoreCase) || facet.Equals(HistoryRecoveriesFacetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryGetCommandGeneratorTuple(componentId, facet, out _, out var commandGeneratorTuple))
        {
            return false;
        }

        return !commandGeneratorTuple.supportedOperations.HasFlag(Operations.Update) && !commandGeneratorTuple.supportedOperations.HasFlag(Operations.Create);
    }

    private void LogIgnoredReadOnlyFacets(IReadOnlyDictionary<string, IEnumerable<string>> readOnlyFacets)
    {
        if (readOnlyFacets.Count == 0)
        {
            return;
        }

        var componentFacets = new StringBuilder();
        foreach (var (componentId, facetList) in readOnlyFacets)
        {
            componentFacets.Append(Environment.NewLine).Append("  Component ID: ").Append(componentId).Append(" facets: ");
            foreach (var facet in facetList.ToList())
            {
                componentFacets.Append(facet).Append(", ");
            }

            // Trim last facet's trailing ", " 
            componentFacets.Length -= 2;
        }

        _systemLogger.LogInformation("Application configuration operation ignored the following read-only facets:{ComponentFacets}", componentFacets.ToString());
    }

    #endregion
}
