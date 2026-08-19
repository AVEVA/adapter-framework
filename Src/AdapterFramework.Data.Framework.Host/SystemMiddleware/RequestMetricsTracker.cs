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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.Host.SystemMiddleware;

public class RequestMetricsTracker : IRequestMetricsTracker
{
    private long _totalRequests;
    private long _requestsInFlight;

    public RequestMetricsTracker()
    {
    }

    public long TotalRequests => _totalRequests;

    public async Task AwaitCompletionOfFinalRequestsAsync(TimeSpan timeout, ILogger logger)
    {
        var abortTime = DateTime.UtcNow.Add(timeout);

        while (Interlocked.Read(ref _requestsInFlight) > 0)
        {
            await Task.Delay(100);

            if (timeout > TimeSpan.Zero && DateTime.UtcNow >= abortTime)
            {
                logger.LogCritical("Abandoning awaiting of completion of requests.  Integrity may be compromised.");
                break;
            }
        }
    }

    public void BeginRequest()
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _requestsInFlight);
    }

    public void EndRequest()
    {
        Interlocked.Decrement(ref _requestsInFlight);
    }
}
