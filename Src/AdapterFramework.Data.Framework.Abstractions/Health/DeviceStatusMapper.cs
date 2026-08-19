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

namespace AdapterFramework.Data.Framework.Abstractions.Health;

public static class DeviceStatusMapper
{
    public static string DeviceStatusEnumToString(DeviceStatus status)
    {
        return status switch
        {
            DeviceStatus.Good => "Good",
            DeviceStatus.ConnectedNoData => "Connected / No Data",
            DeviceStatus.AttemptingFailover => "Attempting Failover",
            DeviceStatus.Starting => "Starting",
            DeviceStatus.DeviceInError => "Device in error",
            DeviceStatus.Shutdown => "Shutdown",
            DeviceStatus.Removed => "Removed",
            DeviceStatus.NotConfigured => "Not Configured",
            _ => throw new ArgumentOutOfRangeException(nameof(status), $"Encountered invalid enum {status}"),
        };
    }
}
