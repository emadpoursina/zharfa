namespace Zharfa.Api.Purchasing;

using System;

/// <summary>
/// Configuration for purchasing figures and formulas (<c>Purchasing__*</c>).
/// An empty value from an env file falls back to the default.
/// </summary>
public sealed class PurchasingOptions
{
    public const string SectionName = "Purchasing";
    public const string ConfigurationKey = "Purchasing__DiscountBasis";
    public const string DefaultDiscountBasis = PurchasingDiscountBasis.Line;

    private string _discountBasis = DefaultDiscountBasis;

    /// <summary>
    /// Discount basis: "line" (default until verified on-site) or "lineAndDocument".
    /// In production, this must match the source map's verified discount basis (startup refusal if not).
    /// </summary>
    public string DiscountBasis
    {
        get => _discountBasis;
        set => _discountBasis = string.IsNullOrWhiteSpace(value) ? DefaultDiscountBasis : value.Trim();
    }
}

/// <summary>
/// Discount basis rules (FR-014, research R9). Pure: no I/O.
/// </summary>
public static class PurchasingDiscountBasis
{
    public const string Line = "line";
    public const string LineAndDocument = "lineAndDocument";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Line, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, LineAndDocument, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Startup decision. Returns <c>null</c> when the configuration may start;
    /// otherwise a Persian + English reason. Rules:
    ///  - the configured basis must be <c>line</c> or <c>lineAndDocument</c> in every mode;
    ///  - in production, when the source map declares a verified basis, the configured
    ///    basis must equal it (hard refusal, never a silent mismatch). No map → nothing to compare.
    /// </summary>
    public static string? Validate(string? configured, bool isProduction, string? verifiedBasis)
    {
        if (!IsKnown(configured))
        {
            return $"مقدار {PurchasingOptions.ConfigurationKey} نامعتبر است؛ فقط \"line\" یا \"lineAndDocument\" مجاز است. " +
                   $"{PurchasingOptions.ConfigurationKey} must be \"line\" or \"lineAndDocument\".";
        }

        if (!isProduction || verifiedBasis is null)
        {
            return null;
        }

        if (!IsKnown(verifiedBasis))
        {
            return "پایه تخفیف در نقشه منبع نامعتبر است. The source map discountBasis.value must be \"line\" or \"lineAndDocument\".";
        }

        if (!string.Equals(configured!.Trim(), verifiedBasis.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"{PurchasingOptions.ConfigurationKey} ({configured.Trim()}) با پایه تخفیف تأییدشده در نقشه منبع ({verifiedBasis.Trim()}) یکسان نیست؛ " +
                   "در حالت production راه‌اندازی متوقف می‌شود. " +
                   $"{PurchasingOptions.ConfigurationKey} ({configured.Trim()}) differs from the verified source-map basis ({verifiedBasis.Trim()}); production startup refused.";
        }

        return null;
    }
}
