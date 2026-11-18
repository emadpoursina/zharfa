namespace Zharfa.Api.Controllers;

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Security;

/// <summary>
/// Sign-in, sign-out, the signed-in profile and the password change (contracts/api.md §1). Sign-in looks the user up
/// by <c>username_key</c>, applies the lockout rule (<see cref="SignInPolicy"/>) and audits every attempt. Passwords
/// are never logged, audited or returned.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    ZharfaDbContext db,
    IAuditLogger audit,
    IOptions<SyncOptions> syncOptions,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string InvalidCredentialsMessage = "نام کاربری یا گذرواژه اشتباه است.";
    private const string LockedMessage =
        "حساب شما به‌دلیل ورودهای ناموفق پیاپی به‌طور موقت قفل شده است. پس از ۱۵ دقیقه دوباره تلاش کنید یا با مدیر سامانه تماس بگیرید.";
    private const string WrongCurrentPasswordMessage = "گذرواژهٔ فعلی نادرست است.";
    private const string SameAsCurrentPasswordMessage = "گذرواژهٔ جدید باید با گذرواژهٔ فعلی متفاوت باشد.";
    private const string InvalidInputMessage = "داده‌های ورودی نامعتبر است.";
    private const string SignOutMessage = "خروج با موفقیت انجام شد.";
    private const int MaxAttemptedUsernameLength = 64;

    public record LoginRequest(string? Username, string? Password);

    public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var attempted = Cut(request.Username.Trim(), MaxAttemptedUsernameLength);
        var key = UsernameRules.Key(request.Username);
        var user = await db.AppUsers.FirstOrDefaultAsync(row => row.UsernameKey == key, cancellationToken);

        if (user is null)
        {
            await AuditFailedAsync(attempted, "unknown-user", null, cancellationToken);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        if (!user.IsActive)
        {
            await AuditFailedAsync(attempted, "inactive", user.UserId, cancellationToken);
            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        var decision = SignInPolicy.Decide(
            new SignInState(user.FailedSignins, user.LockedUntil),
            PasswordHasher.Verify(request.Password, user.PasswordHash),
            DateTime.UtcNow);

        user.FailedSignins = decision.NewFailed;
        user.LockedUntil = decision.NewLockedUntil;
        await db.SaveChangesAsync(cancellationToken);

        switch (decision.Outcome)
        {
            case SignInOutcome.Locked:
            {
                var lockedUntilJalali = user.LockedUntil is { } until ? UntilText(until) : null;
                await AuditFailedAsync(attempted, "locked", user.UserId, cancellationToken);
                if (decision.JustLocked)
                {
                    await audit.RecordAsync(
                        new AuditEntry(
                            AuditActions.AuthLocked,
                            $"user:{user.Username}",
                            AuditDetail.Of(("until", lockedUntilJalali)),
                            ActorOverride: user.Username,
                            ActorUserIdOverride: user.UserId),
                        cancellationToken);
                }

                logger.LogWarning("Sign-in refused: account temporarily locked");
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    message = LockedMessage,
                    code = "locked",
                    lockedUntilJalali,
                });
            }

            case SignInOutcome.BadCredentials:
                await AuditFailedAsync(attempted, "bad-password", user.UserId, cancellationToken);
                logger.LogWarning("Sign-in refused: wrong password");
                return Unauthorized(new { message = InvalidCredentialsMessage });

            default:
            {
                var principal = SessionValidator.BuildPrincipal(ToSessionUser(user));
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    SessionProperties());

                await audit.RecordAsync(
                    new AuditEntry(
                        AuditActions.SignInSucceeded,
                        $"user:{user.Username}",
                        ActorOverride: user.Username,
                        ActorUserIdOverride: user.UserId),
                    cancellationToken);

                logger.LogInformation("User signed in: '{Username}' (role: '{Role}')", RedactionHelper.Scrub(user.Username), user.Role);
                return Ok(MeOf(principal));
            }
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await audit.RecordAsync(new AuditEntry(AuditActions.SignOut, $"user:{User.Identity.Name}"), cancellationToken);
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = SignOutMessage });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me() => Ok(MeOf(User));

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest? request, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var user = userId is null
            ? null
            : await db.AppUsers.FirstOrDefaultAsync(row => row.UserId == userId.Value, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Unauthorized(new { message = AccessDenied.UnauthenticatedMessage });
        }

        if (request is null)
        {
            return BadRequest(new { message = InvalidInputMessage });
        }

        if (!PasswordHasher.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
        {
            return BadRequest(new { message = WrongCurrentPasswordMessage });
        }

        if (!PasswordRules.TryValidate(request.NewPassword, out var error))
        {
            return BadRequest(new { message = error });
        }

        if (PasswordHasher.Verify(request.NewPassword!, user.PasswordHash))
        {
            return BadRequest(new { message = SameAsCurrentPasswordMessage });
        }

        var forced = user.MustChangePassword;
        user.PasswordHash = PasswordHasher.Hash(request.NewPassword!);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            SessionValidator.BuildPrincipal(ToSessionUser(user)),
            SessionProperties());

        await audit.RecordAsync(
            new AuditEntry(AuditActions.PasswordChanged, $"user:{user.Username}", AuditDetail.Of(("forced", forced ? "true" : "false"))),
            cancellationToken);

        return NoContent();
    }

    /// <summary>The <c>Me</c> shape of contracts/api.md §1, read from a principal (the reloaded one on <c>me</c>).</summary>
    private static object MeOf(ClaimsPrincipal user)
    {
        var role = user.RoleId();
        var granted = user.Permissions();

        return new
        {
            id = user.UserId(),
            username = user.Identity?.Name ?? string.Empty,
            role,
            roleLabel = Roles.Label(role),
            permissions = Permissions.All.Where(permission => granted.Contains(permission)).ToArray(),
            mustChangePassword = user.MustChangePassword(),
        };
    }

    private static SessionUser ToSessionUser(AppUser user) =>
        new(user.UserId, user.Username, user.Role, user.IsActive, user.MustChangePassword);

    private static AuthenticationProperties SessionProperties() => new()
    {
        IsPersistent = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
    };

    /// <summary>
    /// One <c>auth.signin.failed</c> entry. The actor is the attempted username (never the password); the user id is
    /// set only when the username exists.
    /// </summary>
    private Task AuditFailedAsync(string attempted, string reason, long? userId, CancellationToken cancellationToken) =>
        audit.RecordAsync(
            new AuditEntry(
                AuditActions.SignInFailed,
                $"user:{attempted}",
                AuditDetail.Of(("reason", reason)),
                ActorOverride: attempted,
                ActorUserIdOverride: userId),
            cancellationToken);

    private string UntilText(DateTime untilUtc)
    {
        var zone = SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
        return SyncTimeText.Jalali(new DateTimeOffset(DateTime.SpecifyKind(untilUtc, DateTimeKind.Utc)), zone);
    }

    private static string Cut(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;
}
