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
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading.Tasks;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using Xunit;

namespace AdapterFramework.Data.Framework.Messages.Tests.Awaitable;

/// <summary>
/// Metrics published on the <see cref="OmfAwaitableCoordinator.MeterName"/> meter.
/// </summary>
public sealed class OmfAwaitableMetrics_Tests
{
    /// <summary>
    /// Creating, completing, waiting on, and disposing a scope publishes the active-scope, outcome, and wait-duration instruments.
    /// </summary>
    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The scope is disposed with await using.")]
    public async Task Scope_Lifecycle_PublishesMetrics()
    {
        var measurements = new ConcurrentBag<(string Instrument, double Value, string[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == OmfAwaitableCoordinator.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => measurements.Add((instrument.Name, value, tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}").ToArray())));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => measurements.Add((instrument.Name, value, tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}").ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => measurements.Add((instrument.Name, value, tags.ToArray().Select(tag => $"{tag.Key}={tag.Value}").ToArray())));
        listener.Start();

        using var coordinator = new OmfAwaitableCoordinator();
        coordinator.SetOmfVersion(OmfVersion.Omf20);
        Assert.True(coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out var scope));
        await using (scope)
        {
            var result = await scope.WaitForAcceptanceAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(OmfAcceptanceOutcome.Filtered, result.Outcome);
        }

        Assert.Contains(measurements, m => m.Instrument == "omf.awaitable.scopes.active" && m.Value == 1);
        Assert.Contains(measurements, m => m.Instrument == "omf.awaitable.scopes.active" && m.Value == -1);
        Assert.Contains(measurements, m => m.Instrument == "omf.awaitable.scope.outcomes" && m.Tags.Contains("outcome=Filtered") && m.Tags.Contains("reason=None"));
        Assert.Contains(measurements, m => m.Instrument == "omf.awaitable.wait.duration" && m.Tags.Contains("result=Filtered"));
    }
}
