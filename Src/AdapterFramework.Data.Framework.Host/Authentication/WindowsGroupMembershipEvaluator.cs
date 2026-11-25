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