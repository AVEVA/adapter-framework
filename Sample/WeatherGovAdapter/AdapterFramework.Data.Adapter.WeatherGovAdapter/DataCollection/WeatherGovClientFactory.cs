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
using AdapterFramework.Data.Adapter.WeatherGovAdapter.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;

/// <summary>
/// Creates <see cref="WeatherGovClient"/> instances from the framework-managed HTTP client factory.
/// </summary>
internal class WeatherGovClientFactory : IWeatherGovClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WeatherGovClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public WeatherGovClient Create(DataSourceConfiguration config, ILogger logger) =>
        new(_httpClientFactory.CreateClient(AdapterConstants.ClientName), config, logger);
}