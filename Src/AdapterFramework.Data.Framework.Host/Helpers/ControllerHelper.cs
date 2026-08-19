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
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Web;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Host.Helpers;

public class ControllerHelper : Controller
{
    protected const string ComponentNotFoundString = "Component with ID: '{0}' was not found.";
    private const string ConfigNotArrayType = $"Requested configuration isn't of type {nameof(Array)} and cannot be retrieved by ID.";
    private const string EmptyObjectString = "{}";
    private const string EmptyArrayString = "[]";
    private const string EntryWithIdNotFoundString = "Requested configuration entry with ID: '{0}' was not found.";
    private const string FacetNotFoundString = "Facet with name: '{0}' was not found.";
    private const string InvalidInputErrorString = "Invalid input. Configuration value for component ID: '{0}' and facet: '{1}' cannot be null.";
    private const string ModelStateErrorString = "Bad request reported from {RequestPath} - Message: {ModelStateError}";
    private const string UnableToSerializeConfig = "Unable to read configuration for component ID {0} and facet {1}: {2}";
    private const string UndefinedIdErrorString = "Requested configuration of type {0} doesn't have {1} defined and cannot be retrieved by ID.";

    private readonly IConfigurationProtector _configurationProtector;
    private readonly ILogger _logger;
    private readonly IRuntimeConfigurationRegistry _runtimeConfigurationRegistry;

    public ControllerHelper(IConfigurationProtector configurationProtector, ILogger logger, IRuntimeConfigurationRegistry runtimeConfigurationRegistry)
    {
        _configurationProtector = configurationProtector;
        _logger = logger;
        _runtimeConfigurationRegistry = runtimeConfigurationRegistry;
    }

    #region Protected Methods

    protected IActionResult GetFacetConfiguration(string componentId, string facet, string id = null, int count = 0, int skip = 0)
    {
        if (TryGenerateGetCommand(componentId, facet, id, out var actionResult, out var getCommand, out var commandGeneratorTuple))
        {
            ConfigurationProcessor.TryExecuteCommand(getCommand, out var configObject, out var errors);

            if (errors.Count != 0)
            {
                return SetHttpResponse(StatusCodes.Status500InternalServerError, string.Format(CultureInfo.InvariantCulture, UnableToSerializeConfig, componentId, facet, string.Join('\n', errors)));
            }

            if (id == null)
            {
                configObject = configObject != null ? MaskSecretsInConfiguration(commandGeneratorTuple, configObject) : GetEmptyConfiguration(commandGeneratorTuple.ConfigurationType);

                if (count > 0 && configObject is IEnumerable<object> enumerable)
                {
                    return Ok(enumerable.Skip(skip).Take(count));
                }

                return Ok(configObject);
            }

            if (configObject != null)
            {
                configObject = MaskSecretsInConfiguration(commandGeneratorTuple, configObject);
                return Ok(configObject);
            }

            return SetHttpResponse(StatusCodes.Status404NotFound, string.Format(CultureInfo.InvariantCulture, EntryWithIdNotFoundString, id));
        }

        return actionResult;
    }

    protected IActionResult PostFacetConfiguration(string componentId, string facet, JsonElement configObject, string id = null)
    {
        if (ModelState.IsValid)
        {
            if (TryGenerateCreateCommand(componentId, facet, configObject, out var actionResult, out var setCommand, out var commandGeneratorTuple))
            {
                if (ConfigurationProcessor.TryExecuteCommand(setCommand, commandGeneratorTuple.CallbackAction, _logger, out ICollection<string> errors))
                {
                    return NoContent();
                }

                LogActionErrors("POST", facet, componentId, errors, id);
                return SetHttpResponse(StatusCodes.Status400BadRequest, errors);
            }

            return actionResult;
        }

        LogEntryFromModelStateError(Request.Path.Value, ModelState);
        return new BadRequestObjectResult(ModelState);
    }

    protected IActionResult PutFacetConfiguration(string componentId, string facet, JsonElement configObject, string id = null)
    {
        if (ModelState.IsValid)
        {
            if (TryGenerateSetCommand(componentId, facet, id, configObject, out var actionResult, out var setCommand, out var commandGeneratorTuple))
            {
                if (ConfigurationProcessor.TryExecuteCommand(setCommand, commandGeneratorTuple.CallbackAction, _logger, out ICollection<string> errors))
                {
                    return NoContent();
                }

                LogActionErrors("PUT", facet, componentId, errors, id);
                return SetHttpResponse(StatusCodes.Status400BadRequest, errors);
            }

            return actionResult;
        }

        LogEntryFromModelStateError(Request.Path.Value, ModelState);
        return new BadRequestObjectResult(ModelState);
    }

