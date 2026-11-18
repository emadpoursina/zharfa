namespace Zharfa.Api.Security;

/// <summary>
/// Password rule for passwords set in the application (create, reset and change; research R20 item 3).
/// The installer-supplied first Admin password keeps its own 12-character minimum (<c>BootstrapAccounts</c>).
/// Pure: no hashing and no storage here.
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 8;

    public const string TooShortMessage = "گذرواژه باید حداقل ۸ نویسه باشد.";

    /// <summary>Refuses null, whitespace-only and shorter-than-<see cref="MinLength"/> passwords (raw length, not trimmed).</summary>
    public static bool TryValidate(string? password, out string? error)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinLength)
        {
            error = TooShortMessage;
            return false;
        }

        error = null;
        return true;
    }
}
