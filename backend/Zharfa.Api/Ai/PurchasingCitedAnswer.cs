namespace Zharfa.Api.Ai;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Zharfa.Api.Analytics;
using Zharfa.Api.Controllers;

/// <summary>
/// Server-side cited answer resolver and templater (research R22).
/// Enforces that answer either strictly respects facts or falls back to server template.
/// Model text is preserved in interpretation; never presents purchase net as sales.
/// </summary>
public static class PurchasingCitedAnswer
{
    public const string MetricUnavailableAnswer = "داده در دسترس نیست.";

    public static PurchasingChatResponse CreateUnavailable(
        PurchasingPeriodDto? period = null,
        PurchasingFiltersDto? filters = null,
        string dataLabel = "real",
        string? demoNotice = null)
    {
        return new PurchasingChatResponse(
            Answer: MetricUnavailableAnswer,
            Period: period ?? new PurchasingPeriodDto(string.Empty, string.Empty, string.Empty),
            Filters: filters ?? new PurchasingFiltersDto(),
            View: null,
            Facts: Array.Empty<string>(),
            Interpretation: string.Empty,
            Unavailable: true,
            DataLabel: dataLabel,
            DemoNotice: demoNotice);
    }

    public static string BuildServerTemplate(
        IReadOnlyList<string> facts,
        PurchasingPeriodDto period,
        PurchasingFiltersDto? filters,
        string? view)
    {
        var sb = new StringBuilder();

        var periodLabel = !string.IsNullOrWhiteSpace(period.JalaliLabel)
            ? period.JalaliLabel
            : (!string.IsNullOrWhiteSpace(period.From) ? $"{period.From} تا {period.To}" : "دوره جاری");

        sb.Append($"بر اساس گزارش {view ?? "خرید"} در دوره {JalaliDate.ToPersianDigits(periodLabel)}");

        if (filters != null)
        {
            var filterParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(filters.Vendor)) filterParts.Add($"تأمین‌کننده: {JalaliDate.ToPersianDigits(filters.Vendor)}");
            if (!string.IsNullOrWhiteSpace(filters.Product)) filterParts.Add($"کالا: {JalaliDate.ToPersianDigits(filters.Product)}");
            if (!string.IsNullOrWhiteSpace(filters.Category)) filterParts.Add($"گروه: {JalaliDate.ToPersianDigits(filters.Category)}");
            if (filterParts.Count > 0)
            {
                sb.Append($" ({string.Join("، ", filterParts)})");
            }
        }

        sb.Append(":\n");

        if (facts == null || facts.Count == 0)
        {
            sb.Append(MetricUnavailableAnswer);
            return sb.ToString();
        }

