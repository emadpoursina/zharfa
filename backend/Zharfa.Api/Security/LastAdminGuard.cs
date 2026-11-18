namespace Zharfa.Api.Security;

/// <summary>
/// Refuses the change that would leave the install without an active Admin (FR-008, research R4). Pure: the caller
/// counts the other active Admins inside the transaction that locks the Admin rows.
/// </summary>
public static class LastAdminGuard
{
    public const string Code = "last-admin";
    public const string Message = "دست‌کم یک مدیر سامانهٔ فعال باید باقی بماند.";

    /// <summary>
    /// False only when the target is an active <c>admin</c>, the change removes it from the active Admin set
    /// (deactivation, or a new role other than <c>admin</c>) and no other active Admin exists.
    /// </summary>
    public static bool Allows(
        string targetRole,
        bool targetActive,
        string? newRole,
        bool deactivating,
        int otherActiveAdmins)
    {
        var isActiveAdmin = targetActive && targetRole == Roles.Admin;
        if (!isActiveAdmin)
        {
            return true;
        }

        var removesFromActiveAdmins = deactivating || newRole != Roles.Admin;
        return !removesFromActiveAdmins || otherActiveAdmins > 0;
    }
}
