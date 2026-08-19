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
using AdapterFramework.Data.Framework.EgressComponent.Services;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests.Services;

public class EgressComponentIdService_Tests
{
    [Fact]
    public void EgressComponentIdService_GetComponentId()
    {
        var expectedId = "TestId";
        var egressComponentIdService = new EgressComponentIdService(expectedId);

        Assert.Equal(expectedId, egressComponentIdService.ComponentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EgressComponentIdService_InvalidInput(string componentId)
    {
        var exceptionThrown = false;
        try
        {
            _ = new EgressComponentIdService(componentId);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }
}
