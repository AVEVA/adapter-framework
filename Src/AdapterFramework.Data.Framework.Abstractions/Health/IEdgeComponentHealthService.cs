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
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Components;

namespace AdapterFramework.Data.Framework.Abstractions.Health;

/// <summary>
/// Represents a service used to generate Health data for <see cref="IEdgeComponent"/>.
/// </summary>
public interface IEdgeComponentHealthService
{
    /// <summary>
    /// Starts the health service. Currently only activates heartbeat message timer. 
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task StartAsync();

    /// <summary>
    /// Stops the health service. Currently only deactivates heartbeat message timer. 
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task StopAsync();

    /// <summary>
    /// Does the required initial setup in order to start sending OMF data message and health methods.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Sends again the last device status value 
    /// </summary>
    void ResendDeviceStatus();

    /// <summary>
    /// Sends again types, streams and links created by the service.
    /// </summary>
    void ResendTypesAndStreams();
}
