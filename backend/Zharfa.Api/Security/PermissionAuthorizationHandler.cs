namespace Zharfa.Api.Security;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Succeeds when the caller is signed in and the role holds the required permission (research R1).
/// It never calls <c>Fail()</c>, so other handlers on the same policy can still succeed.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
