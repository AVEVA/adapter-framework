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
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Security;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests;

public class AdapterCommonConfigServices_Tests
{
    [Fact]
    public void CheckCreatedAdapterCommonConfigServices()
    {
        // Create mock provider and protector
        var provider = new Mock<IComponentConfigurationProvider>();
        var protector = new Mock<IEdgeDataProtector>();

        // Create a new service with mock provider and protector
        var service = new AdapterCommonConfigService(provider.Object, protector.Object);

        // Make sure creation of the service was successful
        Assert.NotNull(service);

        // Read back values to confirm they match what was written
        Assert.Equal(provider.Object, service.ConfigurationProvider);
        Assert.Equal(protector.Object, service.DataProtector);

        // Overwrite old instances with new instances
        provider = new Mock<IComponentConfigurationProvider>();
        protector = new Mock<IEdgeDataProtector>();

        // Make sure they do not match
        Assert.NotEqual(provider.Object, service.ConfigurationProvider);
        Assert.NotEqual(protector.Object, service.DataProtector);
    }
}
