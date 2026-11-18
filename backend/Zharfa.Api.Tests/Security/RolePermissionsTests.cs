namespace Zharfa.Api.Tests.Security;

using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Security;

/// <summary>
/// T007 (research R1, data-model §1.2): the four roles, the seven permissions, the role to permission sets and the
/// legacy role mapping. Pure: no database, no network.
/// </summary>
public class RolePermissionsTests
{
    private static readonly IReadOnlyDictionary<string, string[]> Expected = new Dictionary<string, string[]>
    {
        ["admin"] = [Permissions.Overview, Permissions.PurchasingReports, Permissions.PurchasingTransactions, Permissions.Admin],
        ["management"] =
        [
            Permissions.Overview, Permissions.PurchasingReports, Permissions.PurchasingTransactions,
            Permissions.VendorFinancials, Permissions.AiQuery, Permissions.Export,
        ],
        ["purchasing"] =
        [
            Permissions.Overview, Permissions.PurchasingReports, Permissions.PurchasingTransactions,
            Permissions.AiQuery, Permissions.Export,
        ],
        ["finance"] =
        [
            Permissions.Overview, Permissions.PurchasingReports, Permissions.PurchasingTransactions,
            Permissions.VendorFinancials, Permissions.AiQuery, Permissions.Export,
        ],
    };

    [Fact]
    public void Roles_All_is_exactly_the_four_roles_in_order()
    {
        Assert.Equal(new[] { "admin", "management", "purchasing", "finance" }, Roles.All);
    }

    [Fact]
    public void Permissions_All_is_exactly_the_seven_permissions_in_order()
    {
        Assert.Equal(
            new[]
            {
                "overview", "purchasing.reports", "purchasing.transactions", "vendor.financials",
                "admin", "ai.query", "export",
            },
            Permissions.All);
    }

    [Theory]
    [MemberData(nameof(RoleSets))]
    public void For_returns_exactly_the_data_model_set(string role, string[] permissions)
    {
        Assert.Equal(permissions.OrderBy(p => p, StringComparer.Ordinal), RolePermissions.For(role).OrderBy(p => p, StringComparer.Ordinal));
    }

    public static IEnumerable<object[]> RoleSets() => Expected.Select(pair => new object[] { pair.Key, pair.Value });

    [Theory]
    [InlineData("admin", "vendor.financials", false)]
    [InlineData("purchasing", "vendor.financials", false)]
    [InlineData("management", "vendor.financials", true)]
    [InlineData("finance", "vendor.financials", true)]
    [InlineData("admin", "ai.query", false)]
    [InlineData("admin", "export", false)]
    [InlineData("management", "admin", false)]
    [InlineData("admin", "admin", true)]
    public void Has_matches_the_matrix(string role, string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Has(role, permission));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_missing_role_has_no_permissions(string? role)
    {
        Assert.Empty(RolePermissions.For(role));
        Assert.False(RolePermissions.Has(role, Permissions.Overview));
    }

    [Theory]
    [InlineData("manager", "management")]
    [InlineData("ADMIN", "admin")]
    [InlineData("  Finance  ", "finance")]
    [InlineData("purchasing", "purchasing")]
    public void Normalize_maps_claims_to_role_ids(string raw, string expected)
    {
        Assert.Equal(expected, Roles.Normalize(raw));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("")]
    [InlineData(null)]
    public void Normalize_returns_null_for_unknown_or_missing_roles(string? raw)
    {
        Assert.Null(Roles.Normalize(raw));
    }

    [Fact]
    public void Every_permission_and_role_has_a_persian_label()
    {
        foreach (var permission in Permissions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(Permissions.Label(permission)), permission);
            Assert.NotEqual(permission, Permissions.Label(permission));
        }

        foreach (var role in Roles.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(Roles.Label(role)), role);
            Assert.NotEqual(role, Roles.Label(role));
        }
    }
}
