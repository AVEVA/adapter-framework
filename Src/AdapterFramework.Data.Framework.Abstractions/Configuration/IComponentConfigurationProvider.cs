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
namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// Provides methods for reading and saving configuration objects for a component.
/// </summary>
/// <remarks>
/// Component ID is being supplied by <see cref="IComponentConfigurationProvider"/> in order
/// to improve edge component developer experience.
/// </remarks> 
public interface IComponentConfigurationProvider
{
    /// <summary>
    /// Get the configuration object from the file named <paramref name="configName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object</typeparam>
    /// <param name="configName">The name of the configuration file</param>
    /// <returns>The configuration object</returns>
    T GetConfiguration<T>(string configName) where T : EdgeConfigurationBase;

    /// <summary>
    /// Save a configuration object to the file named <paramref name="configName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object</typeparam>
    /// <param name="configName">The name of the configuration file to store the configuration object</param>
    /// <param name="config">The configuration object to be saved</param>
    void SaveConfiguration<T>(string configName, T config) where T : EdgeConfigurationBase;

    /// <summary>
    /// Get the array of configurations from the file named <paramref name="configName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object</typeparam>
    /// <param name="configName">The name of the configuration file</param>
    /// <returns>The array of configurations</returns>
    T[] GetArrayConfiguration<T>(string configName) where T : EdgeConfigurationBase;

    /// <summary>
    /// Save the array of configurations to the file named <paramref name="configName"/>.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object</typeparam>
    /// <param name="configName">The name of the configuration file to store the array of configurations</param>
    /// <param name="config">The array of configurations to be saved</param>
    void SaveArrayConfiguration<T>(string configName, T[] config) where T : EdgeConfigurationBase;

    /// <summary>
    /// Returns full path to Common Application Data directory for the platform.
    /// </summary>
    /// <returns>Full path to Common Application Data directory.</returns>
    string GetAdapterDataDirectoryPath();
}
