using System.Security.Claims;

namespace AdapterFramework.Data.Framework.Host.Authentication;

internal interface IGroupMembershipEvaluator
{
    bool IsMember(ClaimsPrincipal principal);
}