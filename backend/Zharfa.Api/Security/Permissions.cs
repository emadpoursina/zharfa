namespace Zharfa.Api.Security;

using System.Collections.Generic;

/// <summary>
/// Permission ids (research R1, data-model §1.2). Held in code: the role to permission sets are fixed, not editable.
/// Every endpoint policy is named after one of these ids (contracts/api.md §0).
/// </summary>
public static class Permissions
{
    public const string Overview = "overview";
    public const string PurchasingReports = "purchasing.reports";
    public const string PurchasingTransactions = "purchasing.transactions";
    public const string VendorFinancials = "vendor.financials";
    public const string Admin = "admin";
    public const string AiQuery = "ai.query";
    public const string Export = "export";

    /// <summary>All seven permission ids, in the contract order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Overview,
        PurchasingReports,
        PurchasingTransactions,
        VendorFinancials,
        Admin,
        AiQuery,
        Export,
    ];

    /// <summary>Persian label of a permission id (research R1). Unknown ids return the id itself.</summary>
    public static string Label(string id) => id switch
    {
        Overview => "نمای کلی خرید",
        PurchasingReports => "تأمین‌کنندگان، گزارش‌های خرید و مقایسه",
        PurchasingTransactions => "جزئیات اسناد و تراکنش‌ها",
        VendorFinancials => "اطلاعات مالی تأمین‌کنندگان",
        Admin => "مدیریت سامانه (کاربران، همگام‌سازی، کیفیت داده، گزارش رویدادها، تطبیق)",
        AiQuery => "پرسش از دستیار هوشمند",
        Export => "خروجی اکسل",
        _ => id,
    };
}