        foreach (var fact in facts)
        {
            var parts = ParseFact(fact);
            var metric = parts.GetValueOrDefault("metric");
            var value = parts.GetValueOrDefault("value") ?? parts.GetValueOrDefault("net");

            if (value != null && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            {
                string label;
                if (parts.TryGetValue("name", out var name))
                {
                    label = metric == "purchasing_top_products" ? $"کالای {name}" : $"تأمین‌کننده {name}";
                }
                else
                {
                    label = GetMetricLabel(metric);
                }

                var formatted = JalaliDate.ToPersianDigits($"{amount:#,0}");
                var raw = JalaliDate.ToPersianDigits(amount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine($"- {label}: {formatted} ریال ({raw} ریال)");
            }
            else if (parts.TryGetValue("count", out var count))
            {
                var persianCount = JalaliDate.ToPersianDigits(count);
                sb.AppendLine($"- تعداد تأمین‌کنندگان فعال: {persianCount} مورد");
            }
            else if (parts.TryGetValue("delta", out var delta) && parts.TryGetValue("percent", out var pct))
            {
                var formattedDelta = long.TryParse(delta, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deltaAmt)
                    ? JalaliDate.ToPersianDigits($"{deltaAmt:#,0}")
                    : JalaliDate.ToPersianDigits(delta);
                var persianPct = JalaliDate.ToPersianDigits(pct);
                sb.AppendLine($"- تغییرات نسبت به دوره قبل: {formattedDelta} ریال ({persianPct}٪)");
            }
            else
            {
                sb.AppendLine($"- {JalaliDate.ToPersianDigits(fact)}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static Dictionary<string, string> ParseFact(string fact)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entries = fact.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var eqIdx = entry.IndexOf('=');
            if (eqIdx > 0)
            {
                var k = entry[..eqIdx].Trim();
                var v = entry[(eqIdx + 1)..].Trim();
                dict[k] = v;
            }
        }
        return dict;
    }

    private static string GetMetricLabel(string? metric) => metric switch
    {
        "purchasing_overview.net" => "خالص خرید",
        "purchasing_overview.gross" => "ناخالص خرید",
        "purchasing_overview.returns" => "مبلغ برگشت از خرید",
        "purchasing_overview.vat" => "مالیات ارزش افزوده خرید",
        "purchasing_returns" => "مبلغ برگشت از خرید",
        _ => "مبلغ خرید"
    };

    public static (string Answer, string Interpretation) ResolveAnswer(
        IReadOnlyList<string> facts,
        PurchasingPeriodDto period,
        PurchasingFiltersDto? filters,
        string? view,
        string? modelText)
    {
        if (facts == null || facts.Count == 0)
        {
            return (MetricUnavailableAnswer, string.Empty);
        }

        var trimmedModel = modelText?.Trim() ?? string.Empty;

        // 1. If model text is empty, return server template
        if (string.IsNullOrWhiteSpace(trimmedModel))
        {
            return (BuildServerTemplate(facts, period, filters, view), string.Empty);
        }

        // 2. If model text says unavailable when facts exist, override with server template
        if (trimmedModel.Contains("داده در دسترس نیست") || trimmedModel.Contains("داده‌ای در دسترس نیست"))
        {
            return (BuildServerTemplate(facts, period, filters, view), trimmedModel);
        }

        // 3. Never present purchase net as فروش / pharmacy sales (Q13 rule)
        if (trimmedModel.Contains("فروش") || trimmedModel.Contains("داروخانه"))
        {
            return (BuildServerTemplate(facts, period, filters, view), trimmedModel);
        }

        // 4. Verify numbers in model text are within facts / period / filters
        if (InventsNumbers(trimmedModel, facts, period, filters))
        {
            return (BuildServerTemplate(facts, period, filters, view), trimmedModel);
        }

        return (trimmedModel, trimmedModel);
    }

    private static bool InventsNumbers(
        string modelText,
        IReadOnlyList<string> facts,
        PurchasingPeriodDto period,
        PurchasingFiltersDto? filters)
    {
        // Allowed numbers set
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Standard small numbers that might appear in prose
        string[] common = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "12", "100"];
        foreach (var c in common) allowed.Add(c);

        // Extract numbers from facts
        foreach (var fact in facts)
        {
            foreach (var n in ExtractNumbers(fact))
            {
                allowed.Add(n);
            }
        }

        // Extract numbers from period
        if (period != null)
        {
            foreach (var n in ExtractNumbers(period.From)) allowed.Add(n);
            foreach (var n in ExtractNumbers(period.To)) allowed.Add(n);
            foreach (var n in ExtractNumbers(period.JalaliLabel)) allowed.Add(n);
            if (period.Month != null) foreach (var n in ExtractNumbers(period.Month)) allowed.Add(n);
        }

        // Extract numbers from filters
        if (filters != null)
        {
            if (filters.Vendor != null) foreach (var n in ExtractNumbers(filters.Vendor)) allowed.Add(n);
            if (filters.Product != null) foreach (var n in ExtractNumbers(filters.Product)) allowed.Add(n);
            if (filters.Category != null) foreach (var n in ExtractNumbers(filters.Category)) allowed.Add(n);
        }

        // Extract numbers from modelText and check if any is not allowed
        var modelNumbers = ExtractNumbers(modelText);
        foreach (var num in modelNumbers)
        {
            // Ignore if in allowed
            if (allowed.Contains(num))
            {
                continue;
            }

            // Also check if it's a sub-fragment or percentage (e.g. 10.5)
            if (num.Length > 1 && !allowed.Contains(num))
            {
                return true; // Model invented an unknown number!
            }
        }

        return false;
    }

    private static IEnumerable<string> ExtractNumbers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch is >= '0' and <= '9')
            {
                sb.Append(ch);
            }
            else if (ch is >= '۰' and <= '۹')
            {
                sb.Append((char)('0' + (ch - '۰')));
            }
            else if (ch is >= '٠' and <= '٩')
            {
                sb.Append((char)('0' + (ch - '٠')));
            }
            else if (ch == ',' || ch == '،')
            {
                // ignore separator inside digits
            }
            else
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }
}
