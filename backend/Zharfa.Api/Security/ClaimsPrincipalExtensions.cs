namespace Zharfa.Api.Security;

using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;

/// <summary>
/// Helpers that read the session claims (data-model §1.2): user id, normalized role, the role's permission set
/// and the forced password-change flag. Unauthenticated principals have no id, no role and no permissions.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Claim set at sign-in when the account must change its password first (research R6).</summary>
    public const string MustChangePasswordClaim = "zharfa:mcp";

    /// <summary>The signed-in user's id (<c>ClaimTypes.NameIdentifier</c>), or null.</summary>
    public static long? UserId(this ClaimsPrincipal user)
    {
        if (!IsSignedIn(user))
        {
            return null;
        }

        var raw = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
    }

    /// <summary>The normalized role id (<c>manager</c> is read as <c>management</c>), or null.</summary>
    public static string? RoleId(this ClaimsPrincipal user)
    {
        if (!IsSignedIn(user))
        {
            return null;
        }

        return Roles.Normalize(user.FindFirst(ClaimTypes.Role)?.Value);
    }

    /// <summary>The permission set of the signed-in role; empty when not signed in.</summary>
    public static IReadOnlySet<string> Permissions(this ClaimsPrincipal user) =>
        RolePermissions.For(user.RoleId());

    /// <summary>True when the signed-in role holds the permission.</summary>
    public static bool HasPermission(this ClaimsPrincipal user, string permission) =>
        IsSignedIn(user) && user.Permissions().Contains(permission);

    /// <summary>True when the session must change its password before anything else.</summary>
    public static bool MustChangePassword(this ClaimsPrincipal user) =>
        IsSignedIn(user) && user.FindFirst(MustChangePasswordClaim)?.Value == "1";

    private static bool IsSignedIn(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true;
}
