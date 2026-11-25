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
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.Extensions.Options;

namespace AdapterFramework.Data.Framework.Host.Authentication;

internal class WindowsGroupMembershipEvaluator : IGroupMembershipEvaluator
{
    private readonly IOptions<AccessAdapterAuthorizationOptions> _options;

    public WindowsGroupMembershipEvaluator(
        IOptions<AccessAdapterAuthorizationOptions> options)
    {
        _options = options;
    }

    public bool IsMember(ClaimsPrincipal principal)
    {
        if (!OperatingSystem.IsWindows() ||
            principal.Identity is not WindowsIdentity windowsIdentity)
        {
            return false;
        }

        var localGroupSid = new SecurityIdentifier(_options.Value.LocalGroup.Sid);
        return new WindowsPrincipal(windowsIdentity).IsInRole(localGroupSid);
    }
}
