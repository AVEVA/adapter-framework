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
using Xunit;

namespace AdapterFramework.Data.Framework.Extensions.Tests;

public class ThrowHelper_Tests
{
    [Fact]
    public void ThrowIfArgumentNull_Null()
    {
        string requiredId = null;
        var ex = Assert.Throws<ArgumentNullException>(() =>
            ThrowHelper.ThrowIfArgumentNull(requiredId, nameof(requiredId)));

        Assert.Contains(nameof(requiredId), ex.Message, StringComparison.InvariantCulture);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SampleId")]
    public void ThrowIfArgumentNull_ValidInput(string requiredId)
    {
        ThrowHelper.ThrowIfArgumentNull(requiredId, nameof(requiredId));
    }

    [Fact]
    public void ThrowIfArgumentNullOrEmpty_Invalid()
    {
        string requiredId = null;
        var ex = Assert.Throws<ArgumentNullException>(() =>
            ThrowHelper.ThrowIfArgumentNullOrEmpty(requiredId, nameof(requiredId)));

        Assert.Contains(nameof(requiredId), ex.Message, StringComparison.InvariantCulture);

        requiredId = string.Empty;

        var aor = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ThrowHelper.ThrowIfArgumentNullOrEmpty(requiredId, nameof(requiredId)));

        Assert.Contains(nameof(requiredId), aor.Message, StringComparison.InvariantCulture);
    }

    [Theory]
    [InlineData("SampleId")]
    [InlineData("  ")]
    public void ThrowIfArgumentNullOrEmpty_Valid(string requiredId)
    {
        ThrowHelper.ThrowIfArgumentNullOrEmpty(requiredId, nameof(requiredId));
    }

    [Fact]
    public void ThrowIfArgumentNullEmptyOrWhiteSpace_Invalid()
    {
        string requiredId = null;
        var ex = Assert.Throws<ArgumentNullException>(() =>
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(requiredId, nameof(requiredId)));

        Assert.Contains(nameof(requiredId), ex.Message, StringComparison.InvariantCulture);

        requiredId = string.Empty;

        var aor = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(requiredId, nameof(requiredId)));

        Assert.Contains(nameof(requiredId), aor.Message, StringComparison.InvariantCulture);

        requiredId = "   ";

        var ae = Assert.Throws<ArgumentException>(() =>
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(requiredId, nameof(requiredId)));

        Assert.Contains(nameof(requiredId), ae.Message, StringComparison.InvariantCulture);
    }

    [Theory]
    [InlineData("SampleId")]
    public void ThrowIfArgumentNullEmptyOrWhiteSpace_Valid(string requiredId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(requiredId, nameof(requiredId));
    }
}
