namespace Zharfa.Api.DataQuality;

using System;
using System.Collections.Generic;
using System.Linq;
using Zharfa.Api.Audit;

/// <summary>
/// One issue type of the data-quality catalog (data-model §3.3, research R13, contracts/api.md §4 and §9). The
/// <see cref="NotChecked"/> list holds the deferred sub-checks of the type (only inconsistent-amounts); the
/// <see cref="Heuristic"/> note marks a type whose Release 1 rule is a labelled heuristic (only inconsistent-product-ids).
/// </summary>
public sealed record IssueTypeInfo(
    string Id,
    string Label,
    string Description,
    IReadOnlyList<string> AffectedReports,
    IReadOnlyList<(string Code, string Reason)> NotChecked,
    string? Heuristic);

/// <summary>The seven issue types, their field labels, and which reports each one affects (single source on the server).</summary>
public static class DataQualityCatalog
{
    public const string MissingValues = "missing-values";
    public const string DuplicateRecords = "duplicate-records";
    public const string BrokenRelationships = "broken-relationships";
    public const string InconsistentProductIds = "inconsistent-product-ids";
    public const string InvalidDates = "invalid-dates";
    public const string UnexpectedQuantities = "unexpected-quantities";
    public const string InconsistentAmounts = "inconsistent-amounts";

    public const string LineNetMismatch = "line-net-mismatch";
    public const string DocumentTotalMismatch = "document-total-mismatch";

    /// <summary>S-DQ-NET: reason of the line-net-mismatch sub-check (contracts/api.md §9).</summary>
    public const string NetReason = "ستون مبلغ خالص ردیف در نقشهٔ منبع تأیید نشده است.";
    /// <summary>S-DQ-TOTAL: reason of the document-total-mismatch sub-check.</summary>
    public const string TotalReason = "ستون جمع سند در نقشهٔ منبع تأیید نشده است.";
    /// <summary>S-DQ-HEURISTIC: the note of inconsistent-product-ids.</summary>
    public const string HeuristicNote = "بررسی تقریبی: یک نام کالا با چند کد کالا. بررسی بر پایهٔ کد کالا پس از تأیید ستون‌های کد در محل فعال می‌شود.";

    /// <summary>The eleven report keys of research R13 ("all 11"): the directory covers the profile, the document list the lines.</summary>
    private static readonly IReadOnlyList<string> AllEleven =
    [
        ReportKeys.Overview,
        ReportKeys.Vendors,
        ReportKeys.ByVendor,
        ReportKeys.ByProduct,
        ReportKeys.ByCategory,
        ReportKeys.Trend,
        ReportKeys.TopProducts,
        ReportKeys.Returns,
        ReportKeys.Transactions,
        ReportKeys.VendorsCompare,
        ReportKeys.DrillDown,
    ];

