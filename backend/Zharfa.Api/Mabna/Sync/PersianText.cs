namespace Zharfa.Api.Mabna.Sync;

using System.Text;

/// <summary>
/// Shared text normalization for ingestion (FR-019): Arabic yeh/kaf become
/// Persian yeh/kaf, whitespace is trimmed and collapsed, ZWNJ (U+200C) and
/// digits are kept exactly as recorded. One shared implementation is used by
/// sync and by later search.
/// </summary>
public static class PersianText
{
    private const char ArabicYeh = '\u064A';   // ي
    private const char PersianYeh = '\u06CC';  // ی
    private const char ArabicKaf = '\u0643';   // ك
    private const char PersianKaf = '\u06A9';  // ک
    private const char Zwnj = '\u200C';

    /// <summary>Placeholder substituted by callers for an empty source name.</summary>
    public const string EmptyNamePlaceholder = "(بدون نام)";

    /// <summary>
    /// Normalizes a source text value. Never returns null; an empty or
    /// whitespace-only input returns <see cref="string.Empty"/> (the caller
    /// substitutes <see cref="EmptyNamePlaceholder"/>).
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        var pendingSpace = false;
        foreach (var ch in input)
        {
            var c = ch switch
            {
                ArabicYeh => PersianYeh,
                ArabicKaf => PersianKaf,
                _ => ch
            };

            if (c == Zwnj)
            {
                sb.Append(c);
                pendingSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Normalize and substitute the placeholder for an empty result.</summary>
    public static string NormalizeName(string? input)
    {
        var normalized = Normalize(input);
        return normalized.Length == 0 ? EmptyNamePlaceholder : normalized;
    }
}
