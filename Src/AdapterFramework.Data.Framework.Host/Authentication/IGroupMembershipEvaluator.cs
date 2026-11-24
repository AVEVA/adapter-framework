using System.Security.Claims;

internal interface IGroupMembershipEvaluator
{
    bool IsMember(ClaimsPrincipal principal);
}