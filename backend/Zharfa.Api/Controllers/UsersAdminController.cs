namespace Zharfa.Api.Controllers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zharfa.Api.Platform;
using Zharfa.Api.Security;
using Zharfa.Api.Users;

/// <summary>
/// Users and roles for the Admin (contracts/api.md §2, research R4): list, create, change role, reset password,
/// deactivate, reactivate and unlock. Every route requires the <c>admin</c> permission on the server. Passwords are
/// never echoed, and no response carries a hash or the failure counter.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Permissions.Admin)]
public sealed class UsersAdminController(UserAdminService users, DataModeOptions dataMode) : ControllerBase
{
    public sealed record CreateUserRequest(string? Username, string? Password, string? Role);

    public sealed record RoleRequest(string? Role);

    public sealed record PasswordRequest(string? Password);

    /// <summary>The four roles in contract order, each with its permission ids and Persian labels.</summary>
    [HttpGet("roles")]
    public IActionResult GetRoles() => Ok(new
    {
        roles = Roles.All.Select(role => new
        {
            id = role,
            label = Roles.Label(role),
            permissions = Permissions.All
                .Where(permission => RolePermissions.Has(role, permission))
                .Select(permission => new { id = permission, label = Permissions.Label(permission) })
                .ToArray(),
        }).ToArray(),
    });

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(CancellationToken cancellationToken) => Ok(new
    {
        dataLabel = dataMode.DataLabel,
        users = await users.ListAsync(cancellationToken),
    });

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest? request, CancellationToken cancellationToken)
    {
        var outcome = await users.CreateAsync(request?.Username, request?.Password, request?.Role, cancellationToken);
        return outcome is UserOperation.Succeeded created ? StatusCode(StatusCodes.Status201Created, created.User) : Respond(outcome);
    }

    [HttpPost("users/{id:long}/role")]
    public async Task<IActionResult> ChangeRole(long id, [FromBody] RoleRequest? request, CancellationToken cancellationToken) =>
        Respond(await users.ChangeRoleAsync(id, request?.Role, cancellationToken));

    [HttpPost("users/{id:long}/password")]
    public async Task<IActionResult> ResetPassword(long id, [FromBody] PasswordRequest? request, CancellationToken cancellationToken)
    {
        var outcome = await users.ResetPasswordAsync(id, request?.Password, cancellationToken);
        return outcome is UserOperation.Succeeded ? NoContent() : Respond(outcome);
    }

    [HttpPost("users/{id:long}/deactivate")]
    public async Task<IActionResult> Deactivate(long id, CancellationToken cancellationToken) =>
        Respond(await users.DeactivateAsync(id, cancellationToken));

    [HttpPost("users/{id:long}/reactivate")]
    public async Task<IActionResult> Reactivate(long id, CancellationToken cancellationToken) =>
        Respond(await users.ReactivateAsync(id, cancellationToken));

    [HttpPost("users/{id:long}/unlock")]
    public async Task<IActionResult> Unlock(long id, CancellationToken cancellationToken) =>
        Respond(await users.UnlockAsync(id, cancellationToken));

    private IActionResult Respond(UserOperation outcome) => outcome switch
    {
        UserOperation.Succeeded succeeded => Ok(succeeded.User),
        UserOperation.NotFound => NotFound(new { message = UserAdminService.NotFoundMessage }),
        UserOperation.Invalid invalid => BadRequest(new { message = invalid.Message }),
        UserOperation.Conflict conflict => Conflict(new { message = conflict.Message, code = conflict.Code }),
        _ => throw new InvalidOperationException("Unknown user operation outcome."),
    };
}
