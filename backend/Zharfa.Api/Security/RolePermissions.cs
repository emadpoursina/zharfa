namespace Zharfa.Api.Security;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The single source of role to permission sets (research R1, data-model §1.2). The API, the session
/// claims, <c>/api/auth/me</c> and <c>/api/system/status</c> all read it; nothing else lists these pairs.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlySet<string> NoPermissions = FrozenSet<string>.Empty;

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Sets =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [Roles.Admin] = Set(
                Permissions.Overview,
                Permissions.PurchasingReports,
                Permissions.PurchasingTransactions,
                Permissions.Admin),
            [Roles.Management] = Set(
                Permissions.Overview,
                Permissions.PurchasingReports,
                Permissions.PurchasingTransactions,
                Permissions.VendorFinancials,
                Permissions.AiQuery,
                Permissions.Export),
            [Roles.Purchasing] = Set(
                Permissions.Overview,
                Permissions.PurchasingReports,
                Permissions.PurchasingTransactions,
                Permissions.AiQuery,
                Permissions.Export),
            [Roles.Finance] = Set(
                Permissions.Overview,
                Permissions.PurchasingReports,
                Permissions.PurchasingTransactions,
                Permissions.VendorFinancials,
                Permissions.AiQuery,
                Permissions.Export),
        };

    /// <summary>The permission set of a role. Unknown, missing or legacy-unmappable roles return an empty set.</summary>
    public static IReadOnlySet<string> For(string? role)
    {
        var id = Roles.Normalize(role);
        return id is not null && Sets.TryGetValue(id, out var permissions)
            ? permissions
            : NoPermissions;
    }

    /// <summary>True when the role holds the permission.</summary>
    public static bool Has(string? role, string permission) => For(role).Contains(permission);

    private static IReadOnlySet<string> Set(params string[] permissions) =>
        permissions.ToFrozenSet(StringComparer.Ordinal);
}
