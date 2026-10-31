namespace Zharfa.Api.Controllers;

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Data;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly ZharfaDbContext _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(ZharfaDbContext db, ILogger<AuthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    public record LoginRequest(string? Username, string? Password);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized(new { message = "نام کاربری یا گذرواژه اشتباه است." });
        }

        var trimmedUsername = request.Username.Trim();
        var user = await _db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == trimmedUsername && u.IsActive);

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Authentication failed for username '{Username}'", RedactionHelper.Scrub(trimmedUsername));
            return Unauthorized(new { message = "نام کاربری یا گذرواژه اشتباه است." });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("name", user.Username),
            new("role", user.Role)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);

        _logger.LogInformation("User logged in successfully: '{Username}' (role: '{Role}')", RedactionHelper.Scrub(user.Username), user.Role);

        return Ok(new
        {
            username = user.Username,
            role = user.Role
        });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { message = "خروج با موفقیت انجام شد." });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var username = User.Identity?.Name ?? User.FindFirst("name")?.Value ?? string.Empty;
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;

        return Ok(new
        {
            username,
            role
        });
    }
}
