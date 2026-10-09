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
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

/// <summary>
/// Parsing of Platform OMF 2xx response bodies into receipts.
/// </summary>
public class OmfIngressReceiptParser_Tests
{
    /// <summary>
    /// The platform's documented body parses into every receipt value.
    /// </summary>
    [Fact]
    public void Parse_PlatformBody_ReturnsEveryValue()
    {
        var operationId = Guid.NewGuid();

        var receipt = OmfIngressReceiptParser.Parse($"{{\"Operation-Id\":\"{operationId}\",\"LastProcessedOperationId\":null,\"ErrorCount\":3,\"QueueAge\":\"00:01:05\"}}");

        Assert.Equal(new OmfIngressReceipt(operationId, 3, TimeSpan.FromSeconds(65)), receipt);
    }

    /// <summary>
    /// Property names match case-insensitively, so camel-cased bodies parse too.
    /// </summary>
    [Fact]
    public void Parse_CamelCaseBody_ReturnsValues()
    {
        var receipt = OmfIngressReceiptParser.Parse("{\"operation-id\":\"not-a-guid\",\"errorCount\":0}");

        Assert.Equal(new OmfIngressReceipt(null, 0, null), receipt);
    }

    /// <summary>
    /// Bodies that aren't a JSON object with a receipt property give no receipt.
    /// </summary>
    /// <param name="content">The response body.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"Other\":1}")]
    public void Parse_NoReceipt_ReturnsNull(string content)
    {
        Assert.Null(OmfIngressReceiptParser.Parse(content));
    }
}
