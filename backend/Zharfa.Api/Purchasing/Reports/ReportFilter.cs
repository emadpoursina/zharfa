namespace Zharfa.Api.Purchasing.Reports;

using System;
using System.Collections.Generic;
using System.Globalization;
using Zharfa.Api.Analytics;
using Zharfa.Api.Purchasing;

/// <summary>
/// A validated request for one report, list or print twin (data-model §4 "ReportFilter", contracts/api.md §0).
/// Every value is already checked against the catalog entry: only declared filters are set, the sort key is
/// from the report's allowlist, and free text is trimmed. Values are never placed into SQL text; the SQL
/// layer binds them as parameters.
/// </summary>
public sealed record ReportFilter(
    JalaliPeriod Period,
    bool Compare,
    string? VendorKey,
    string? ProductKey,
    string? CategoryKey,
    string? Month,
    string? DocType,
    string? Search,
    string? Match,
    string Sort,
    bool Descending,
    int Page,
    int PageSize,
    bool Print)
{
    /// <summary>Page sizes the screens offer (contracts/api.md §0).</summary>
    public static readonly int[] AllowedPageSizes = { 25, 50, 100 };

    public const int DefaultPageSize = 25;
    public const int MaxSearchLength = 100;
    public const string UnknownKey = "~unknown";

    /// <summary>Message for a filter the report does not declare (contracts/api.md §0).</summary>
    public const string NotApplicableMessage = "این فیلتر برای این گزارش معتبر نیست";

    /// <summary>
    /// Validates a request against <paramref name="report"/>. In print mode (<paramref name="print"/>) paging
    /// parameters are ignored: the page is 1 and the page size is the print limit (research R9).
    /// Returns false with a Persian message on the first violation.
    /// </summary>
    public static bool TryParse(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> query,
        JalaliDate today,
        bool print,
        out ReportFilter? filter,
        out string? error)
    {
        filter = null;
        error = null;

        if (!TryParsePeriod(query, today, out var period, out error))
        {
            return false;
        }

        var periodKind = Value(query, "period")?.ToLowerInvariant();
        var monthIsPeriod = string.IsNullOrEmpty(periodKind) || periodKind == "month";

        // `month` is the period value for a month period; otherwise it is the trend drill-down filter.
        string? month = null;
        if (!monthIsPeriod && Value(query, "month") is { } monthText)
        {
            if (!IsDeclared(report, ReportFilterNames.Month))
            {
                error = NotApplicableMessage;
                return false;
            }

            if (!JalaliMonth.TryParse(monthText, out var parsed))
            {
                error = "ماه باید به صورت YYYY-MM و با ماه بین ۱ تا ۱۲ باشد.";
                return false;
            }

            if (!MonthIntersects(parsed, period!))
            {
                error = "ماه انتخاب‌شده خارج از بازه گزارش است";
                return false;
            }

            month = parsed.Key;
        }

        if (!TryKey(report, query, ReportFilterNames.Vendor, out var vendor, out error) ||
            !TryKey(report, query, ReportFilterNames.Product, out var product, out error) ||
            !TryKey(report, query, ReportFilterNames.Category, out var category, out error))
        {
            return false;
        }

        string? docType = null;
        if (Value(query, "docType") is { } docTypeText)
        {
            if (!IsDeclared(report, ReportFilterNames.DocType))
            {
                error = NotApplicableMessage;
                return false;
            }

            if (docTypeText is not ("invoice" or "return"))
            {
                error = "نوع سند باید فاکتور خرید یا برگشت از خرید باشد.";
                return false;
            }

            docType = docTypeText;
        }

        string? search = null;
        if (Value(query, "q") is { } qText)
        {
            if (qText.Length > MaxSearchLength)
            {
                error = "متن جستجو نمی‌تواند بیش از ۱۰۰ نویسه باشد.";
                return false;
            }

            search = qText.Length == 0 ? null : qText;
        }

        string? match = null;
        if (Value(query, "match") is { } matchText)
        {
            if (report.Id != "documents")
            {
                error = NotApplicableMessage;
                return false;
            }

            match = matchText;
        }

        var compare = false;
        if (Value(query, "compare") is { } compareText)
        {
            if (!report.Compare)
            {
                error = NotApplicableMessage;
                return false;
            }

            if (compareText is not ("true" or "false"))
            {
                error = "مقدار مقایسه باید true یا false باشد.";
                return false;
            }

            compare = compareText == "true";
        }

        if (!TrySort(report, query, out var sort, out var descending, out error))
        {
            return false;
        }

        var page = 1;
        var pageSize = PrintLimit.Limit;
        if (!print)
        {
            if (!TryPage(query, out page, out error) || !TryPageSize(query, out pageSize, out error))
            {
                return false;
            }
        }

        filter = new ReportFilter(
            period!,
            compare,
            vendor,
            product,
            category,
            month,
            docType,
            search,
            match,
            sort!,
            descending,
            page,
            pageSize,
            print);
        return true;
    }

    /// <summary>
    /// The page to show: at least 1 and at most the last page for <paramref name="totalRows"/> rows (an empty
    /// result is page 1). The SQL layer reruns the query on this page when the requested page was beyond it.
    /// </summary>
    public static int EffectivePage(int page, int pageSize, long totalRows)
    {
        var lastPage = totalRows <= 0 ? 1 : (int)((totalRows + pageSize - 1) / pageSize);
        return Math.Clamp(page, 1, lastPage);
    }

    /// <summary>
    /// Escapes <c>%</c>, <c>_</c> and <c>\</c> for a LIKE pattern used with <c>ESCAPE '\'</c>. The result is
    /// bound as an NpgsqlParameter; it is never concatenated into SQL text.
    /// </summary>
    public static string EscapeLike(string text)
    {
        var escaped = text.Replace(@"\", @"\\", StringComparison.Ordinal);
        return escaped.Replace("%", @"\%", StringComparison.Ordinal).Replace("_", @"\_", StringComparison.Ordinal);
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

    private static bool IsDeclared(ReportDefinition report, string filterName)
    {
        foreach (var declared in report.Filters)
        {
            if (declared == filterName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParsePeriod(IReadOnlyDictionary<string, string?> query, JalaliDate today, out JalaliPeriod? period, out string? error)
    {
        var kind = Value(query, "period");
        var value = kind?.ToLowerInvariant() switch
        {
            null or "" or "month" => Value(query, "month"),
            "quarter" => Value(query, "quarter"),
            "year" => Value(query, "year"),
            _ => null,
        };

        var ok = JalaliPeriod.TryParse(kind, value, Value(query, "from"), Value(query, "to"), today, out period, out error);
        return ok;
    }

    private static bool MonthIntersects(JalaliMonth month, JalaliPeriod period)
    {
        var start = month.Year * 10000 + month.Month * 100 + 1;
        var end = month.Year * 10000 + month.Month * 100 + 31;
        return start <= period.ToKey && end >= period.FromKey;
    }

    private static bool TryKey(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> query,
        string name,
        out string? key,
        out string? error)
    {
        key = null;
        error = null;
        if (Value(query, name) is not { } text)
        {
            return true;
        }

        if (!IsDeclared(report, name))
        {
            error = NotApplicableMessage;
            return false;
        }

        key = text;
        return true;
    }

    private static bool TrySort(
        ReportDefinition report,
        IReadOnlyDictionary<string, string?> query,
        out string? sort,
        out bool descending,
        out string? error)
    {
        sort = null;
        descending = false;
        error = null;

        var requested = Value(query, "sort");
        SortKey? key = null;
        if (requested is null)
        {
            key = report.Sorts[0];
        }
        else
        {
            foreach (var candidate in report.Sorts)
            {
                if (candidate.Name == requested)
                {
                    key = candidate;
                    break;
                }
            }

            if (key is null)
            {
                error = "ترتیب انتخاب‌شده برای این گزارش معتبر نیست.";
                return false;
            }
        }

        descending = key.Descending;
        if (Value(query, "dir") is { } dir)
        {
            if (dir is not ("asc" or "desc"))
            {
                error = "جهت مرتب‌سازی باید صعودی یا نزولی باشد.";
                return false;
            }

            descending = dir == "desc";
        }

        sort = key.Name;
        return true;
    }

    private static bool TryPage(IReadOnlyDictionary<string, string?> query, out int page, out string? error)
    {
        page = 1;
        error = null;
        if (Value(query, "page") is not { } text)
        {
            return true;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1)
        {
            error = "شماره صفحه باید عدد صحیح و بزرگ‌تر یا برابر ۱ باشد.";
            return false;
        }

        return true;
    }

    private static bool TryPageSize(IReadOnlyDictionary<string, string?> query, out int pageSize, out string? error)
    {
        pageSize = DefaultPageSize;
        error = null;
        if (Value(query, "pageSize") is not { } text)
        {
            return true;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || Array.IndexOf(AllowedPageSizes, parsed) < 0)
        {
            error = "تعداد ردیف هر صفحه باید ۲۵، ۵۰ یا ۱۰۰ باشد.";
            return false;
        }

        pageSize = parsed;
        return true;
    }
}
