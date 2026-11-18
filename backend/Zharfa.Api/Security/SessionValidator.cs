namespace Zharfa.Api.Security;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zharfa.Api.Data;

/// <summary>The user row a session is checked against on every request (research R3).</summary>
public sealed record SessionUser(long Id, string Username, string Role, bool IsActive, bool MustChangePassword);

/// <summary>What the cookie validator does with the current session: keep it, end it, or replace its claims.</summary>
public abstract record SessionDecision
{
    public static SessionDecision Keep { get; } = new KeepSession();

    public static SessionDecision Reject { get; } = new RejectSession();

    public static SessionDecision Replace(ClaimsPrincipal principal) => new ReplaceSession(principal);

    public sealed record KeepSession : SessionDecision;

    public sealed record RejectSession : SessionDecision;

    public sealed record ReplaceSession(ClaimsPrincipal Principal) : SessionDecision;
}

/// <summary>
/// Applies the role, the activity and the forced-change flag of the stored user to every request (FR-007, SC-003,
/// research R3). The cookie only proves the user id; authorization always uses the reloaded claims.
/// </summary>
public static class SessionValidator
{
    /// <summary>
    /// Pure decision. A cookie without a user id (a Demo 1 session) is rejected, so the user signs in once after the
    /// upgrade. A deleted or inactive user is rejected. A changed role, name or forced-change flag is replaced.
    /// </summary>
    public static SessionDecision Evaluate(ClaimsPrincipal current, SessionUser? user)
    {
        if (current.UserId() is null || user is null || !user.IsActive)
        {
            return SessionDecision.Reject;
        }

        var storedRole = Roles.Normalize(user.Role);
        if (storedRole is null)
        {
            return SessionDecision.Reject;
        }

        var claimedName = current.FindFirst(ClaimTypes.Name)?.Value;
        var unchanged =
            current.UserId() == user.Id &&
            string.Equals(claimedName, user.Username, StringComparison.Ordinal) &&
            string.Equals(current.RoleId(), storedRole, StringComparison.Ordinal) &&
            current.MustChangePassword() == user.MustChangePassword;

        return unchanged ? SessionDecision.Keep : SessionDecision.Replace(BuildPrincipal(user));
    }

    /// <summary>The cookie identity of a user (data-model §1.2). Used at sign-in, password change and replacement.</summary>
    public static ClaimsPrincipal BuildPrincipal(SessionUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, user.Username),
        };

        var role = Roles.Normalize(user.Role);
        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (user.MustChangePassword)
        {
            claims.Add(new Claim(ClaimsPrincipalExtensions.MustChangePasswordClaim, "1"));
        }

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Cookie event <c>OnValidatePrincipal</c>: loads the user by id (primary key, no tracking) and applies
    /// <see cref="Evaluate"/>. A rejected session is signed out; a replaced one is renewed with the new claims.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null)
        {
            return;
        }

        SessionUser? user = null;
        if (context.Principal.UserId() is { } userId)
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<ZharfaDbContext>();
            user = await db.AppUsers
                .AsNoTracking()
                .Where(row => row.UserId == userId)
                .Select(row => new SessionUser(row.UserId, row.Username, row.Role, row.IsActive, row.MustChangePassword))
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        }

        switch (Evaluate(context.Principal, user))
        {
            case SessionDecision.ReplaceSession replace:
                context.ReplacePrincipal(replace.Principal);
                context.ShouldRenew = true;
                break;

            case SessionDecision.RejectSession:
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                break;
        }
    }
}
