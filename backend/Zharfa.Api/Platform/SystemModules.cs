namespace Zharfa.Api.Platform;

/// <summary>
/// Which modules exist in this install for this caller (contracts/api.md §1, FR-021, FR-023). Pure: the
/// data mode and the Admin role decide everything. The client uses these flags only to draw navigation;
/// the server enforces the same rules on every endpoint.
/// </summary>
/// <param name="Overview">Purchasing Overview: every signed-in user.</param>
/// <param name="DataSync">Data sync page: Admin only.</param>
/// <param name="DemoPages">Demo 1 pages (sales, inventory alerts, receivables): demo installs only.</param>
/// <param name="Ai">AI agent: demo installs only until Phase 4 adds AI over real data (FR-023).</param>
/// <param name="PurchasingReports">Purchasing reports, vendor directory and comparison: every signed-in user (navigation only; spec 004, contracts/api.md §8).</param>
public sealed record SystemModules(bool Overview, bool DataSync, bool DemoPages, bool Ai, bool PurchasingReports)
{
    public static SystemModules For(DataModeOptions mode, bool isAdmin) => new(
        Overview: true,
        DataSync: isAdmin,
        DemoPages: mode.IsDemo,
        Ai: mode.IsDemo,
        PurchasingReports: true);
}
