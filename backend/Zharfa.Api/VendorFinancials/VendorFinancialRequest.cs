namespace Zharfa.Api.VendorFinancials;

using System;
using System.Collections.Generic;
using System.Globalization;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing.Reports;

/// <summary>One validated financial request (contracts/api.md §1, §3, §4). Pure: no database.</summary>
public sealed record VendorFinancialRequest(
    JalaliPeriod Period,
    bool Compare,
    string? Search,
    string NormalizedSearch,
    int Page,
    int PageSize,
    string Sort,
    bool Descending);

/// <summary>Which surface is parsing: the sort allowlist and the compare rule differ (research R2).</summary>
public enum VendorFinancialSurface
{
    /// <summary>The Financials list / detail header: compare allowed (list + purchases vs payments only).</summary>
    Summary,

    /// <summary>Payment history: compare refused with compare-not-allowed (T036).</summary>
    History,

    /// <summary>Outstanding instruments: compare refused with compare-not-allowed (T036).</summary>
    Outstanding,
}

/// <summary>Parse failure of one financial request: a Persian message plus an optional machine code.</summary>
public sealed record VendorFinancialRequestError(string Message, string? Code = null);

/// <summary>
/// Validates the financial query (contracts/api.md §1: `kind`/`month`/`quarter`/`year`/`from`/`to`, `q`, `page`,
/// `pageSize`, `sort`, `dir`, `compare`). The period keys mirror the Phase 2 directory but use `kind` (a
/// `period` alias is accepted too); `compare` takes 1/0 or true/false — truthy compare on the document lists
/// is refused with <c>compare-not-allowed</c> (T036). Sort keys come from <see cref="VendorFinancialSorts"/>;
/// an unknown sort or direction is a 400, never a silent fallback.
/// </summary>
public static class VendorFinancialRequestParser
{
    public static readonly int[] AllowedPageSizes = { 25, 50, 100 };
    public const int DefaultPageSize = 25;
    public const int MaxSearchLength = 100;

    /// <summary>Sort keys that default to descending; everything else defaults to ascending.</summary>
    private static readonly string[] DefaultDescSorts = ["vendorBalance", "outstandingPayments", "paidAt", "date", "amount"];

    public static bool TryParse(
        IReadOnlyDictionary<string, string?> query,
        VendorFinancialSurface surface,
        JalaliDate today,
        out VendorFinancialRequest? request,
        out VendorFinancialRequestError? error)
    {
        ArgumentNullException.ThrowIfNull(query);
        request = null;
        error = null;

        var kind = Value(query, "kind") ?? Value(query, "period");
        var value = kind?.ToLowerInvariant() switch
        {
            null or "" or "month" => Value(query, "month"),
            "quarter" => Value(query, "quarter"),
            "year" => Value(query, "year"),
            _ => null,
        };

        if (!JalaliPeriod.TryParse(kind, value, Value(query, "from"), Value(query, "to"), today, out var period, out var periodError))
        {
            error = new VendorFinancialRequestError(periodError ?? VendorFinancialPeriod.PeriodErrorFallback);
            return false;
        }

        if (TryCompare(query, surface, out var compare, out var compareMessage, out var compareCode))
        {
            error = new VendorFinancialRequestError(compareMessage!, compareCode);
            return false;
        }

        string? search = null;
        var normalized = string.Empty;
        if (Value(query, "q") is { } qText)
        {
            if (qText.Length > MaxSearchLength)
            {
                error = new VendorFinancialRequestError("متن جستجو نمی‌تواند بیش از ۱۰۰ نویسه باشد.");
                return false;
            }

            search = qText.Length == 0 ? null : qText;
            normalized = VendorFinancialPeriod.SearchKey(qText);
        }

        var page = 1;
        if (Value(query, "page") is { } pageText)
        {
            if (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1)
            {
                error = new VendorFinancialRequestError("شماره صفحه باید عدد صحیح و بزرگ‌تر یا برابر ۱ باشد.");
                return false;
            }
        }

        var pageSize = DefaultPageSize;
        if (Value(query, "pageSize") is { } sizeText)
        {
            if (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                Array.IndexOf(AllowedPageSizes, parsed) < 0)
            {
                error = new VendorFinancialRequestError("تعداد ردیف هر صفحه باید ۲۵، ۵۰ یا ۱۰۰ باشد.");
                return false;
            }

            pageSize = parsed;
        }

        var sorts = surface switch
        {
            VendorFinancialSurface.Summary => VendorFinancialSorts.Summary,
            VendorFinancialSurface.History => VendorFinancialSorts.History,
            _ => VendorFinancialSorts.Outstanding,
        };
        var defaultSort = surface switch
        {
            VendorFinancialSurface.Summary => "vendorBalance",
            VendorFinancialSurface.History => "paidAt",
            _ => "date",
        };

        string sort;
        var descending = true;
        var requested = Value(query, "sort");
        if (requested is null)
        {
            sort = defaultSort;
            descending = DefaultDescSorts.Contains(sort);
        }
        else
        {
            if (Array.IndexOf(sorts, requested) < 0)
            {
                error = new VendorFinancialRequestError("ترتیب انتخاب‌شده برای این گزارش معتبر نیست.");
                return false;
            }

            sort = requested;
            descending = DefaultDescSorts.Contains(sort);
        }

        if (Value(query, "dir") is { } dir)
        {
            if (dir is not ("asc" or "desc"))
            {
                error = new VendorFinancialRequestError("جهت مرتب‌سازی باید صعودی یا نزولی باشد.");
                return false;
            }

            descending = dir == "desc";
        }

        request = new VendorFinancialRequest(period!, compare, search, normalized, page, pageSize, sort, descending);
        return true;
    }

    /// <summary>True when the value could not be understood (400); truthy compare on the document lists is its own 400.</summary>
    private static bool TryCompare(
        IReadOnlyDictionary<string, string?> query,
        VendorFinancialSurface surface,
        out bool compare,
        out string? message,
        out string? code)
    {
        compare = false;
        message = null;
        code = null;

        var text = Value(query, "compare");
        if (text is null)
        {
            return false;
        }

        if (text is "1" or "true")
        {
            if (surface is VendorFinancialSurface.History or VendorFinancialSurface.Outstanding)
            {
                message = surface == VendorFinancialSurface.History
                    ? "مقایسه دوره برای تاریخچه پرداخت در دسترس نیست."
                    : "مقایسه دوره برای فهرست پرداخت‌های معوق در دسترس نیست.";
                code = "compare-not-allowed";
                return true;
            }

            compare = true;
            return false;
        }

        if (text is "0" or "false")
        {
            return false;
        }

        message = "مقدار مقایسه باید 1 یا 0 باشد.";
        return true;
    }

    private static string? Value(IReadOnlyDictionary<string, string?> query, string name)
    {
        if (!query.TryGetValue(name, out var raw))
        {
            return null;
        }

        var trimmed = raw?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
