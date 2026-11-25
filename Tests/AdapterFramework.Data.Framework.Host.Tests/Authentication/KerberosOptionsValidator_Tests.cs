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
using AdapterFramework.Data.Framework.Host.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Authentication;

public class KerberosOptionsValidator_Tests
{
    [Fact]
    public void Validate_MissingLocalGroupName_Fails()
    {
        var options = new AccessAdapterAuthorizationOptions();
        options.LocalGroup.Sid = "S-1-5-21-1";

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            OperatingSystem.IsWindows()
                ? "LocalGroup:Name is required"
                : "Operating system must be Windows",
            result.FailureMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MissingLocalGroupSid_Fails()
    {
        var options = new AccessAdapterAuthorizationOptions();
        options.LocalGroup.Name = "Adapter API Users";

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            OperatingSystem.IsWindows()
                ? "LocalGroup:Sid is required"
                : "Operating system must be Windows",
            result.FailureMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_InvalidLocalGroupSid_Fails()
    {
        var options = new AccessAdapterAuthorizationOptions();
        options.LocalGroup.Name = "Adapter API Users";
        options.LocalGroup.Sid = "not-a-sid";

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(
            OperatingSystem.IsWindows()
                ? "configured SID is invalid"
                : "Operating system must be Windows",
            result.FailureMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    private static ValidateOptionsResult Validate(AccessAdapterAuthorizationOptions options)
    {
        IValidateOptions<AccessAdapterAuthorizationOptions> validator =
            new KerberosOptionsStartupValidator(
                NullLogger.Instance,
                new ConfigurationBuilder().Build());

        return validator.Validate(Options.DefaultName, options);
    }
}
