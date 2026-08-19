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
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Common.Security;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests;

public class AccessTokenManager_Tests : IDisposable
{
    private const string Url = "http://localhost:5565/";
    private const string BadUrl = "https://localhost:5592";
    private const string ClientId = TestEndpoint.ClientId;
    private const string Secret = TestEndpoint.ClientSecret;
    private const string TokenRawUrl = TestEndpoint.Token;

    private readonly TestEndpoint _endpoint;
    private bool _disposed;

    public AccessTokenManager_Tests()
    {
        _endpoint = new TestEndpoint(Url);

        _endpoint.StartListening();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AccessTokenManager_GetAccessToken_EmptyOrNullUrlReturnsNull(string unifiedRecordLocatior)
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);

        Assert.Null(await manager.GetAccessTokenAsync(unifiedRecordLocatior, null, ClientId, protectedSecret, true, CancellationToken.None));
    }

    [Fact]
    public async Task AccessTokenManager_GetAccessToken_BadUrlWritesLog()
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);
        Assert.Null(await manager.GetAccessTokenAsync(BadUrl, null, ClientId, protectedSecret, true, CancellationToken.None));
        Assert.NotEmpty(logger.GetLogMessages());
    }

    [Fact]
    public async Task AccessTokenManager_GetAccessToken_BadUrl_OperationCanceled()
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await manager.GetAccessTokenAsync(BadUrl, null, ClientId, protectedSecret, true, cancellationTokenSource.Token);
        Assert.Empty(logger.GetLogMessages());
    }

    [Theory]
    [InlineData("BadClientId", Secret)]
    [InlineData(ClientId, "BadSecret")]
    [InlineData("BadClientId", "BadSecret")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public async Task AccessTokenManager_GetAccessToken_BadRequestWritesLog(string clientId, string secret)
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out _);

        Assert.Null(await manager.GetAccessTokenAsync(Url, null, clientId, secret, true, CancellationToken.None));
        Assert.NotEmpty(logger.GetLogMessages());
    }

    [Theory]
    [InlineData(Url, null, true)]
    [InlineData(Url, null, false)]
    [InlineData("badUrl", Url + TokenRawUrl, true)]
    [InlineData("veryBadUrl", Url + TokenRawUrl, false)]
    public async Task AccessTokenManager_GetAccessToken_ValidParametersReturnsToken(string unifiedRecordLocator, string tokenEndpoint, bool validateEndPointCertificate)
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);

        Assert.NotNull(await manager.GetAccessTokenAsync(unifiedRecordLocator, tokenEndpoint, ClientId, protectedSecret, validateEndPointCertificate, CancellationToken.None));
        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.Equal(2, logger.GetLogMessages().Count);
        Assert.Equal(LogLevel.Debug, logger.GetLogMessages()[0].LogLevel);
        Assert.Equal(LogLevel.Debug, logger.GetLogMessages()[1].LogLevel);
    }

    [Theory]
    [InlineData(Url, null)]
    [InlineData("badUrl", Url + TokenRawUrl)]
    public async Task AccessTokenManager_GetAccessToken_WithClient_ValidParametersReturnsToken(string unifiedRecordLocator, string tokenEndpoint)
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);

        using var client = new HttpClient();

        Assert.NotNull(await manager.GetAccessTokenAsync(client, unifiedRecordLocator, tokenEndpoint, ClientId, protectedSecret, CancellationToken.None));
        Assert.False(logger.AreErrorsWarningsInLog());
        Assert.Equal(2, logger.GetLogMessages().Count);
        Assert.Equal(LogLevel.Debug, logger.GetLogMessages()[0].LogLevel);
        Assert.Equal(LogLevel.Debug, logger.GetLogMessages()[1].LogLevel);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("", null)]
    [InlineData(null, "")]
    public async Task AccessTokenManager_GetAccessToken_InvalidParametersReturnsNull(string unifiedRecordLocator, string tokenEndpoint)
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);

        Assert.Null(await manager.GetAccessTokenAsync(unifiedRecordLocator, tokenEndpoint, ClientId, protectedSecret, true, CancellationToken.None));
        Assert.NotEmpty(logger.GetLogMessages());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("", null)]
    [InlineData(null, "")]
    public async Task AccessTokenManager_GetAccessToken_WithClient_InvalidParametersReturnsNull(string unifiedRecordLocator, string tokenEndpoint)
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);

        using var client = new HttpClient();

        Assert.Null(await manager.GetAccessTokenAsync(client, unifiedRecordLocator, tokenEndpoint, ClientId, protectedSecret, CancellationToken.None));
        Assert.NotEmpty(logger.GetLogMessages());
    }

    [Fact]
    public async Task AccessTokenManager_GetAccessToken_WithClient_NullClientThrows()
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await manager.GetAccessTokenAsync(null, Url, TokenRawUrl, ClientId, Secret, CancellationToken.None));
    }

    [Fact]
    public async Task AccessTokenManager_GetAccessToken_CachingTest()
    {
        static async Task GetAccessTokenMultipleTimesAsync(AccessTokenManager manager, string protectedSecret)
        {
            for (int i = 0; i < 10; i++)
            {
                Assert.NotNull(await manager.GetAccessTokenAsync(Url, null, ClientId, protectedSecret, true, CancellationToken.None));
            }
        }

        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out var dataProtector);
        var protectedSecret = dataProtector.Protect(Secret);

        await GetAccessTokenMultipleTimesAsync(manager, protectedSecret);

        Assert.Equal(1, _endpoint.OpenIdRouteGetCount);
        Assert.Null(await manager.GetAccessTokenAsync(BadUrl, null, ClientId, protectedSecret, true, CancellationToken.None));

        await GetAccessTokenMultipleTimesAsync(manager, protectedSecret);

        Assert.Equal(2, _endpoint.OpenIdRouteGetCount);
    }

    [Fact]
    public void AccessTokenManager_ShouldRefreshOcsAccessToken_DefaultFalse()
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out _);
        Assert.False(manager.AccessTokenRequiresRefresh());
    }

    [Fact]
    public void AccessTokenManager_ShouldRefreshAccessToken_OutdatedReturnsTrue()
    {
        var logger = new TestLogger();
        var manager = CreateAccessTokenManager(logger, out _);
        var expiryField = typeof(AccessTokenManager).GetField("_accessTokenExpiry", BindingFlags.Instance | BindingFlags.NonPublic);
        expiryField?.SetValue(manager, DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(1)));

        Assert.True(manager.AccessTokenRequiresRefresh());
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _endpoint.StopListening();
            _endpoint.Dispose();
        }

        _disposed = true;
    }

    private static AccessTokenManager CreateAccessTokenManager(ILogger logger, out SecretsManager dataProtector)
    {
        dataProtector = TestUtilities.CreateSecretsManagerInstance(null, logger, null);
        var manager = new AccessTokenManager(logger, dataProtector);

        return manager;
    }
}
