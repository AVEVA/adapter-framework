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
using System.Text.Json;

using AdapterFramework.Data.Framework.Abstractions.Security;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;

/// <summary>
/// Defines set of services provided to configuration components.
/// </summary>
public interface IConfigurationCommandGenerator
{
    /// <summary>
    /// Generates a configuration GET command.
    /// </summary>
    /// <param name="configurationType">Type of the configuration to retrieve.</param>
    /// <param name="id">Optional ID of configuration entry to retrieve.</param>
    /// <returns>New <see cref="IConfigurationGetCommand"/> instance.</returns>
    IConfigurationGetCommand GenerateConfigurationGetCommand(Type configurationType, string id);

    /// <summary>
    /// Generates a configuration SET command.
    /// </summary>
    /// <param name="newValue">New value to set.</param>
    /// <param name="configurationType">Type of the configuration to set.</param>
    /// <param name="id">Optional ID of configuration entry to set.</param>
    /// <param name="configurationProtector">Instance of <see cref="IConfigurationProtector"/> service.</param>
    /// <param name="customValidationFunction">Optional custom validation function that gets called before a new configuration object is saved to disk.</param>
    /// <returns>New <see cref="IConfigurationSetCommand"/> instance.</returns>
    IConfigurationSetCommand GenerateConfigurationSetCommand(JsonElement newValue, Type configurationType, string id, IConfigurationProtector configurationProtector, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction);

    /// <summary>
    /// Generates a configuration CREATE command.
    /// </summary>
    /// <param name="newValue">New value to set.</param>
    /// <param name="configurationType">Type of the configuration to set.</param>
    /// <param name="configurationProtector">Instance of <see cref="IConfigurationProtector"/> service.</param>
    /// <param name="customValidationFunction">Optional custom validation function that gets called before configuration object is saved to disk.</param>
    /// <returns>New <see cref="IConfigurationSetCommand"/> instance.</returns>
    IConfigurationSetCommand GenerateConfigurationCreateCommand(JsonElement newValue, Type configurationType, IConfigurationProtector configurationProtector, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction);

    /// <summary>
    /// Generates a configuration PATCH command.
    /// </summary>
    /// <param name="patches">Properties to patch.</param>
    /// <param name="configurationType">Type of the configuration to patch.</param>
    /// <param name="id">Optional ID of configuration entry to patch.</param>
    /// <param name="configurationProtector">Instance of <see cref="IConfigurationProtector"/> service.</param>
    /// <param name="customValidationFunction">Optional custom validation function that gets called before configuration object is saved to disk.</param>
    /// <returns>New <see cref="IConfigurationSetPatchCommand"/> instance.</returns>
    IConfigurationSetPatchCommand GenerateConfigurationPatchCommand(JsonElement patches, Type configurationType, string id, IConfigurationProtector configurationProtector, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction);

    /// <summary>
    /// Generates a configuration DELETE command.
    /// </summary>
    /// <param name="configurationType">Type of the configuration to delete.</param>
    /// <param name="id">Optional ID of configuration entry to delete.</param>
    /// <param name="customValidationFunction">Optional custom validation function that gets called before configuration object is saved to disk.</param>
    /// <returns>New <see cref="IConfigurationSetCommand"/> instance.</returns>
    IConfigurationSetCommand GenerateConfigurationDeleteCommand(Type configurationType, string id, Func<ConfigurationChangedEventArgs, ICollection<string>> customValidationFunction);
}
