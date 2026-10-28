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
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Host.Helpers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Helpers;

public sealed class Services_Tests : IDisposable
{
    private readonly string _applicationDataDirectory;

    public Services_Tests()
    {
        _applicationDataDirectory = Path.Combine(Path.GetTempPath(), $"AdapterFramework.Host.Tests.{Guid.NewGuid()}");
    }


    [Fact]
    public void GetComponentImplementations_LegacyCandidate_EdgeAdapterImplementationReturned()
    {
        var logger = new Mock<ILogger>().Object;
        var configurationProvider = new JsonConfigurationProvider(_applicationDataDirectory);

        var implementations = Services.GetComponentImplementations<IEdgeAdapter>(
            configurationProvider,
            "Not.A.Matching.Prefix",
            logger)
            .ToList();

        Assert.Contains(typeof(TestEdgeAdapter), implementations);
    }

    [Fact]
    public void GetComponentImplementations_LegacyCandidate_EdgeServiceImplementationReturned()
    {
        var logger = new Mock<ILogger>().Object;
        var configurationProvider = new JsonConfigurationProvider(_applicationDataDirectory);

        var implementations = Services.GetComponentImplementations<IEdgeService>(
            configurationProvider,
            "Not.A.Matching.Prefix",
            logger)
            .ToList();

        Assert.Contains(typeof(TestEdgeService), implementations);
    }

    [Fact]
    public void GetComponentImplementations_LegacyCandidate_FailoverServiceProviderImplementationReturned()
    {
        var logger = new Mock<ILogger>().Object;
        var configurationProvider = new JsonConfigurationProvider(_applicationDataDirectory);

        var implementations = Services.GetComponentImplementations<IFailoverServiceProvider>(
            configurationProvider,
            "Not.A.Matching.Prefix",
            logger)
            .ToList();

        Assert.Contains(typeof(TestFailoverServiceProvider), implementations);
    }

    [Fact]
    public void GetComponentImplementations_NonLegacyCandidate_NoMatchingPrefix_ReturnsEmpty()
    {
        var logger = new Mock<ILogger>().Object;
        var configurationProvider = new JsonConfigurationProvider(_applicationDataDirectory);

        var implementations = Services.GetComponentImplementations<ISinkProvider>(
            configurationProvider,
            "Not.A.Matching.Prefix",
            logger)
            .ToList();

        Assert.Empty(implementations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_applicationDataDirectory))
        {
            Directory.Delete(_applicationDataDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
