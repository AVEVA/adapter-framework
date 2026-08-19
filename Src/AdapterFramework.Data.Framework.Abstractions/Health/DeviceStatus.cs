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
namespace AdapterFramework.Data.Framework.Abstractions.Health;

/// <summary>
/// The list of possible statues that can describe the current state of the adapter to the device.
/// </summary>
public enum DeviceStatus
{
    /// <summary>Connected to device and collecting data.</summary>
    Good,

    /// <summary>Connected to device but not receiving data from it.</summary>
    ConnectedNoData,

    /// <summary>Adapter is attempting to failover.</summary>
    AttemptingFailover,

    /// <summary>Adapter just started up and is not connected to the device.</summary>
    Starting,

    /// <summary>An error occurred when connecting to device or collecting data.</summary>
    DeviceInError,

    /// <summary>Adapter is shutting down.</summary>
    Shutdown,

    /// <summary>The adapter component has been removed and will no longer collect data.</summary>
    Removed,

    /// <summary>The adapter component has been created but is not yet configured.</summary>
    NotConfigured,
}
