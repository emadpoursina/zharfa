namespace Zharfa.Api.Audit;

using System.Collections.Generic;

/// <summary>
/// Audit action codes and their Persian labels (data-model §2.2). A code is written only by the code that owns
/// the event; old rows whose code is not listed keep the code as their label.
/// </summary>
public static class AuditActions
{
    public const string SignInSucceeded = "auth.signin.succeeded";
    public const string SignInFailed = "auth.signin.failed";
    public const string SignOut = "auth.signout";
    public const string AuthLocked = "auth.locked";
    public const string PasswordChanged = "auth.password_changed";
    public const string UserCreated = "user.created";
    public const string UserRoleChanged = "user.role_changed";
    public const string UserDeactivated = "user.deactivated";
    public const string UserReactivated = "user.reactivated";
    public const string UserPasswordReset = "user.password_reset";
    public const string UserUnlocked = "user.unlocked";
    public const string ReportOpened = "report.opened";
    public const string ReportPrinted = "report.printed";
    public const string AccessDenied = "access.denied";
    public const string AuditViewed = "audit.viewed";
    public const string DataQualityViewed = "data_quality.viewed";
    public const string SyncRequested = "sync.requested";
    public const string SyncRejected = "sync.rejected";
    public const string ProviderStatusViewed = "provider.status.viewed";
    public const string ReconciliationRun = "reconciliation.run";
    public const string ReconciliationMismatch = "reconciliation.mismatch";
    public const string SettingsAiProviderChanged = "settings.ai_provider.changed";
    public const string Question = "ai.question";

    /// <summary>Every action code with its Persian label, in the data-model order.</summary>
    public static IReadOnlyList<(string Code, string Label)> All { get; } =
    [
        (SignInSucceeded, "ورود موفق"),
        (SignInFailed, "ورود ناموفق"),
        (SignOut, "خروج"),
        (AuthLocked, "قفل موقت حساب"),
        (PasswordChanged, "تغییر گذرواژه توسط کاربر"),
        (UserCreated, "ایجاد کاربر"),
        (UserRoleChanged, "تغییر نقش کاربر"),
        (UserDeactivated, "غیرفعال‌سازی کاربر"),
        (UserReactivated, "فعال‌سازی دوباره کاربر"),
        (UserPasswordReset, "بازنشانی گذرواژه"),
        (UserUnlocked, "رفع قفل حساب"),
        (ReportOpened, "باز کردن گزارش"),
        (ReportPrinted, "چاپ گزارش"),
        (AccessDenied, "رد دسترسی"),
        (AuditViewed, "مشاهده گزارش رویدادها"),
        (DataQualityViewed, "مشاهده کیفیت داده"),
        (SyncRequested, "درخواست همگام‌سازی"),
        (SyncRejected, "رد درخواست همگام‌سازی"),
        (ProviderStatusViewed, "مشاهده وضعیت ارائه‌دهنده هوش مصنوعی"),
        (ReconciliationRun, "اجرای تطبیق"),
        (ReconciliationMismatch, "عدم تطابق در تطبیق"),
        (SettingsAiProviderChanged, "تغییر تنظیمات ارائه‌دهنده هوش مصنوعی"),
        (Question, "پرسش از دستیار هوشمند"),
    ];

    /// <summary>Persian label of an action code. Unknown codes return the code itself.</summary>
    public static string Label(string code)
    {
        foreach (var entry in All)
        {
            if (entry.Code == code)
            {
                return entry.Label;
            }
        }

        return code;
    }
}
