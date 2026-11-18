namespace Zharfa.Api.Tests.Security;

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Zharfa.Api.Security;

/// <summary>
/// T034 (FR-007, SC-003, research R3): the role, activity and forced-change flag of the stored user apply on the
/// next request. Pure: <see cref="SessionValidator.Evaluate"/> only.
/// </summary>
public class SessionValidatorTests
{
    private static ClaimsPrincipal Cookie(string role, bool mustChange = false, long id = 7, string name = "purchasing")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, role),
        };
        if (mustChange)
        {
            claims.Add(new Claim(ClaimsPrincipalExtensions.MustChangePasswordClaim, "1"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private static SessionUser User(
        string role = "purchasing",
        bool active = true,
        bool mustChange = false,
        long id = 7,
        string name = "purchasing") =>
        new(id, name, role, active, mustChange);

    [Fact]
    public void A_cookie_without_a_user_id_from_demo_one_is_rejected()
    {
        var legacy = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, "manager"), new Claim(ClaimTypes.Role, "manager") },
            CookieAuthenticationDefaults.AuthenticationScheme));

        Assert.IsType<SessionDecision.RejectSession>(SessionValidator.Evaluate(legacy, User("management")));
    }

    [Fact]
    public void A_deleted_user_is_rejected()
    {
        Assert.IsType<SessionDecision.RejectSession>(SessionValidator.Evaluate(Cookie("purchasing"), null));
    }

    [Fact]
    public void A_deactivated_user_is_rejected()
    {
        Assert.IsType<SessionDecision.RejectSession>(SessionValidator.Evaluate(Cookie("purchasing"), User(active: false)));
    }

    [Fact]
    public void An_unchanged_session_is_kept()
    {
        Assert.IsType<SessionDecision.KeepSession>(SessionValidator.Evaluate(Cookie("purchasing"), User("purchasing")));
    }

    [Fact]
    public void A_role_changed_in_the_database_replaces_the_session_on_the_next_request()
    {
        var decision = SessionValidator.Evaluate(Cookie("purchasing"), User("finance"));

        var replaced = Assert.IsType<SessionDecision.ReplaceSession>(decision).Principal;
        Assert.Equal("finance", replaced.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal("7", replaced.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("purchasing", replaced.Identity?.Name);
        Assert.True(replaced.HasPermission("vendor.financials"));
    }

    [Fact]
    public void The_replaced_identity_uses_the_cookie_scheme()
    {
        var decision = SessionValidator.Evaluate(Cookie("purchasing"), User("management"));

        var replaced = Assert.IsType<SessionDecision.ReplaceSession>(decision).Principal;
        Assert.Equal(CookieAuthenticationDefaults.AuthenticationScheme, replaced.Identity?.AuthenticationType);
    }

    [Fact]
    public void A_forced_change_that_appears_replaces_the_session_with_the_flag()
    {
        var decision = SessionValidator.Evaluate(Cookie("purchasing"), User("purchasing", mustChange: true));

        var replaced = Assert.IsType<SessionDecision.ReplaceSession>(decision).Principal;
        Assert.True(replaced.MustChangePassword());
    }

    [Fact]
    public void A_cleared_forced_change_replaces_the_session_without_the_flag()
    {
        var decision = SessionValidator.Evaluate(Cookie("purchasing", mustChange: true), User("purchasing"));

        var replaced = Assert.IsType<SessionDecision.ReplaceSession>(decision).Principal;
        Assert.False(replaced.MustChangePassword());
    }

    [Fact]
    public void A_user_whose_stored_role_is_unknown_is_rejected()
    {
        Assert.IsType<SessionDecision.RejectSession>(SessionValidator.Evaluate(Cookie("purchasing"), User("director")));
    }
}
