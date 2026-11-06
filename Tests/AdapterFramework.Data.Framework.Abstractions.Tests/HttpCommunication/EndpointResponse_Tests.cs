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
using System.Net;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using Xunit;

namespace AdapterFramework.Data.Framework.Abstractions.Tests.HttpCommunication;

public class EndpointResponse_Tests
{
    private static readonly Guid OperationId = Guid.Parse("6f1c2a7e-3b4d-4c5e-9f60-718293a4b5c6");

    /// <summary>Verifies that responses with the same values, including an equal receipt, are equal and share a hash code.</summary>
    [Fact]
    public void Equals_SameValues_AreEqual()
    {
        var left = new EndpointResponse(ResponseStatusEnum.Success, "ok")
        {
            StatusCode = HttpStatusCode.Accepted,
            Receipt = new OmfIngressReceipt(OperationId, 0, TimeSpan.FromSeconds(1)),
        };
        var right = new EndpointResponse(ResponseStatusEnum.Success, "ok")
        {
            StatusCode = HttpStatusCode.Accepted,
            Receipt = new OmfIngressReceipt(OperationId, 0, TimeSpan.FromSeconds(1)),
        };

        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    /// <summary>Verifies that a 202 response and a 200 response with otherwise equal values aren't equal.</summary>
    [Fact]
    public void Equals_DifferentStatusCode_AreNotEqual()
    {
        var accepted = new EndpointResponse(ResponseStatusEnum.Success) { StatusCode = HttpStatusCode.Accepted };
        var ok = new EndpointResponse(ResponseStatusEnum.Success) { StatusCode = HttpStatusCode.OK };

        Assert.True(accepted != ok);
    }

    /// <summary>Verifies that a response with a receipt and one without a receipt aren't equal.</summary>
    [Fact]
    public void Equals_DifferentReceipt_AreNotEqual()
    {
        var withReceipt = new EndpointResponse(ResponseStatusEnum.Success)
        {
            StatusCode = HttpStatusCode.Accepted,
            Receipt = new OmfIngressReceipt(OperationId, null, null),
        };
        var withoutReceipt = new EndpointResponse(ResponseStatusEnum.Success) { StatusCode = HttpStatusCode.Accepted };

        Assert.False(withReceipt.Equals(withoutReceipt));
        Assert.False(withReceipt.Equals((object)withoutReceipt));
    }
}
