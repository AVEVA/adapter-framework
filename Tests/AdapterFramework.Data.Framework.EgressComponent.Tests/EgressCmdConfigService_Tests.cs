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
using AdapterFramework.Data.Framework.Abstractions.Constants;
using Xunit;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests;

public class EgressCmdConfigService_Tests
{
    [Fact]
    public void EgressCmdConfigService_GetLoggingHelpOutput()
    {
        var componentId = "EgressTestId";
        var egressCmdConfigService = new EgressCmdConfigService(componentId);

        var loggingHelp = egressCmdConfigService.GetLoggingHelpOutput();

        Assert.NotEmpty(loggingHelp);
        Assert.Contains(componentId, loggingHelp, StringComparison.InvariantCulture);
        Assert.Contains(EdgeSystemConstants.LoggingFacetName, loggingHelp, StringComparison.InvariantCulture);
    }

    [Fact]
    public void EgressCmdConfigService_GetEgressEndpointsHelpOutput()
    {
        var componentId = "EgressTestId";
        var egressCmdConfigService = new EgressCmdConfigService(componentId);

        var endpointHelp = egressCmdConfigService.GetEgressEndpointsHelpOutput();

        Assert.NotEmpty(endpointHelp);
        Assert.Contains(componentId, endpointHelp, StringComparison.InvariantCulture);
        Assert.Contains(EdgeSystemConstants.DataEndpointsFacetName, endpointHelp, StringComparison.InvariantCulture);
    }
}
