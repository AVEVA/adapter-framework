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
using System.Collections.Generic;
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.ConfigurationProvider.Commands.Helpers;
using Xunit;

namespace AdapterFramework.Data.Framework.ConfigurationProvider.Tests.Commands.Helpers;

public class ConfigurationCommandHelper_Tests
{
    [Fact]
    public void GetProtectedProperties_Success()
    {
        var expectedPropertyNames = new List<string> { "Secret", "Secret2" };

        var protectedAttributes = ConfigurationCommandHelper.GetProtectedProperties(typeof(IdAndProtected));

        var protectedAttributesList = protectedAttributes.ToList();

        Assert.Equal(2, protectedAttributesList.Count);
        Assert.Contains(protectedAttributesList[0].Name, expectedPropertyNames);
        Assert.Contains(protectedAttributesList[1].Name, expectedPropertyNames);
    }

    [Fact]
    public void GetProtectedProperties_NotDefined()
    {
        var protectedAttributes = ConfigurationCommandHelper.GetProtectedProperties(typeof(NoIdOrProtected));

        Assert.Empty(protectedAttributes);
    }

    private class NoIdOrProtected
    {
        public int Index { get; set; }
        public string Secret { get; set; }
    }

    private class IdAndProtected
    {
        [Id]
        public int Index { get; set; }
        [Protected]
        public string Secret { get; set; }
        [Protected]
        public string Secret2 { get; set; }
    }
}
