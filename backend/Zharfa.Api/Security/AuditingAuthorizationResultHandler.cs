namespace Zharfa.Api.Security;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Replaces the default authorization result handler (research R2). A challenge answers 401 with no audit, a
/// forbid answers 403 and writes <c>access.denied</c> for the first missing permission, and only a success
/// calls the next middleware. Never calls <c>next</c> on 401 or 403.
/// </summary>
public sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private const string FallbackPermission = "authenticated";

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            return AccessDenied.RespondUnauthenticatedAsync(context);
        }

        if (authorizeResult.Forbidden)
        {
            var permission = authorizeResult.AuthorizationFailure?.FailedRequirements
                .OfType<PermissionRequirement>()
                .FirstOrDefault()?.Permission ?? FallbackPermission;

            return AccessDenied.RespondAsync(context, permission);
        }

        return next(context);
    }
}
