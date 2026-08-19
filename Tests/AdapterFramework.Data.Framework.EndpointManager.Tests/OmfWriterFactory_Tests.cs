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
using System.IO;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Serialization;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

public class OmfWriterFactory_Tests
{
    private const string UserName = "test";
    private const string Password = "test";

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    public void OmfWriterFactory_GetOmfWriterInstance(bool bufferingEnabled, int maxBufferSizeMb = 1024)
    {
        var mockDataProtector = new Mock<IEdgeDataProtector>();
        mockDataProtector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("123");

        var factory = new OmfWriterFactory(mockDataProtector.Object, new OmfJsonSerializer(), new JsonConfigurationProvider("UnitTests"), new ApplicationManifest());

        var omfEndpointConfig = new EndpointConfigurationBase
        {
            Id = Guid.NewGuid().ToString(),
            Endpoint = "https://localhost:5465/api/omf/",
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfig = new BufferingConfiguration { EnablePersistentBuffering = bufferingEnabled, MaxBufferSizeMB = maxBufferSizeMb };
        var logger = new TestLogger();
        using var omfWriter = factory.GetOmfWriterInstance(omfEndpointConfig, logger, OmfWriterType.Health, bufferingConfig, (x) => { });

        Assert.NotNull(omfWriter);
        Assert.Empty(logger.GetLogMessages());
    }

    [Theory]
    [InlineData(OmfWriterType.Health)]
    [InlineData(OmfWriterType.Data)]
    public void OmfWriterFactory_HttpDebugLog_Location(OmfWriterType omfWriterType)
    {
        var mockDataProtector = new Mock<IEdgeDataProtector>();
        mockDataProtector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns("123");

        var configurationProvider = new JsonConfigurationProvider("UnitTests");

        var factory = new OmfWriterFactory(
            mockDataProtector.Object,
            new OmfJsonSerializer(),
            configurationProvider,
            new ApplicationManifest());

        var omfEndpointConfig = new EndpointConfigurationBase
        {
            Id = Guid.NewGuid().ToString(),
            Endpoint = "https://localhost:5465/api/omf/",
            UserName = UserName,
            Password = Password,
        };

        var bufferingConfig = new BufferingConfiguration();
        var logger = new TestLogger();
        using var omfWriter = (OmfWriter)factory.GetOmfWriterInstance(omfEndpointConfig, logger, omfWriterType, bufferingConfig, (x) => { });

        Assert.NotNull(omfWriter);
        Assert.Empty(logger.GetLogMessages());

        var httpByteClient = (OmfByteHttpClient)TestUtilities.GetFieldValueFromObject("_client", omfWriter);
        var actualEgressDebugLogsPath = (string)TestUtilities.GetFieldValueFromObject("_debugLogsPath", httpByteClient);

        var expectedEgressDebugLogsPath = Path.Combine(
            configurationProvider.GetCommonApplicationDataDirectoryPath(),
            "Logs",
            "EgressDebugLogs",
            omfWriterType.ToString(),
            omfEndpointConfig.Id);

        Assert.Equal(expectedEgressDebugLogsPath, actualEgressDebugLogsPath);
    }
}
