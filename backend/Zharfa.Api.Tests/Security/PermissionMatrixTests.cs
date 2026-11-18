namespace Zharfa.Api.Tests.Security;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Zharfa.Api.Security;

/// <summary>
/// T055 (SC-002, research R2, contracts/api.md §0): every controller action that answers an HTTP verb must carry the
/// policy the contract names. Reflection over the API assembly; a new endpoint without a policy, or with a different
/// one, fails this suite. Pure: no server, no database.
/// </summary>
public class PermissionMatrixTests
{
    private const string SignedIn = "signed-in";
    private const string Anonymous = "anonymous";

    /// <summary>
    /// Expected effective requirements per action, keyed <c>Controller.Action</c>, mirroring contracts/api.md §0.
    /// <c>signed-in</c> is a plain <c>[Authorize]</c>; <c>anonymous</c> is <c>[AllowAnonymous]</c>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["AuthController.Login"] = [Anonymous],
        ["AuthController.Logout"] = [Anonymous],
        ["AuthController.Me"] = [SignedIn],
        ["AuthController.ChangePassword"] = [SignedIn],
        ["SystemStatusController.GetStatus"] = [SignedIn],

        ["OverviewController.Purchasing"] = [Permissions.Overview],
        ["InventoryController.GetAlerts"] = [Permissions.Overview],
        ["MetaController.GetCategories"] = [Permissions.Overview],
        ["ReceivablesController.GetSummary"] = [Permissions.Overview],
        ["ReceivablesController.GetRows"] = [Permissions.Overview],
        ["SalesController.GetOverview"] = [Permissions.Overview],
        ["SalesController.GetLines"] = [Permissions.Overview],

        ["PurchasingReportsController.Report"] = [Permissions.PurchasingReports],
        ["PurchasingReportsController.ReportPrint"] = [Permissions.PurchasingReports],
        ["PurchasingReportsController.Documents"] = [Permissions.PurchasingReports, Permissions.PurchasingTransactions],
        ["PurchasingReportsController.DocumentsPrint"] = [Permissions.PurchasingReports, Permissions.PurchasingTransactions],
        ["PurchasingReportsController.DocumentLines"] = [Permissions.PurchasingReports, Permissions.PurchasingTransactions],
        ["PurchasingReportsController.Lookup"] = [Permissions.PurchasingReports],
        ["PurchasingReportsController.Vendors"] = [Permissions.PurchasingReports],
        ["PurchasingReportsController.VendorsPrint"] = [Permissions.PurchasingReports],
        ["PurchasingReportsController.VendorProfile"] = [Permissions.PurchasingReports],
        ["PurchasingReportsController.VendorCompare"] = [Permissions.PurchasingReports],
        ["PurchasingReportsController.VendorComparePrint"] = [Permissions.PurchasingReports],

        ["VendorFinancialsController.Summary"] = [Permissions.VendorFinancials],
        ["VendorFinancialsController.Vendor"] = [Permissions.VendorFinancials],

        ["AiController.Chat"] = [Permissions.AiQuery],

