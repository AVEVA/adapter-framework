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
using System.Net.Sockets;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Common.Helpers;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Helpers;

public class SystemInformationResolver_Tests
{
    [Fact]
    public void SystemInformationResolver_GetMachineName_Test()
    {
        var resolvedMachineName = SystemInformationResolver.GetMachineName();

        string actualMachineName;
        try
        {
            actualMachineName = Environment.MachineName;
            Assert.Equal(actualMachineName, resolvedMachineName);
        }
        catch (InvalidOperationException)
        {
            try
            {
                actualMachineName = Dns.GetHostName();
                Assert.Equal(actualMachineName, resolvedMachineName);
            }
            catch (SocketException)
            {
                Assert.Equal(EdgeSystemConstants.UnknownMachine, resolvedMachineName);
            }
        }
    }
}
