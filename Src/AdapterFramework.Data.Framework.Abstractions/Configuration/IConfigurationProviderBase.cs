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
/// Interface for classes that are capable of creating Configuration Provider Base.
/// </summary>
public interface IConfigurationProviderBase
{
    /// <summary>
    /// Delete the configuration file named <paramref name="configName"/>.
    /// </summary>
    /// <param name="componentId">The id of the component requesting the configuration.</param>
    /// <param name="configName">The name of the configuration file to delete.</param>
    void DeleteConfiguration(string componentId, string configName);

    /// <summary>
    /// Returns path to a passed configuration file.
    /// </summary>
    /// <param name="componentId">The id of the component requesting the configuration.</param>
    /// <param name="configName">The name of the configuration file to get full path for.</param>
    /// <returns>Path to the <paramref name="configName"/> file.</returns>
    string GetConfigFilePath(string componentId, string configName);

    /// <summary>
    /// Returns path to application base directory where executable files are.
    /// </summary>
    /// <returns>Path to application base directory.</returns>
    string GetBaseDirectoryPath();

    /// <summary>
    /// Returns name of the common application data directory.
    /// </summary>
    /// <returns>Name of the common application data directory.</returns>
    string GetCommonApplicationDataDirectoryName();
}
