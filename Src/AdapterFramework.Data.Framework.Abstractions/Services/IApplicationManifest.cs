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
using AdapterFramework.Data.DataModel;

namespace AdapterFramework.Data.Framework.Abstractions.Services;

public interface IApplicationManifest
{
    /// <summary>
    /// Returns application port number.
    /// </summary>
    int ApplicationPort { get; }

    /// <summary>
    /// Returns version of AVEVA Adapter Framework.
    /// </summary>
    Version AdapterFrameworkVersion { get; }

    /// <summary>
    /// Returns base application URL including port number.
    /// </summary>
    string BaseApplicationAddress { get; }

    /// <summary>
    /// Returns version of the application.
    /// </summary>
    Version ProductVersion { get; }

    /// <summary>
    /// Returns the version of OMF the OMFEgress service is using.
    /// </summary>
    OmfVersion OmfVersion { get; }

    /// <summary>
    /// Returns health prefix.
    /// </summary>
    string HealthPrefix { get; }

    /// <summary>
    /// Returns machine name of --deviceName value when the command-line argument is included.
    /// </summary>
    string MachineName { get; }

    /// <summary>
    /// Returns service name.
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// Returns true when product is Edge Data Store; false when standalone AVEVA Adapter.
    /// </summary>
    bool IsEdgeDataStore { get; }

    /// <summary>
    /// Searches for the <paramref name="componentFullName"/> in the set of AdapterFramework executables deployed with the application.
    /// </summary>
    /// <param name="componentFullName">Full name of the component ('AdapterFramework.Data.Framework.Abstractions' for instance).</param>
    /// <returns>True when the component was found, false otherwise.</returns>
    bool HasComponent(string componentFullName);

    /// <summary>
    /// Gets the types of the available adapters.
    /// </summary>
    /// <returns>The types of the available adapters.</returns>
    IEnumerable<string> GetAdapterTypes();
}