    protected IActionResult PatchFacetConfiguration(string componentId, string facet, JsonElement configObject, string id = null)
    {
        if (ModelState.IsValid)
        {
            if (TryGeneratePatchCommand(componentId, facet, id, configObject, out var actionResult, out var patchCommand, out var commandGeneratorTuple))
            {
                if (ConfigurationProcessor.TryExecuteCommand(patchCommand, commandGeneratorTuple.CallbackAction, _logger, out ICollection<(int StatusCode, string ErrorMessage)> errors))
                {
                    return NoContent();
                }

                LogActionErrors("PATCH", facet, componentId, errors, id);
                foreach (var error in errors)
                {
                    if (error.StatusCode == StatusCodes.Status409Conflict)
                    {
                        return SetHttpResponse(StatusCodes.Status409Conflict, error.ErrorMessage);
                    }
                }

                return SetHttpResponse(StatusCodes.Status400BadRequest, errors);
            }

            return actionResult;
        }

        LogEntryFromModelStateError(Request.Path.Value, ModelState);
        return new BadRequestObjectResult(ModelState);
    }

    protected IActionResult DeleteFacetConfiguration(string componentId, string facet, string id = null)
    {
        if (TryGenerateDeleteCommand(componentId, facet, id, out var actionResult, out var deleteCommand, out var commandGeneratorTuple))
        {
            if (ConfigurationProcessor.TryExecuteCommand(deleteCommand, commandGeneratorTuple.CallbackAction, _logger, out ICollection<string> errors))
            {
                return NoContent();
            }

            LogActionErrors("DELETE", facet, componentId, errors, id);
            return SetHttpResponse(StatusCodes.Status400BadRequest, errors);
        }

        return actionResult;
    }

    protected object MaskSecretsInConfiguration((IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction,
        Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunc, Type ConfigurationType, Operations SupportedOperations) commandGeneratorTuple, object facetConfiguration)
    {
        if (facetConfiguration is object[] configurationObjects)
        {
            _configurationProtector.MaskSecrets(ref configurationObjects, commandGeneratorTuple.ConfigurationType);
            facetConfiguration = configurationObjects;
        }
        else
        {
            _configurationProtector.MaskSecrets(ref facetConfiguration, commandGeneratorTuple.ConfigurationType);
        }

        return facetConfiguration;
    }

