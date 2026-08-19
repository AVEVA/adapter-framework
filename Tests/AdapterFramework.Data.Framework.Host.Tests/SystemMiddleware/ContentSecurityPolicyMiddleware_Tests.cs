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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Moq;
using AdapterFramework.Data.Framework.Host.SystemMiddleware;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.SystemMiddleware;

public class ContentSecurityPolicyMiddleware_Tests
{
    [Fact]
    public async Task ContentSecurityPolicyMiddleware_Test()
    {
        const string ExpectedKey = "Content-Security-Policy";
        const string ExpectedValue = "default-src 'self';object-src 'none'";
        var nextDelegateExecuted = false;
        IHeaderDictionary headers = new HeaderDictionary();

        var contentSecurityPolicyMiddleware = new ContentSecurityPolicyMiddleware(context =>
        {
            nextDelegateExecuted = true;
            return Task.CompletedTask;
        });

        var mockContext = new Mock<HttpContext>();
        var mockResponse = new Mock<HttpResponse>();
        mockResponse.Setup(response => response.Headers).Returns(headers);
        mockContext.Setup(ctx => ctx.Response).Returns(mockResponse.Object);

        await contentSecurityPolicyMiddleware.Invoke(mockContext.Object);

        Assert.True(nextDelegateExecuted);
        Assert.NotEmpty(headers);
        Assert.Equal(ExpectedValue, headers[ExpectedKey]);
    }
}
