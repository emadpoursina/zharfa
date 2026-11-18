namespace Zharfa.Api.Tests.Security;

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Zharfa.Api.Security;

/// <summary>
/// T008 (research R1, R3): the permission handler succeeds exactly when the signed-in role holds the permission.
/// Pure: builds principals and runs the handler directly.
/// </summary>
public class PermissionAuthorizationHandlerTests
{
    public static IEnumerable<object[]> RoleAndPermission() =>
        from permission in Permissions.All
        from role in Roles.All
        select new object[] { role, permission };

    public static IEnumerable<object[]> Permission() =>
        Permissions.All.Select(permission => new object[] { permission });

    [Theory]
    [MemberData(nameof(RoleAndPermission))]
    public async Task Succeeds_exactly_when_the_role_holds_the_permission(string role, string permission)
    {
        var context = Context(Principal(role), permission);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.Equal(RolePermissions.Has(role, permission), context.HasSucceeded);
    }

    [Theory]
    [MemberData(nameof(Permission))]
    public async Task An_unauthenticated_principal_never_succeeds(string permission)
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "admin") }));
        var context = Context(anonymous, permission);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Theory]
    [MemberData(nameof(Permission))]
    public async Task A_legacy_manager_claim_is_treated_as_management(string permission)
    {
        var context = Context(Principal("manager"), permission);

        await new PermissionAuthorizationHandler().HandleAsync(context);

        Assert.Equal(RolePermissions.Has("management", permission), context.HasSucceeded);
    }

    private static ClaimsPrincipal Principal(string role) => new(new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, "tester"), new Claim(ClaimTypes.Role, role) },
        authenticationType: "test"));

    private static AuthorizationHandlerContext Context(ClaimsPrincipal principal, string permission) =>
        new(new[] { new PermissionRequirement(permission) }, principal, resource: null);
}
