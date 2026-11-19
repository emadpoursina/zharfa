namespace Zharfa.Api.Audit;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Report keys used by <c>report.opened</c>/<c>report.printed</c> and by the data-quality catalog, with their
/// Persian names (data-model §2.2).
/// </summary>
public static class ReportKeys
{
    public const string Overview = "overview";
    public const string Vendors = "vendors";
    public const string VendorProfile = "vendor-profile";
    public const string ByVendor = "by-vendor";
    public const string ByProduct = "by-product";
    public const string ByCategory = "by-category";
    public const string Trend = "trend";
    public const string TopProducts = "top-products";
    public const string Returns = "returns";
    public const string Transactions = "transactions";
    public const string VendorsCompare = "vendors-compare";
    public const string DrillDown = "drill-down";
    public const string DrillDownLines = "drill-down-lines";
    public const string Generated = "generated";

    private static readonly (string Key, string Name)[] Entries =
    [
        (Overview, "نمای کلی خرید"),
        (Vendors, "فهرست و نمایهٔ تأمین‌کنندگان"),
        (VendorProfile, "نمایهٔ تأمین‌کننده"),
        (ByVendor, "خرید به تفکیک تأمین‌کننده"),
        (ByProduct, "خرید به تفکیک کالا"),
        (ByCategory, "خرید به تفکیک گروه کالا"),
        (Trend, "روند ماهانه"),
        (TopProducts, "پرخریدترین کالاها"),
        (Returns, "برگشت از خرید"),
        (Transactions, "تاریخچهٔ تراکنش‌ها"),
        (VendorsCompare, "مقایسهٔ تأمین‌کنندگان"),
        (DrillDown, "جزئیات اسناد"),
        (DrillDownLines, "اقلام سند"),
        (Generated, "گزارش تولیدشده"),
    ];

    /// <summary>The thirteen report keys in the data-model order.</summary>
    public static IReadOnlyList<string> All { get; } = Entries.Select(entry => entry.Key).ToArray();

    /// <summary>Persian name of a report key. Unknown keys return the key itself.</summary>
    public static string Name(string key)
    {
        foreach (var entry in Entries)
        {
            if (entry.Key == key)
            {
                return entry.Name;
            }
        }

        return key;
    }
}
