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

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// Provides methods for reading, saving and deleting configuration objects for components.
/// </summary>
public interface IConfigurationProvider
{
    /// <summary>
    /// Get the configuration object from the file named <paramref name="configurationName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="componentId">Id of the component requesting the configuration.</param>
    /// <param name="configurationName">The name of the configuration file.</param>
    /// <returns>The configuration object.</returns>
    T GetConfiguration<T>(string componentId, string configurationName)
        where T : class;

    /// <summary>
    /// Get the configuration object from the file named <paramref name="configurationName"/>.
    /// </summary>
    /// <param name="componentId">Component Id.</param>
    /// <param name="configurationName">The name of the configuration file.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    /// <returns>The configuration object.</returns>
    object GetConfiguration(string componentId, string configurationName, Type configurationType);

    /// <summary>
    /// Tries to get configuration object for the file named <paramref name="configurationName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="componentId">Id of the component requesting configuration.</param>
    /// <param name="configurationName">The name of the configuration file.</param>
    /// <param name="configuration">The configuration object.</param>
    /// <param name="errors">List of errors for the caller to handle.</param>
    /// <returns>True when configuration is found and valid. False otherwise.</returns>
    bool TryGetConfiguration<T>(string componentId, string configurationName, out T configuration, out ICollection<string> errors)
        where T : class;

    /// <summary>
    /// Tries to get configuration object for the file named <paramref name="configurationName"/>.
    /// </summary>
    /// <param name="componentId">Id of the component requesting configuration.</param>
    /// <param name="configurationName">The name of the configuration file.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    /// <param name="configuration">The configuration object.</param>
    /// <param name="errors">List of errors for the caller to handle.</param>
    /// <returns>True when configuration is found and valid. False otherwise.</returns>
    bool TryGetConfiguration(string componentId, string configurationName, Type configurationType, out object configuration, out ICollection<string> errors);

    /// <summary>
    /// Save a configuration object to the file named <paramref name="configurationName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="componentId">Id of the component requesting configuration.</param>
    /// <param name="configurationName">The name of the configuration file to store the configuration object.</param>
    /// <param name="config">The configuration object to be saved.</param>
    void SaveConfiguration<T>(string componentId, string configurationName, T config)
        where T : class;

    /// <summary>
    /// Tries to save a configuration object to the file named <paramref name="configurationName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="componentId">Id of the component requesting configuration.</param>
    /// <param name="configurationName">The name of the configuration file to store the configuration object.</param>
    /// <param name="configuration">The configuration object to be saved.</param>
    /// <param name="errors">List of errors for the caller to handle.</param>
    /// <returns>True when given configuration is valid and saved. False otherwise.</returns>
    bool TrySaveConfiguration<T>(string componentId, string configurationName, T configuration, out ICollection<string> errors)
        where T : class;

    /// <summary>
    /// Performs validation of <see paramref="configuration"/> by triggering <see cref="IValidatableObject"/> validation method.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be validated.</param>
    /// <param name="errors">List of errors for the caller to handle.</param>
    /// <returns>True when given configuration is valid. False otherwise.</returns>
    bool IsConfigurationValid<T>(T configuration, out ICollection<string> errors)
        where T : class;

    /// <summary>
    /// Delete the configuration file named <paramref name="configurationName"/>.
    /// </summary>
    /// <param name="componentId">The id of the component requesting delete operation.</param>
    /// <param name="configurationName">The name of the configuration file to delete.</param>
    void DeleteConfiguration(string componentId, string configurationName);

    /// <summary>
    /// Returns full path to Common Application Data directory.
    /// </summary>
    /// <param name="componentId">Optional ID of a component for which common application data directory should be returned.</param>
    /// <returns>Full path to root Common Application Data directory when componentId isn't specified or to ComponentId specific common data directory.</returns>
    string GetCommonApplicationDataDirectoryPath(string componentId = null);

    /// <summary>
    /// Returns name of the common application data directory.
    /// </summary>
    /// <returns>Name of the common application data directory.</returns>
    string GetCommonApplicationDataDirectoryName();

    /// <summary>
    /// Tries to get discovery result object belonging to <paramref name="discoveryId"/>.
    /// </summary>
    /// <typeparam name="T">The type of the discovery result object.</typeparam>
    /// <param name="componentId">Id of the component requesting discovery result.</param>
    /// <param name="discoveryId">Id of the discovery operation.</param>
    /// <param name="discoveryResult">The discovery result object.</param>
    /// <param name="errors">List of errors for the caller to handle.</param>
    /// <returns>True when discovery result is found and valid. False otherwise.</returns>
    bool TryGetDiscoveryResult<T>(string componentId, string discoveryId, out T discoveryResult, out ICollection<string> errors) where T : class;

    /// <summary>
    /// Tries to save a discovery result object belonging to <paramref name="discoveryId"/>.
    /// </summary>
    /// <typeparam name="T">The type of the discovery result object.</typeparam>
    /// <param name="componentId">Id of the component persisting discovery result.</param>
    /// <param name="discoveryId">Id of the discovery operation.</param>
    /// <param name="discoveryConfiguration">The discovery result object.</param>
    /// <param name="errors">List of errors for the caller to handle.</param>
    /// <returns>True when given configuration is valid and saved. False otherwise.</returns>
    bool TrySaveDiscoveryResult<T>(string componentId, string discoveryId, T discoveryConfiguration, out ICollection<string> errors) where T : class;

    /// <summary>
    ///  Delete the discovery results file belonging to <paramref name="componentId"/> and given <paramref name="discoveryId"/>.
    /// </summary>
    /// <param name="componentId">Id of the component to delete the result for.</param>
    /// <param name="discoveryId">Id of discovery operation.</param>
    void DeleteDiscoveryResult(string componentId, string discoveryId);

    /// <summary>
    /// Move the corrupted configuration file named <paramref name="configurationName"/> to the Removed folder.
    /// </summary>
    /// <param name="componentId">Id of the component requesting configuration.</param>
    /// <param name="configurationName">The name of the configuration file to be moved.</param>
    void MoveCorruptedConfiguration(string componentId, string configurationName);

    /// <summary>
    /// Tries to move the corrupted configuration file named <paramref name="configurationName"/> to the Removed folder.
    /// </summary>
    /// <param name="componentId">Id of the component requesting configuration.</param>
    /// <param name="configurationName">The name of the configuration file to be moved.</param>
    /// <param name="errorMessage">List of errors for the caller to handle.</param>
    /// <returns>True when given configuration is moved successfully. False otherwise.</returns>
    bool TryMoveCorruptedConfiguration(string componentId, string configurationName, out string errorMessage);
}
