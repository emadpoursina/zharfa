namespace Zharfa.Api.Users;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;
using Zharfa.Api.Audit;
using Zharfa.Api.Data;
using Zharfa.Api.Mabna.Sync;
using Zharfa.Api.Security;

/// <summary>One user as the admin screens see it: never the hash, never the failure counter (contracts/api.md §2).</summary>
public sealed record UserDto(
    long Id,
    string Username,
    string Role,
    string RoleLabel,
    bool Active,
    bool Locked,
    string? LockedUntilJalali,
    bool MustChangePassword,
    string CreatedAtJalali);

/// <summary>Outcome of one admin action; the controller maps it to the HTTP status.</summary>
public abstract record UserOperation
{
    public sealed record Succeeded(UserDto User) : UserOperation;

    public sealed record NotFound : UserOperation;

    public sealed record Invalid(string Message) : UserOperation;

    public sealed record Conflict(string Message, string Code) : UserOperation;
}

/// <summary>
/// Admin actions on users and roles (FR-006, FR-008, FR-010, research R4). Every change writes its audit entry after
/// the change is stored. Role changes and deactivations run in one transaction that first locks the active Admin
/// rows, so two concurrent demotions cannot both pass the last-Admin rule.
/// </summary>
public sealed class UserAdminService(
    ZharfaDbContext db,
    IAuditLogger audit,
    IOptions<SyncOptions> syncOptions)
{
    public const string NotFoundMessage = "کاربر موردنظر یافت نشد.";
    public const string DuplicateUsernameMessage = "این نام کاربری قبلاً ثبت شده است.";
    public const string UnknownRoleMessage = "نقش انتخاب‌شده معتبر نیست.";

    private const string UniqueViolationCode = "23505";

    public async Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var zone = Zone();
        var rows = await db.AppUsers
            .AsNoTracking()
            .OrderBy(user => user.Username)
            .ThenBy(user => user.UserId)
            .ToListAsync(cancellationToken);

        return rows.Select(user => ToDto(user, now, zone)).ToList();
    }

    public async Task<UserOperation> CreateAsync(
        string? username,
        string? password,
        string? role,
        CancellationToken cancellationToken)
    {
        if (!UsernameRules.TryValidate(username, out var trimmed, out var key, out var usernameError))
        {
            return new UserOperation.Invalid(usernameError!);
        }

        if (!PasswordRules.TryValidate(password, out var passwordError))
        {
            return new UserOperation.Invalid(passwordError!);
        }

        if (role is null || !Roles.All.Contains(role))
        {
            return new UserOperation.Invalid(UnknownRoleMessage);
        }

        var taken = await db.AppUsers.AsNoTracking().AnyAsync(user => user.UsernameKey == key, cancellationToken);
        if (taken)
        {
            return new UserOperation.Invalid(DuplicateUsernameMessage);
        }

        var now = DateTime.UtcNow;
        var user = new AppUser
        {
            Username = trimmed,
            UsernameKey = key,
            PasswordHash = PasswordHasher.Hash(password!),
            Role = role,
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.AppUsers.Add(user);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent create took the same login key between the check and the insert.
            db.Entry(user).State = EntityState.Detached;
            return new UserOperation.Invalid(DuplicateUsernameMessage);
        }

        await audit.RecordAsync(
            new AuditEntry(AuditActions.UserCreated, TargetOf(trimmed), AuditDetail.Of(("role", role))),
            cancellationToken);

        return new UserOperation.Succeeded(ToDto(user, now, Zone()));
    }

    public async Task<UserOperation> ChangeRoleAsync(long id, string? role, CancellationToken cancellationToken)
    {
        if (role is null || !Roles.All.Contains(role))
        {
            return new UserOperation.Invalid(UnknownRoleMessage);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = await FindForUpdateAsync(id, cancellationToken);
        if (user is null)
        {
            return new UserOperation.NotFound();
        }

        if (user.Role == role)
        {
            return new UserOperation.Succeeded(ToDto(user, DateTime.UtcNow, Zone()));
        }

        var otherActiveAdmins = await CountOtherActiveAdminsAsync(user.UserId, cancellationToken);
        if (!LastAdminGuard.Allows(user.Role, user.IsActive, role, deactivating: false, otherActiveAdmins))
        {
            return new UserOperation.Conflict(LastAdminGuard.Message, LastAdminGuard.Code);
        }

        var previousRole = user.Role;
        var now = DateTime.UtcNow;
        user.Role = role;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await audit.RecordAsync(
            new AuditEntry(
                AuditActions.UserRoleChanged,
                TargetOf(user.Username),
                AuditDetail.Of(("from", previousRole), ("to", role))),
            cancellationToken);

        return new UserOperation.Succeeded(ToDto(user, now, Zone()));
    }

    public async Task<UserOperation> ResetPasswordAsync(long id, string? password, CancellationToken cancellationToken)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(row => row.UserId == id, cancellationToken);
        if (user is null)
        {
            return new UserOperation.NotFound();
        }

        if (!PasswordRules.TryValidate(password, out var error))
        {
            return new UserOperation.Invalid(error!);
        }

        // A reset neither unlocks the account nor forces a change at the next sign-in (research R20 item 2).
        var now = DateTime.UtcNow;
        user.PasswordHash = PasswordHasher.Hash(password!);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry(AuditActions.UserPasswordReset, TargetOf(user.Username)), cancellationToken);

        return new UserOperation.Succeeded(ToDto(user, now, Zone()));
    }

    public async Task<UserOperation> DeactivateAsync(long id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = await FindForUpdateAsync(id, cancellationToken);
        if (user is null)
        {
            return new UserOperation.NotFound();
        }

        if (!user.IsActive)
        {
            return new UserOperation.Succeeded(ToDto(user, DateTime.UtcNow, Zone()));
        }

        var otherActiveAdmins = await CountOtherActiveAdminsAsync(user.UserId, cancellationToken);
        if (!LastAdminGuard.Allows(user.Role, user.IsActive, user.Role, deactivating: true, otherActiveAdmins))
        {
            return new UserOperation.Conflict(LastAdminGuard.Message, LastAdminGuard.Code);
        }

        var now = DateTime.UtcNow;
        user.IsActive = false;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry(AuditActions.UserDeactivated, TargetOf(user.Username)), cancellationToken);

        return new UserOperation.Succeeded(ToDto(user, now, Zone()));
    }

    /// <summary>Reactivation clears the failure counter and any lock (contracts/api.md §2).</summary>
    public async Task<UserOperation> ReactivateAsync(long id, CancellationToken cancellationToken)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(row => row.UserId == id, cancellationToken);
        if (user is null)
        {
            return new UserOperation.NotFound();
        }

        var now = DateTime.UtcNow;
        user.IsActive = true;
        user.FailedSignins = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry(AuditActions.UserReactivated, TargetOf(user.Username)), cancellationToken);

        return new UserOperation.Succeeded(ToDto(user, now, Zone()));
    }

    /// <summary>Unlock clears the counter and the lock. An account that is not locked now is left as it is, unaudited.</summary>
    public async Task<UserOperation> UnlockAsync(long id, CancellationToken cancellationToken)
    {
        var user = await db.AppUsers.FirstOrDefaultAsync(row => row.UserId == id, cancellationToken);
        if (user is null)
        {
            return new UserOperation.NotFound();
        }

        var now = DateTime.UtcNow;
        var locked = user.LockedUntil is { } until && until > now;
        if (!locked)
        {
            return new UserOperation.Succeeded(ToDto(user, now, Zone()));
        }

        user.FailedSignins = 0;
        user.LockedUntil = null;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry(AuditActions.UserUnlocked, TargetOf(user.Username)), cancellationToken);

        return new UserOperation.Succeeded(ToDto(user, now, Zone()));
    }

    /// <summary>
    /// Locks every active Admin row (in a fixed order by id) and returns the user, tracked. The lock is taken before
    /// any count, so a concurrent change to the Admin set waits for this transaction.
    /// </summary>
    private async Task<AppUser?> FindForUpdateAsync(long id, CancellationToken cancellationToken)
    {
        _ = await db.Database
            .SqlQueryRaw<long>("SELECT user_id FROM app_user WHERE role = 'admin' AND is_active ORDER BY user_id FOR UPDATE")
            .ToListAsync(cancellationToken);

        return await db.AppUsers.FirstOrDefaultAsync(row => row.UserId == id, cancellationToken);
    }

    private Task<int> CountOtherActiveAdminsAsync(long id, CancellationToken cancellationToken) =>
        db.AppUsers
            .AsNoTracking()
            .CountAsync(user => user.Role == Roles.Admin && user.IsActive && user.UserId != id, cancellationToken);

    private static string TargetOf(string username) => $"user:{username}";

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: UniqueViolationCode };

    private static UserDto ToDto(AppUser user, DateTime nowUtc, TimeZoneInfo zone)
    {
        var locked = user.LockedUntil is { } until && until > nowUtc;

        return new UserDto(
            Id: user.UserId,
            Username: user.Username,
            Role: user.Role,
            RoleLabel: Roles.Label(user.Role),
            Active: user.IsActive,
            Locked: locked,
            LockedUntilJalali: locked ? JalaliText(user.LockedUntil!.Value, zone) : null,
            MustChangePassword: user.MustChangePassword,
            CreatedAtJalali: JalaliText(user.CreatedAt, zone));
    }

    private static string JalaliText(DateTime utc, TimeZoneInfo zone) =>
        SyncTimeText.Jalali(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), zone);

    private TimeZoneInfo Zone() => SyncOptions.ResolveTimeZone(syncOptions.Value.TimeZone);
}