    protected bool TryGenerateSetCommand(string componentId, string facet, string id, JsonElement configurationObject, out IActionResult result, out IConfigurationSetCommand setCommand,
        out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunc,
        Type ConfigurationType, Operations supportedOperations) commandGeneratorTuple)
    {
        setCommand = null;

        if (!ValidateAndTryGetCommandGeneratorTuple(componentId, facet, id, configurationObject, out result, out commandGeneratorTuple))
        {
            return false;
        }

        if (!commandGeneratorTuple.supportedOperations.HasFlag(Operations.Update) || !commandGeneratorTuple.supportedOperations.HasFlag(Operations.Create))
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, EdgeSystemConstants.UnsupportedOperationString, Operations.Create + " or " + Operations.Update, facet));
            return false;
        }

        try
        {
            setCommand = commandGeneratorTuple.CommandGenerator.GenerateConfigurationSetCommand(configurationObject,
                commandGeneratorTuple.ConfigurationType, id, _configurationProtector, commandGeneratorTuple.CustomValidationFunc);
        }
        catch (Exception ex)
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, ex.GetExceptionTypeAndMessages());
            return false;
        }

        return true;
    }

    protected bool TryGenerateGetCommand(string componentId, string facet, string id, out IActionResult result, out IConfigurationGetCommand getCommand,
        out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs,
            ICollection<string>> CustomValidationFunc, Type ConfigurationType, Operations supportedOperations) commandGeneratorTuple)
    {
        getCommand = null;

        if (!TryGetCommandGeneratorTuple(componentId, facet, out result, out commandGeneratorTuple))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(id))
        {
            if (!IsIndexedConfigurationType(commandGeneratorTuple.ConfigurationType, out result))
            {
                return false;
            }
        }

        getCommand = commandGeneratorTuple.CommandGenerator.GenerateConfigurationGetCommand(commandGeneratorTuple.ConfigurationType, id);

        return true;
    }

    protected bool TryGetCommandGeneratorTuple(string componentId, string facet, out IActionResult result,
        out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunc,
            Type ConfigurationType, Operations supportedOperations) commandGeneratorTuple)
    {
        result = null;
        commandGeneratorTuple = default;

        if (!IsComponentRegistered(componentId))
        {
            result = SetHttpResponse(StatusCodes.Status404NotFound, string.Format(CultureInfo.InvariantCulture, ComponentNotFoundString, componentId));
            return false;
        }

        if (!_runtimeConfigurationRegistry.TryGetCommandGeneratorTuple((componentId, facet), out commandGeneratorTuple))
        {
            result = SetHttpResponse(StatusCodes.Status404NotFound, string.Format(CultureInfo.InvariantCulture, FacetNotFoundString, facet));
            return false;
        }

        return true;
    }

    protected void LogEntryFromModelStateError(string requestPath, ModelStateDictionary modelState)
    {
        _logger.LogError(ModelStateErrorString, requestPath, GetModelErrorString(modelState));
    }

    protected void LogActionErrors(string restAction, string facet, string componentId, ICollection<string> errors, string id = null)
    {
        if (id == null)
        {
            _logger.LogError("Unable to {Action} configuration '{Facet}' for component '{ComponentId}'. {Errors}", restAction, facet, componentId, errors);
        }
        else
        {
            _logger.LogError("Unable to {Action} configuration '{Facet}' for component '{ComponentId} with id '{Id}'. {Errors}", restAction, facet, componentId, id, errors);
        }
    }

    protected void LogActionErrors(string restAction, string facet, string componentId, ICollection<(int StatusCode, string ErrorMessage)> errors, string id = null)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var messageError = new StringBuilder();
        foreach (var error in errors)
        {
            messageError.Append(error.ErrorMessage);
            messageError.Append(", ");
        }

        messageError.Remove(messageError.Length - 2, 2);
        if (id == null)
        {
            _logger.LogError("Unable to {Action} configuration '{Facet}' for component '{ComponentId}'. {Errors}", restAction, facet, componentId, messageError.ToString());
        }
        else
        {
            _logger.LogError("Unable to {Action} configuration '{Facet}' for component '{ComponentId} with id '{Id}'. {Errors}", restAction, facet, componentId, id, messageError.ToString());
        }
    }

    protected bool IsComponentRegistered(string componentId)
    {
        return _runtimeConfigurationRegistry.TryGetAvailableFacets(componentId, out _);
    }

    protected IActionResult SetHttpResponse(int httpCode, IEnumerable<string> messages, string reason = "", string resolution = "")
    {
        return SetHttpResponse(httpCode, string.Join("; ", messages), reason, resolution);
    }

    protected IActionResult SetHttpResponse(int httpCode, ICollection<(int StatusCode, string ErrorMessage)> errors, string reason = "", string resolution = "")
    {
        ArgumentNullException.ThrowIfNull(errors);

        var errorMessage = new StringBuilder();
        foreach (var error in errors)
        {
            errorMessage.Append(error.ErrorMessage);
            errorMessage.Append("; ");
        }

        errorMessage.Remove(errorMessage.Length - 2, 2);
        return SetHttpResponse(httpCode, errorMessage.ToString(), reason, resolution);
    }

    /// <summary>
    /// This is a helper function to set the HTTP return code in a response and copy an error
    /// message into the response's body.
    /// </summary>
    /// <param name="httpCode">HTTP code (e.g. 200 => OK)</param>
    /// <param name="message">Message to be placed as a text string in the HTTP response</param>
    /// <param name="reason">Reason for a response.</param>
    /// <param name="resolution">Potential resolution (suggestion) of a problem.</param>
    /// <returns>A Task object so that this return can be called asynchronously</returns>
    protected IActionResult SetHttpResponse(int httpCode, string message, string reason = "", string resolution = "")
    {
        if (!message.IsNullOrEmpty())
        {
            var json = new RestApiErrorResponse(message, reason, resolution);
            return StatusCode(httpCode, json);
        }

        return StatusCode(httpCode);
    }

    #endregion

    #region Private Methods

    private static bool HasIdAttributeDefined(Type configurationType)
    {
        return configurationType.GetProperties().FirstOrDefault(propertyInfo => Attribute.IsDefined(propertyInfo, typeof(IdAttribute))) != null;
    }

    private static object GetEmptyConfiguration(Type configurationType)
    {
        return configurationType.IsArray ? JsonSerializer.Deserialize<JsonElement>(EmptyArrayString) : JsonSerializer.Deserialize<JsonElement>(EmptyObjectString);
    }

    private static string GetModelErrorString(ModelStateDictionary modelState)
    {
        ThrowHelper.ThrowIfArgumentNull(modelState, nameof(modelState));

        if (modelState.IsValid)
        {
            return string.Empty;
        }

        var errorStringBuilder = new StringBuilder();
        foreach (var item in modelState.Values)
        {
            if (item.Errors.Count > 0)
            {
                foreach (var error in item.Errors)
                {
                    errorStringBuilder.Append(error.ErrorMessage).Append(Environment.NewLine);
                }
            }
        }

        return errorStringBuilder.ToString();
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

    private bool IsIndexedConfigurationType(Type configurationType, out IActionResult actionResult)
    {
        actionResult = null;

        if (!configurationType.IsArray)
        {
            actionResult = SetHttpResponse(StatusCodes.Status400BadRequest, ConfigNotArrayType);
            return false;
        }

        if (!HasIdAttributeDefined(configurationType.GetElementType()))
        {
            actionResult = SetHttpResponse(StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, UndefinedIdErrorString, configurationType.Name, nameof(IdAttribute)));
            return false;
        }

        return true;
    }

    private bool TryGenerateCreateCommand(string componentId, string facet, JsonElement configurationObject, out IActionResult result, out IConfigurationSetCommand setCommand,
        out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs,
            ICollection<string>> CustomValidationFunc, Type ConfigurationType, Operations supportedOperations) commandGeneratorTuple)
    {
        setCommand = null;

        if (!ValidateAndTryGetCommandGeneratorTuple(componentId, facet, null, configurationObject, out result, out commandGeneratorTuple))
        {
            return false;
        }

        if (!commandGeneratorTuple.supportedOperations.HasFlag(Operations.Update) || !commandGeneratorTuple.supportedOperations.HasFlag(Operations.Create))
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, EdgeSystemConstants.UnsupportedOperationString, Operations.Create + " or " + Operations.Update, facet));
            return false;
        }

        try
        {
            setCommand = commandGeneratorTuple.CommandGenerator.GenerateConfigurationCreateCommand(configurationObject,
                commandGeneratorTuple.ConfigurationType, _configurationProtector, commandGeneratorTuple.CustomValidationFunc);
        }
        catch (Exception ex)
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, ex.GetExceptionTypeAndMessages());
            return false;
        }

        return true;
    }

    private bool TryGeneratePatchCommand(string componentId, string facet, string id, JsonElement configurationObject, out IActionResult result, out IConfigurationSetPatchCommand patchCommand,
        out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunc,
            Type ConfigurationType, Operations supportedOperations) commandGeneratorTuple)
    {
        patchCommand = null;

        if (!ValidateAndTryGetCommandGeneratorTuple(componentId, facet, id, configurationObject, out result, out commandGeneratorTuple))
        {
            return false;
        }

        if (!commandGeneratorTuple.supportedOperations.HasFlag(Operations.Update))
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, EdgeSystemConstants.UnsupportedOperationString, Operations.Update, facet));
            return false;
        }

        try
        {
            patchCommand = commandGeneratorTuple.CommandGenerator.GenerateConfigurationPatchCommand(configurationObject,
                commandGeneratorTuple.ConfigurationType, id, _configurationProtector, commandGeneratorTuple.CustomValidationFunc);
        }
        catch (Exception ex)
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, ex.GetExceptionTypeAndMessages());
            return false;
        }

        return true;
    }

    private bool TryGenerateDeleteCommand(string componentId, string facet, string id, out IActionResult result, out IConfigurationSetCommand deleteCommand,
        out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunc,
            Type ConfigurationType, Operations supportedOperations) commandGeneratorTuple)
    {
        deleteCommand = null;

        if (!TryGetCommandGeneratorTuple(componentId, facet, out result, out commandGeneratorTuple))
        {
            return false;
        }

        if (!commandGeneratorTuple.supportedOperations.HasFlag(Operations.Delete))
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, EdgeSystemConstants.UnsupportedOperationString, Operations.Delete, facet));
            return false;
        }

        if (!string.IsNullOrEmpty(id))
        {
            if (!IsIndexedConfigurationType(commandGeneratorTuple.ConfigurationType, out result))
            {
                return false;
            }
        }

        deleteCommand = commandGeneratorTuple.CommandGenerator.GenerateConfigurationDeleteCommand(commandGeneratorTuple.ConfigurationType, id, commandGeneratorTuple.CustomValidationFunc);

        return true;
    }

    private bool ValidateAndTryGetCommandGeneratorTuple(string componentId, string facet, string id, object configurationObject, out IActionResult result,
        out (IConfigurationCommandGenerator CommandGenerator, Action<ConfigurationChangedEventArgs> CallbackAction, Func<ConfigurationChangedEventArgs, ICollection<string>> CustomValidationFunc,
            Type ConfigurationType, Operations supportedOperations) commandGeneratorTuple)
    {
        if (!TryGetCommandGeneratorTuple(componentId, facet, out result, out commandGeneratorTuple))
        {
            return false;
        }

        if (configurationObject == null)
        {
            result = SetHttpResponse(StatusCodes.Status400BadRequest, string.Format(CultureInfo.InvariantCulture, InvalidInputErrorString, componentId, facet));
            return false;
        }

        if (!string.IsNullOrEmpty(id))
        {
            if (!IsIndexedConfigurationType(commandGeneratorTuple.ConfigurationType, out result))
            {
                return false;
            }
        }

        return true;
    }

    #endregion
}
