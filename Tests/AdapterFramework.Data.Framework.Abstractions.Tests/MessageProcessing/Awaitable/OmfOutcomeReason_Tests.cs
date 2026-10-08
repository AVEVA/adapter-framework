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

namespace AdapterFramework.Data.Framework.Abstractions.Tests.MessageProcessing.Awaitable;

public class OmfOutcomeReason_Tests
{
    public static TheoryData<OmfReasonCode> AllReasonCodes()
    {
        var data = new TheoryData<OmfReasonCode>();
        foreach (var code in Enum.GetValues<OmfReasonCode>())
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllReasonCodes))]
    public void GetStage_EveryReasonCode_HasAStage(OmfReasonCode code)
    {
        Assert.True(Enum.IsDefined(code.GetStage()));
    }

    [Theory]
    [InlineData(OmfReasonCode.PostRejected, OmfPipelineStage.Admission)]
    [InlineData(OmfReasonCode.AwaitingFlush, OmfPipelineStage.Grouping)]
    [InlineData(OmfReasonCode.CountMismatch, OmfPipelineStage.Serialization)]
    [InlineData(OmfReasonCode.NoEndpoints, OmfPipelineStage.Dispatch)]
    [InlineData(OmfReasonCode.BufferFull, OmfPipelineStage.Buffering)]
    [InlineData(OmfReasonCode.NonAcceptedSuccess, OmfPipelineStage.Delivery)]
    [InlineData(OmfReasonCode.BufferDeleted, OmfPipelineStage.Failover)]
    public void Stage_IsDerivedFromCode(OmfReasonCode code, OmfPipelineStage expectedStage)
    {
        Assert.Equal(expectedStage, new OmfOutcomeReason(code, string.Empty).Stage);
    }

    [Fact]
    public void GetStage_UnknownCode_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((OmfReasonCode)999).GetStage());
    }

    [Fact]
    public void Message_IsTruncatedAndNeverNull()
    {
        var longReason = new OmfOutcomeReason(OmfReasonCode.RejectedByEndpoint, new string('x', OmfOutcomeReason.MaxMessageLength + 10));
        var nullReason = new OmfOutcomeReason(OmfReasonCode.Queued, null);

        Assert.Equal(OmfOutcomeReason.MaxMessageLength, longReason.Message.Length);
        Assert.Equal(string.Empty, nullReason.Message);
    }
}
