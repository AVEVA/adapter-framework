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
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.DataCollection;

/// <summary>
/// Unit tests for <see cref="WeatherGovHttpClientExtensions"/>. They verify that the Weather.gov named
/// HTTP client is registered on the dependency-injection container, is resolvable from the
/// <see cref="IHttpClientFactory"/>, and that its per-request timeout and pooled primary handler are
/// configured as intended, all without making a real network call.
/// </summary>
public class WeatherGovHttpClientExtensionsTests
{
    // ----- Argument guarding and chaining -----

    /// <summary>
    /// Verifies that registering the client on a null service collection throws.
    /// </summary>
    [Fact]
    public void AddWeatherGovHttpClient_NullServices_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => WeatherGovHttpClientExtensions.AddWeatherGovHttpClient(null));

        Assert.Equal("services", exception.ParamName);
    }

    /// <summary>
    /// Verifies that the same service collection is returned so registrations can be chained.
    /// </summary>
    [Fact]
    public void AddWeatherGovHttpClient_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddWeatherGovHttpClient();

        Assert.Same(services, result);
    }

    // ----- Named client registration -----

    /// <summary>
    /// Verifies that the named client disables its own timeout so the WeatherGovClient per-request
    /// cancellation owns the deadline rather than being capped by the client.
    /// </summary>
    [Fact]
    public void AddWeatherGovHttpClient_ConfiguresInfiniteTimeout()
    {
        using var provider = new ServiceCollection().AddWeatherGovHttpClient().BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(AdapterConstants.ClientName);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    // ----- Primary handler configuration -----

    /// <summary>
    /// Verifies that the primary handler is a pooled <see cref="SocketsHttpHandler"/> whose connection
    /// lifetime is bounded (so DNS is periodically re-resolved) and that responses are transparently
    /// decompressed.
    /// </summary>
    [Fact]
    public void AddWeatherGovHttpClient_ConfiguresPooledSocketsHandler()
    {
        using var provider = new ServiceCollection().AddWeatherGovHttpClient().BuildServiceProvider();

        var handler = BuildPrimaryHandler(provider, AdapterConstants.ClientName);

        var socketsHandler = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.Equal(TimeSpan.FromMinutes(5), socketsHandler.PooledConnectionLifetime);
        Assert.Equal(DecompressionMethods.All, socketsHandler.AutomaticDecompression);
    }

    // ----- Helpers -----

    // Runs the registered handler-builder actions for a named client and returns the primary handler set
    // by ConfigurePrimaryHttpMessageHandler, without building the factory's full delegating-handler chain.
    private static HttpMessageHandler BuildPrimaryHandler(IServiceProvider provider, string name)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(name);
        var builder = new StubHttpMessageHandlerBuilder();

        foreach (var configure in options.HttpMessageHandlerBuilderActions)
        {
            configure(builder);
        }

        return builder.PrimaryHandler;
    }

    /// <summary>
    /// A minimal <see cref="HttpMessageHandlerBuilder"/> that only captures the primary handler the
    /// configured builder actions assign, so the registration can be inspected in isolation.
    /// </summary>
    private sealed class StubHttpMessageHandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; }

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();

        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}
