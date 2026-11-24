using System;
using System.Security.Claims;
using System.Security.Principal;
using AdapterFramework.Data.Framework.Host.Authentication;
using Microsoft.Extensions.Options;

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