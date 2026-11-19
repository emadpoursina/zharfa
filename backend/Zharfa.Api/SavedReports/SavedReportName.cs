namespace Zharfa.Api.SavedReports;

using Zharfa.Api.Mabna.Sync;

/// <summary>
/// Rules for saved report names (spec 007 FR-011, research R7): trimmed, Persian text normalization
/// (Arabic kaf and yeh mapped to Persian), unique per user via name_key, maximum 50 saved reports per user.
/// Pure: no database, no clock.
/// </summary>
public static class SavedReportName
{
    public const int MaxPerUser = 50;

    public const string EmptyNameMessage = "نام گزارش نمی‌تواند خالی باشد.";
    public const string CapExceededMessage = "حداکثر ۵۰ گزارش ذخیره شده مجاز است. ابتدا یکی را حذف کنید.";
    public const string ExistsMessage = "گزارشی با این نام دارید. آیا جایگزین شود؟";

    /// <summary>
    /// Computes the normalized comparison key for a saved report name.
    /// Trims whitespace and normalizes Arabic yeh/kaf to Persian.
    /// </summary>
    public static string NormalizeKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return PersianText.Normalize(name.Trim());
    }

    /// <summary>
    /// Validates a raw report name. Returns false if null, empty, or whitespace-only.
    /// On success, outputs the trimmed display name and its normalized <paramref name="nameKey"/>.
    /// </summary>
    public static bool TryValidate(string? raw, out string trimmed, out string nameKey, out string? error)
    {
        trimmed = raw?.Trim() ?? string.Empty;
        nameKey = NormalizeKey(trimmed);

        if (string.IsNullOrWhiteSpace(trimmed) || string.IsNullOrEmpty(nameKey))
        {
            error = EmptyNameMessage;
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Checks if a new report can be inserted given the user's current count of saved reports.
    /// Overwriting an existing saved report is always allowed even at or above the 50 cap.
    /// </summary>
    public static bool CanInsert(int currentCount, bool isOverwrite)
    {
        if (isOverwrite)
        {
            return true;
        }

        return currentCount < MaxPerUser;
    }
}
