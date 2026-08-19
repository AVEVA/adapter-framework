// Copyright 2026 AVEVA Group Limited
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

using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.Interfaces;

/// <summary>
/// Creates configured <see cref="WeatherGovClient"/> instances for adapter runtime operations.
/// </summary>
internal interface IWeatherGovClientFactory
{
    /// <summary>
    /// Creates a client for the provided data source configuration.
    /// </summary>
    /// <param name="config">The active data source configuration.</param>
    /// <param name="logger">Logger to use for client diagnostics.</param>
    /// <returns>A configured Weather.gov client.</returns>
    WeatherGovClient Create(DataSourceConfiguration config, ILogger logger);
}