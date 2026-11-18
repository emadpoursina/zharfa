namespace Zharfa.Api.Platform;

using System.Collections.Generic;
using Zharfa.Api.Security;

/// <summary>
/// Which modules exist in this install for this caller (contracts/api.md §7, FR-021, FR-023). Pure: the data mode
/// and the caller's permission set decide everything. The client uses these flags only to draw navigation; the
/// server enforces the same rules on every endpoint.
/// </summary>
/// <param name="Overview">Purchasing overview: the <c>overview</c> permission.</param>
/// <param name="DataSync">Data sync page: the <c>admin</c> permission.</param>
/// <param name="DemoPages">Demo pages (sales, inventory alerts, receivables): demo installs with the <c>overview</c> permission.</param>
/// <param name="Ai">AI agent: demo installs with the <c>ai.query</c> permission (FR-023).</param>
/// <param name="PurchasingReports">Purchasing reports, vendor directory and comparison: the <c>purchasing.reports</c> permission.</param>
/// <param name="Users">Users and roles page: the <c>admin</c> permission.</param>
/// <param name="AuditLog">Audit log page: the <c>admin</c> permission.</param>
/// <param name="DataQuality">Data quality page: the <c>admin</c> permission.</param>
public sealed record SystemModules(
    bool Overview,
    bool DataSync,
    bool DemoPages,
    bool Ai,
    bool PurchasingReports,
    bool Users,
    bool AuditLog,
    bool DataQuality)
{
    public static SystemModules For(DataModeOptions mode, IReadOnlySet<string> permissions)
    {
        var isAdmin = permissions.Contains(Permissions.Admin);

        return new(
            Overview: permissions.Contains(Permissions.Overview),
            DataSync: isAdmin,
            DemoPages: mode.IsDemo && permissions.Contains(Permissions.Overview),
            Ai: mode.IsDemo && permissions.Contains(Permissions.AiQuery),
            PurchasingReports: permissions.Contains(Permissions.PurchasingReports),
            Users: isAdmin,
            AuditLog: isAdmin,
            DataQuality: isAdmin);
    }
}
