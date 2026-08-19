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

namespace AdapterFramework.Data.Framework.CancellationTokenService.Tests;

public class CancellationTokenProvider_Tests : IDisposable
{
    private const string TestString = "Test";
    private readonly CancellationTokenProvider _ctProvider;
    
    private bool _disposed;

    public CancellationTokenProvider_Tests()
    {
        _ctProvider = new CancellationTokenProvider();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void CancellationTokenService_GetTokenNullParamThrows(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() => _ctProvider.GetCancellationToken(input));
    }

    [Fact]
    public void CancellationTokenService_CancelNonExistentToken()
    {
        Assert.Throws<InvalidOperationException>(() => _ctProvider.RequestCancellation(TestString));
        _ctProvider.GetCancellationToken(TestString);
    }

    [Fact]
    public void CancellationTokenService_CancelTokenNullParamThrows()
    {
        Assert.Throws<ArgumentNullException>(() => _ctProvider.RequestCancellation(null));
    }

    [Fact]
    public void CancellationTokenService_GetDuplicateToken()
    {
        var cancellationToken1 = _ctProvider.GetCancellationToken(TestString);
        var cancellationToken2 = _ctProvider.GetCancellationToken(TestString);
        Assert.Equal(cancellationToken1, cancellationToken2);
    }

    [Fact]
    public void CancellationTokenService_TokenCanceled()
    {
        var cancellationToken = _ctProvider.GetCancellationToken(TestString);
        Assert.False(cancellationToken.IsCancellationRequested);

        _ctProvider.RequestCancellation(TestString);
        Assert.True(cancellationToken.IsCancellationRequested);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _ctProvider.Dispose();
        }

        _disposed = true;
    }
}
