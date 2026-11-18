namespace Zharfa.Api.Security;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Role ids stored in <c>app_user.role</c> (research R1, data-model §1.2). Demo 1 "manager" is the
/// legacy spelling of <see cref="Management"/> and is accepted only through <see cref="Normalize"/>.
/// </summary>
public static class Roles
{
    public const string Admin = "admin";
    public const string Management = "management";
    public const string Purchasing = "purchasing";
    public const string Finance = "finance";

    /// <summary>The four role ids in the contract order.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, Management, Purchasing, Finance];

    /// <summary>Persian label of a role id. Unknown or missing ids return the id (empty when missing).</summary>
    public static string Label(string? id) => id switch
    {
        Admin => "مدیر سامانه",
        Management => "مدیریت",
        Purchasing => "واحد خرید",
        Finance => "واحد مالی",
        _ => id ?? string.Empty,
    };

    /// <summary>
    /// Maps a role claim or stored value to a role id: trimmed, lower-invariant, legacy <c>manager</c>
    /// becomes <c>management</c>. Returns null for anything that is not one of <see cref="All"/>.
    /// </summary>
    public static string? Normalize(string? claimRole)
    {
        if (string.IsNullOrWhiteSpace(claimRole))
        {
            return null;
        }

        var id = claimRole.Trim().ToLowerInvariant();
        if (string.Equals(id, "manager", StringComparison.Ordinal))
        {
            id = Management;
        }

        return All.Contains(id) ? id : null;
    }
}
