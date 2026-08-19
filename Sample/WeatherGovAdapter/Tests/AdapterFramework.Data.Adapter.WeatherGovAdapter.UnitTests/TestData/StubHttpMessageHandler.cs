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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;

/// <summary>
/// Shared stub <see cref="HttpMessageHandler"/> for the adapter's HTTP tests. Records each request's URI,
/// path, and User-Agent (plus a call count) and produces responses in one of two modes: a fixed sequence
/// of responders (repeating the last once the sequence is exhausted), or a per-request route function
/// (for multi-endpoint flows such as discovery). Honors cancellation like the real HttpClient so
/// caller-cancellation tests observe it.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responders;
    private readonly Func<CancellationToken, Task<HttpResponseMessage>> _last;
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;

    /// <summary>
    /// Replays the given responders in order, repeating the last once the sequence is exhausted. With no
    /// responders, every request returns 200 OK.
    /// </summary>
    public StubHttpMessageHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] responders)
    {
        if (responders is null || responders.Length == 0)
        {
            responders = [static _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))];
        }

        _responders = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(responders);
        _last = responders[^1];
    }

    /// <summary>
    /// Routes each request through <paramref name="route"/>, so a multi-endpoint flow (for example
    /// discovery's point lookup, stations collection, and per-station probe) can be driven per request.
    /// </summary>
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> route)
    {
        _route = route;
    }

    /// <summary>Gets the number of requests the handler has received.</summary>
    public int CallCount { get; private set; }

    /// <summary>Gets the absolute URI of each request received, in order.</summary>
    public List<Uri> RequestUris { get; } = [];

    /// <summary>Gets the absolute path of each request received, in order.</summary>
    public List<string> RequestPaths { get; } = [];

    /// <summary>Gets the User-Agent header of each request received (null when absent), in order.</summary>
    public List<string> UserAgents { get; } = [];

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Honor cancellation like the real HttpClient so caller-cancellation tests observe it.
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        RequestUris.Add(request.RequestUri);
        RequestPaths.Add(request.RequestUri.AbsolutePath);
        UserAgents.Add(request.Headers.TryGetValues("User-Agent", out var values) ? string.Join(" ", values) : null);

        if (_route != null)
        {
            return _route(request);
        }

        var responder = _responders.Count > 0 ? _responders.Dequeue() : _last;
        return await responder(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Responds 200 OK with the given JSON body.</summary>
    public static Func<CancellationToken, Task<HttpResponseMessage>> RespondJson(string json) =>
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    /// <summary>Responds with the given status code and no body.</summary>
    public static Func<CancellationToken, Task<HttpResponseMessage>> RespondStatus(HttpStatusCode statusCode) =>
        _ => Task.FromResult(new HttpResponseMessage(statusCode));

    /// <summary>Throws the given exception instead of responding (simulates a transport failure).</summary>
    public static Func<CancellationToken, Task<HttpResponseMessage>> Throw(Exception exception) =>
        _ => throw exception;

    /// <summary>
    /// Blocks until the request's (linked) token is cancelled, then surfaces the cancellation. Drives the
    /// real per-request timeout path deterministically regardless of machine speed.
    /// </summary>
    public static Func<CancellationToken, Task<HttpResponseMessage>> BlockUntilCancelled() =>
        async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
}
