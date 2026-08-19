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
using AdapterFramework.Data.Framework.Host.Utilities;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Utilities;

public class BetaTimeout_Tests
{
    [Fact]
    public void BetaTimeout_Timeout_Expires_NotInNext14days_Test()
    {
        var betaExpireDate = BetaTimeout.GetTimeoutDate();
        var nowPlus14Days = DateTime.Now.AddDays(14);

        // BetaTimeout has not expired
        Assert.False(BetaTimeout.HasExpiredAndLogTimeoutMessage(null, null));

        // expiry datetime is later than current datetime +14 days
        Assert.True(DateTime.Compare(betaExpireDate, nowPlus14Days) > 0);
    }

    [Fact]
    public void BetaTimeout_Timeout_OverrideUsed_Test()
    {
        var customExpirationDate = DateTime.Today.AddDays(-1);

        // BetaTimeout has expired
        Assert.True(BetaTimeout.HasExpiredAndLogTimeoutMessage(customExpirationDate));

        // BetaTimeout has not expired
        customExpirationDate = DateTime.Today.AddDays(1);
        Assert.False(BetaTimeout.HasExpiredAndLogTimeoutMessage(customExpirationDate));
    }
}
