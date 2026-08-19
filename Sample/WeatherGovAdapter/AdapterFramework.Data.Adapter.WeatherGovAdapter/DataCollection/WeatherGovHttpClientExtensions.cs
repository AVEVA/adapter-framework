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

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;

/// <summary>
/// Registers the Weather.gov <see cref="IHttpClientFactory"/> named client on the framework's
/// dependency-injection container.
/// </summary>
/// <remarks>
/// The host invokes <c>AdapterMain.AddComponent</c> during service registration, which calls
/// <see cref="AddWeatherGovHttpClient"/>. The factory owns the lifetime and pooling of the underlying
/// <see cref="SocketsHttpHandler"/> — rotating pooled connections so DNS changes are picked up and
/// sockets are not exhausted — so the adapter never creates or disposes a raw handler. Per-request
/// settings (base URL, timeout, and User-Agent) are applied by <see cref="WeatherGovClient"/> on each
/// call, so the single named client serves any configuration.
/// </remarks>
internal static class WeatherGovHttpClientExtensions
{
    /// <summary>
    /// Registers the named Weather.gov HTTP client and its pooled primary handler.
    /// </summary>
    /// <param name="services">The service collection to add the client to.</param>
    /// <returns>The same <paramref name="services"/> instance so calls can be chained.</returns>
    public static IServiceCollection AddWeatherGovHttpClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            // The per-request timeout is owned by WeatherGovClient's linked CancellationTokenSource, so
            // the client's own timeout is disabled to avoid capping a longer configured RequestTimeoutMs.
            .AddHttpClient(AdapterConstants.ClientName, static client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                // Bounded so the handler periodically re-resolves DNS without a new handler per request.
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),

                // weather.gov returns large JSON payloads; transparent decompression cuts bandwidth.
                AutomaticDecompression = DecompressionMethods.All,
            });

        return services;
    }
}
