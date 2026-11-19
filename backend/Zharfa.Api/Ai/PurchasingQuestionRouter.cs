namespace Zharfa.Api.Ai;

using System;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Routing result for a user question.
/// Non-purchasing scopes guarantee Tool and PeriodRule are null.
/// </summary>
public record PurchasingQuestionRoute(
    string Scope,
    string? Tool = null,
    string? PeriodRule = null);

/// <summary>
/// Deterministic Persian token-based scope router for purchasing questions (research R21-R22).
/// Enforces server scope gate before any tool execution or model invocation.
/// Pure C#: no database, no network, no LLM classification.
/// </summary>
public static class PurchasingQuestionRouter
{
    private static readonly Regex HostIdRegex = new(@"\bhost_?id\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Normalizes Persian/Arabic text for token matching:
    /// Arabic Yeh and Alef Maksura -> Persian Yeh; Arabic Kaf -> Persian Kaf;
    /// Hamza variations (أ, إ, ؤ, ئ) normalized; ZWNJ removed.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\u064A': // Arabic Yeh (ي)
                case '\u0649': // Alef Maksura (ى)
                    sb.Append('\u06CC'); // Persian Yeh (ی)
                    break;
                case '\u0643': // Arabic Kaf (ك)
                    sb.Append('\u06A9'); // Persian Kaf (ک)
                    break;
                case '\u0623': // Alef with Hamza above (أ)
                case '\u0625': // Alef with Hamza below (إ)
                case '\u0622': // Alef with Madda (آ)
                    sb.Append('ا');
                    break;
                case '\u200C': // ZWNJ
                    // skip or replace with space
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString().ToLowerInvariant();
    }

    public static PurchasingQuestionRoute Classify(string question, bool hasSuccessfulSync = true)
    {
        var raw = question?.Trim() ?? string.Empty;
        var norm = Normalize(raw);

        // 1. Mabna bypass check
        if (norm.Contains("مبنا") || HostIdRegex.IsMatch(raw))
        {
            return new PurchasingQuestionRoute("mabna-bypass");
        }

        // 2. Out-of-domain sales check: فروش / داروخانه / نسخه
        if (norm.Contains("فروش") || norm.Contains("داروخانه") || norm.Contains("نسخه"))
        {
            return new PurchasingQuestionRoute("out-of-domain-sales");
        }

        // 3. Out-of-domain inventory check: موجودی / انبار / کسری کالا
        if (norm.Contains("موجودی") || norm.Contains("انبار") || norm.Contains("کسری کالا"))
        {
            return new PurchasingQuestionRoute("out-of-domain-inventory");
        }

        // 4. Vendor financials check: مانده / بدهی / پرداخت / چک / اسناد پرداختنی
        if (norm.Contains("مانده") || norm.Contains("بدهی") || norm.Contains("پرداخت") ||
            norm.Contains("چک") || norm.Contains("اسناد پرداختنی"))
        {
            return new PurchasingQuestionRoute("vendor-financials");
        }

        // 5. Overlay: if no successful sync has completed, in-domain purchasing cannot be answered
        if (!hasSuccessfulSync)
        {
            return new PurchasingQuestionRoute("no-successful-sync");
        }

        // 6. In-domain purchasing: determine tool and period rule
        // Check period rule first
        string periodRule = "overview-default";
        string? tool = null;

        if (norm.Contains("اینده") || norm.Contains("آینده"))
        {
            periodRule = "future-empty-month";
            tool = "purchasing_overview";
        }
        else if (norm.Contains("فصل"))
        {
            periodRule = "current-jalali-quarter";
            tool = "purchasing_by_vendor";
        }
        else if ((norm.Contains("ماه قبل") || norm.Contains("ماه جلالی قبل")) &&
                 !norm.Contains("نسبت به") && !norm.Contains("تغییر"))
        {
            periodRule = "previous-jalali-month";
            tool = "purchasing_overview";
        }

        // If tool was not uniquely determined by period rule, resolve from tokens
        if (tool == null)
        {
            if (norm.Contains("روند"))
            {
                tool = "purchasing_trend";
            }
            else if (norm.Contains("تامین") || norm.Contains("تأمین"))
            {
                if (norm.Contains("تفکیک") || norm.Contains("گزارش"))
                {
                    tool = "purchasing_by_vendor";
                }
                else
                {
                    // Q4 (top vendor) and Q9 (active vendors count) are in purchasing_overview
                    tool = "purchasing_overview";
                }
            }
            else if (norm.Contains("پرمصرف") || (norm.Contains("کالا") && (norm.Contains("برتر") || norm.Contains("بیشترین"))))
            {
                tool = "purchasing_top_products";
            }
            else if (norm.Contains("گروه کالا") || norm.Contains("گروه") || norm.Contains("دسته"))
            {
                tool = "purchasing_by_category";
            }
            else if (norm.Contains("برگشت"))
            {
                if (norm.Contains("تفکیک") || norm.Contains("گزارش"))
                {
                    tool = "purchasing_returns";
                }
                else
                {
                    // Q7 (returns total) is in purchasing_overview
                    tool = "purchasing_overview";
                }
            }
            else
            {
                tool = "purchasing_overview";
            }
        }

        return new PurchasingQuestionRoute("purchasing", tool, periodRule);
    }
}