    /// <summary>The seven types in the data-model order (§3.3).</summary>
    public static IReadOnlyList<IssueTypeInfo> IssueTypes { get; } =
    [
        new IssueTypeInfo(
            MissingValues,
            "مقادیر ناقص",
            "سند یا ردیف خرید بدون مقدار لازم (شماره سند، تأمین‌کننده، کالا یا مقدار).",
            AllEleven,
            [],
            null),
        new IssueTypeInfo(
            DuplicateRecords,
            "رکوردهای تکراری",
            "سندهای خرید هم‌نوع با شماره سند یکسان برای یک تأمین‌کننده (به‌جز اولین سند).",
            AllEleven,
            [],
            null),
        new IssueTypeInfo(
            BrokenRelationships,
            "ارتباط‌های ناقص",
            "سند، ردیف یا کالایی که به تأمین‌کننده، کالا یا گروه کالای ثبت‌نشده در فهرست‌ها اشاره می‌کند.",
            [
                ReportKeys.Overview,
                ReportKeys.Vendors,
                ReportKeys.ByVendor,
                ReportKeys.ByProduct,
                ReportKeys.ByCategory,
                ReportKeys.TopProducts,
                ReportKeys.Returns,
                ReportKeys.Transactions,
                ReportKeys.VendorsCompare,
                ReportKeys.DrillDown,
            ],
            [],
            null),
        new IssueTypeInfo(
            InconsistentProductIds,
            "شناسه‌های ناسازگار کالا",
            "کالاهایی با نام یکسان اما شناسه‌های متفاوت (به‌جز اولین کالا).",
            [
                ReportKeys.Overview,
                ReportKeys.Vendors,
                ReportKeys.ByProduct,
                ReportKeys.ByCategory,
                ReportKeys.TopProducts,
                ReportKeys.Returns,
                ReportKeys.DrillDown,
            ],
            [],
            HeuristicNote),
        new IssueTypeInfo(
            InvalidDates,
            "تاریخ‌های نامعتبر",
            "سندهایی با تاریخ بعد از امروز یا پیش از تاریخ شروع مجاز.",
            AllEleven,
            [],
            null),
        new IssueTypeInfo(
            UnexpectedQuantities,
            "مقادیر غیرمنتظره",
            "ردیف‌هایی با مقدار صفر یا منفی.",
            [
                ReportKeys.Vendors,
                ReportKeys.ByVendor,
                ReportKeys.ByProduct,
                ReportKeys.ByCategory,
                ReportKeys.TopProducts,
                ReportKeys.Returns,
                ReportKeys.VendorsCompare,
                ReportKeys.DrillDown,
            ],
            [],
            null),
        new IssueTypeInfo(
            InconsistentAmounts,
            "مبالغ ناسازگار",
            "ردیف‌ها یا سندهایی با تخفیف یا مالیات ناسازگار با مبلغ.",
            AllEleven,
            [
                (LineNetMismatch, NetReason),
                (DocumentTotalMismatch, TotalReason),
            ],
            null),
    ];

    private static readonly IReadOnlyDictionary<string, string> FieldLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["number"] = "شماره سند",
        ["vendor"] = "تأمین‌کننده",
        ["product"] = "کالا",
        ["quantity"] = "مقدار",
        ["date"] = "تاریخ",
        ["discount"] = "تخفیف ردیف",
        ["vat"] = "مالیات بر ارزش افزوده",
        ["document-discount"] = "تخفیف سند",
        ["category"] = "گروه کالا",
        ["name"] = "نام کالا",
    };

    /// <summary>Persian label of a field code (data-model §3.3). An unknown code is returned as it is.</summary>
    public static string FieldLabel(string code) =>
        FieldLabels.TryGetValue(code, out var label) ? label : code;

    /// <summary>The info of one issue type, or null for an id outside the catalog.</summary>
    public static IssueTypeInfo? Find(string id) =>
        IssueTypes.FirstOrDefault(type => string.Equals(type.Id, id, StringComparison.Ordinal));

    public static bool IsIssueType(string id) => Find(id) is not null;

    /// <summary>
    /// The key the notice and the catalog use for a report: <c>vendor-profile</c> is covered by <c>vendors</c> and
    /// <c>drill-down-lines</c> by <c>drill-down</c>; every other key is its own.
    /// </summary>
    public static string NoticeKey(string reportKey) => reportKey switch
    {
        ReportKeys.VendorProfile => ReportKeys.Vendors,
        ReportKeys.DrillDownLines => ReportKeys.DrillDown,
        _ => reportKey,
    };

    /// <summary>The issue type ids whose affected reports contain the report (after <see cref="NoticeKey"/>).</summary>
    public static IReadOnlyList<string> TypesFor(string reportKey)
    {
        var key = NoticeKey(reportKey);
        return IssueTypes
            .Where(type => type.AffectedReports.Contains(key, StringComparer.Ordinal))
            .Select(type => type.Id)
            .ToArray();
    }
}
