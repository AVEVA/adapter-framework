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
using System.Collections.Generic;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using Xunit;

namespace AdapterFramework.Data.Framework.Messages.Tests.Awaitable;

/// <summary>
/// Tests how <see cref="ScopeSidecar"/> counts the scoped items of a slice of a grouped message.
/// </summary>
public class ScopeSidecar_Tests
{
    /// <summary>
    /// Verifies that a window of stream values counts only the part of each scope range inside the window, as when a stream's values
    /// are split across bodies.
    /// </summary>
    [Fact]
    public void AddValues_WindowCutsRanges_CountsOnlyValuesInsideWindow()
    {
        var a = new TestScope();
        var b = new TestScope();
        var sidecar = new InstanceScopeSidecar([a, b], [null, [new ScopeRange(0, 0, 4), new ScopeRange(1, 6, 4)]], null, null, null);
        var membership = new Dictionary<ScopeToken, int>();

        sidecar.AddValues(1, 2, 6, membership);

        Assert.Equal(2, membership[a]);
        Assert.Equal(2, membership[b]);
    }

    /// <summary>
    /// Verifies that counting whole streams sums every scoped value of each stream and skips streams without scoped values.
    /// </summary>
    [Fact]
    public void AddEntries_StreamingData_CountsEveryScopedValueOfEachStream()
    {
        var a = new TestScope();
        var sidecar = new InstanceScopeSidecar([a], [[new ScopeRange(0, 1, 3)], null, [new ScopeRange(0, 0, 2)]], null, null, null);
        var membership = new Dictionary<ScopeToken, int>();

        sidecar.AddEntries(ScopeSidecarKind.StreamingData, 0, 3, membership);

        Assert.Equal(5, membership[a]);
    }

    /// <summary>
    /// Verifies that counting entries skips unscoped entries (index -1) and entries outside the requested run.
    /// </summary>
    [Fact]
    public void AddEntries_Types_SkipsUnscopedAndOutOfRunEntries()
    {
        var a = new TestScope();
        var b = new TestScope();
        var sidecar = new SchemaScopeSidecar([a, b], [0, -1, 1, 1, 0], null, null);
        var membership = new Dictionary<ScopeToken, int>();

        sidecar.AddEntries(ScopeSidecarKind.Types, 1, 3, membership);
        sidecar.AddEntries(ScopeSidecarKind.Containers, 0, 5, membership);

        Assert.False(membership.ContainsKey(a));
        Assert.Equal(2, membership[b]);
    }

    private sealed class TestScope : ScopeToken
    {
    }
}