        ["AdminController.GetProviderStatus"] = [Permissions.Admin],
        ["AdminController.GetReconciliation"] = [Permissions.Admin],
        ["SyncAdminController.Run"] = [Permissions.Admin],
        ["SyncAdminController.Jobs"] = [Permissions.Admin],
        ["SyncAdminController.Job"] = [Permissions.Admin],
        ["UsersAdminController.GetRoles"] = [Permissions.Admin],
        ["UsersAdminController.ListUsers"] = [Permissions.Admin],
        ["UsersAdminController.CreateUser"] = [Permissions.Admin],
        ["UsersAdminController.ChangeRole"] = [Permissions.Admin],
        ["UsersAdminController.ResetPassword"] = [Permissions.Admin],
        ["UsersAdminController.Deactivate"] = [Permissions.Admin],
        ["UsersAdminController.Reactivate"] = [Permissions.Admin],
        ["UsersAdminController.Unlock"] = [Permissions.Admin],
        ["AuditLogController.Actions"] = [Permissions.Admin],
        ["AuditLogController.List"] = [Permissions.Admin],
        ["DataQualityController.Get"] = [Permissions.Admin],
    };

    public static IEnumerable<object[]> RoleCases() => Roles.All.Select(role => new object[] { role });

    [Fact]
    public void Every_endpoint_carries_exactly_the_policy_the_contract_names()
    {
        var actual = ActualEndpoints();
        var expected = Expected.ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var missing = expected.Keys.Except(actual.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var unexpected = actual.Keys.Except(expected.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToList();
        var mismatched = expected.Keys
            .Intersect(actual.Keys, StringComparer.Ordinal)
            .Where(key => !expected[key].SetEquals(actual[key]))
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => $"{key}: expected [{string.Join(", ", expected[key].OrderBy(p => p, StringComparer.Ordinal))}] " +
                           $"actual [{string.Join(", ", actual[key].OrderBy(p => p, StringComparer.Ordinal))}]")
            .ToList();

        Assert.Empty(missing.Select(key => "missing endpoint: " + key));
        Assert.Empty(unexpected.Select(key => "endpoint without a contract entry: " + key));
        Assert.Empty(mismatched);
    }

    [Fact]
    public void No_endpoint_is_left_without_a_requirement()
    {
        var unprotected = ActualEndpoints()
            .Where(pair => pair.Value.Count == 0 || pair.Value.Any(requirement => requirement.StartsWith("roles:", StringComparison.Ordinal)))
            .Select(pair => pair.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unprotected);
    }

    [Fact]
    public void Purchasing_may_call_no_action_that_needs_admin_or_vendor_financials()
    {
        var reachable = ReachableBy(Roles.Purchasing)
            .Where(pair => pair.Value.Contains(Permissions.Admin) || pair.Value.Contains(Permissions.VendorFinancials))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(reachable);
    }

    [Fact]
    public void Admin_may_call_no_vendor_financials_action()
    {
        var reachable = ReachableBy(Roles.Admin)
            .Where(pair => pair.Value.Contains(Permissions.VendorFinancials))
            .Select(pair => pair.Key)
            .ToList();

        Assert.Empty(reachable);
    }

    [Theory]
    [MemberData(nameof(RoleCases))]
    public void Every_role_reaches_at_least_the_signed_in_profile_endpoints(string role)
    {
        var reachable = ReachableBy(role);

        Assert.True(reachable.ContainsKey("AuthController.Me"));
        Assert.True(reachable.ContainsKey("SystemStatusController.GetStatus"));
    }

    /// <summary>The endpoints a role may call: every requirement is <c>signed-in</c>, <c>anonymous</c> or held by the role.</summary>
    private static Dictionary<string, HashSet<string>> ReachableBy(string role)
    {
        return ActualEndpoints()
            .Where(pair => pair.Value.All(requirement =>
                requirement is SignedIn or Anonymous || RolePermissions.Has(role, requirement)))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Every public action with an HTTP verb attribute, keyed <c>Controller.Action</c>, with its effective requirements.</summary>
    private static Dictionary<string, HashSet<string>> ActualEndpoints()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var controllers = typeof(Zharfa.Api.Controllers.AuthController).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsPublic && typeof(ControllerBase).IsAssignableFrom(type));

        foreach (var controller in controllers)
        {
            var classAuthorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
            var classAllowsAnonymous = controller.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
                {
                    continue;
                }

                var requirements = new HashSet<string>(StringComparer.Ordinal);
                if (classAllowsAnonymous || method.IsDefined(typeof(AllowAnonymousAttribute), inherit: true))
                {
                    requirements.Add(Anonymous);
                }

                var authorize = classAuthorize.Concat(method.GetCustomAttributes<AuthorizeAttribute>(inherit: true));
                foreach (var attribute in authorize)
                {
                    requirements.Add(attribute.Roles is null ? attribute.Policy ?? SignedIn : "roles:" + attribute.Roles);
                }

                result[$"{controller.Name}.{method.Name}"] = requirements;
            }
        }

        return result;
    }
}
