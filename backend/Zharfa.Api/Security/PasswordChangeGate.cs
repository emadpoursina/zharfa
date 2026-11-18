namespace Zharfa.Api.Security;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Blocks every API call of a session that must change its password first (research R6, FR-034). Only the
/// profile read, the change itself and sign-out pass; non-API paths (the SPA files) always pass. Runs after
/// authentication and before authorization. A refusal is not an access decision, so it writes no audit entry.
/// </summary>
public sealed class PasswordChangeGate(RequestDelegate next)
{
    public const string Message = "ابتدا باید گذرواژهٔ خود را تغییر دهید.";
    public const string Code = "password-change-required";

    private static readonly (string Method, string Path)[] AllowedWhileChangeRequired =
    [
        ("GET", "/api/auth/me"),
        ("POST", "/api/auth/change-password"),
        ("POST", "/api/auth/logout"),
    ];

    /// <summary>Pure: true when the request may run while a password change is still required.</summary>
    public static bool IsAllowed(string method, PathString path)
    {
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var (allowedMethod, allowedPath) in AllowedWhileChangeRequired)
        {
            if (string.Equals(method, allowedMethod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(path.Value, allowedPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var refused = context.User.Identity?.IsAuthenticated == true &&
                      context.User.MustChangePassword() &&
                      !IsAllowed(context.Request.Method, context.Request.Path);

        if (!refused)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = Message, code = Code }));
    }
}
