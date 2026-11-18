namespace Zharfa.Api.Security;

/// <summary>
/// Usernames (data-model §1.1, research R4). A username is stored as typed (trimmed) and compared through
/// <see cref="Key"/>, the same mapping as the <c>username_key</c> backfill in migration 0008:
/// <c>lower(translate(btrim(u), 'يىك', 'ییک'))</c>. Pure: no database, no clock.
/// </summary>
public static class UsernameRules
{
    public const int MinLength = 3;
    public const int MaxLength = 64;

    public const string LengthMessage = "نام کاربری باید بین ۳ تا ۶۴ نویسه باشد.";
    public const string CharacterMessage = "نام کاربری فقط می‌تواند شامل حروف، رقم، نقطه، خط تیره و زیرخط باشد.";

    /// <summary>
    /// Comparison key: trimmed, Arabic yeh (U+064A) and alef maksura (U+0649) become Persian yeh (U+06CC), Arabic
    /// kaf (U+0643) becomes Persian kaf (U+06A9), then lower-invariant. Matches the SQL backfill of migration 0008.
    /// </summary>
    public static string Key(string s) => s
        .Trim()
        .Replace('ي', 'ی')
        .Replace('ى', 'ی')
        .Replace('ك', 'ک')
        .ToLowerInvariant();

    /// <summary>
    /// Validates a typed username. Trimmed length 3–64; each character is an ASCII letter or digit, <c>.</c>,
    /// <c>_</c>, <c>-</c>, a Persian or Arabic letter (U+0600–U+06FF) or a Persian/Arabic-Indic digit. Zero-width
    /// non-joiner (U+200C) and spaces are refused. Returns the trimmed text and its key on success.
    /// </summary>
    public static bool TryValidate(string? raw, out string trimmed, out string key, out string? error)
    {
        trimmed = raw?.Trim() ?? string.Empty;
        key = string.Empty;

        if (trimmed.Length < MinLength || trimmed.Length > MaxLength)
        {
            error = LengthMessage;
            return false;
        }

        foreach (var c in trimmed)
        {
            if (!IsAllowedCharacter(c))
            {
                error = CharacterMessage;
                return false;
            }
        }

        key = Key(trimmed);
        error = null;
        return true;
    }

    private static bool IsAllowedCharacter(char c)
    {
        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')
        {
            return true;
        }

        // Persian and Arabic letters, and Persian (U+06F0–U+06F9) and Arabic-Indic (U+0660–U+0669) digits.
        if (c is >= '؀' and <= 'ۿ')
        {
            return char.IsLetter(c) || c is >= '۰' and <= '۹' || c is >= '٠' and <= '٩';
        }

        return false;
    }
}
