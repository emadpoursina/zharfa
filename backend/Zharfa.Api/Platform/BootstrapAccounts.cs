namespace Zharfa.Api.Platform;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Data;
using Zharfa.Api.Security;

/// <summary>
/// Production first-run Admin (research R6, FR-034). When the install runs in production mode and no active
/// <c>admin</c> exists, one Admin is created from <c>Bootstrap__AdminUsername</c> and either
/// <c>Bootstrap__AdminPassword</c> or <c>Bootstrap__AdminPasswordFile</c> (a Docker secret file; trailing CR/LF
/// removed). The account must change its password at the first sign-in (<c>must_change_password</c>). The
/// password is at least 12 characters, hashed with <see cref="PasswordHasher"/> and never logged. Missing or short
/// values refuse startup and name the keys only, never a value. The Demo 1 Manager keys are retired.
/// </summary>
public static class BootstrapAccounts
{
    public const int MinPasswordLength = 12;

    public const string AdminUsernameKey = "Bootstrap__AdminUsername";
    public const string AdminPasswordKey = "Bootstrap__AdminPassword";
    public const string AdminPasswordFileKey = "Bootstrap__AdminPasswordFile";

    public sealed record BootstrapValues(string AdminUsername, string AdminPassword);

    /// <summary>
    /// Reads the installer password: <c>Bootstrap__AdminPassword</c>, or when that is blank the content of the file at
    /// <c>Bootstrap__AdminPasswordFile</c> with trailing CR/LF removed. Null when neither gives a value. Never logged.
    /// </summary>
    public static string? ReadPassword(IConfiguration configuration)
    {
        var direct = ConfigurationKeys.Get(configuration, AdminPasswordKey);
        if (direct is not null)
        {
            return direct;
        }

        var file = ConfigurationKeys.Get(configuration, AdminPasswordFileKey);
        if (file is null)
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(file.Trim()).TrimEnd('\r', '\n');
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Pure: reads and validates the Admin username and password (null = valid). Never echoes a value.</summary>
    public static string? Validate(IConfiguration configuration)
    {
        var adminUser = ConfigurationKeys.Get(configuration, AdminUsernameKey)?.Trim();
        var adminPw = ReadPassword(configuration);

        if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPw))
        {
            return "در حالت production باید Bootstrap__AdminUsername و یکی از Bootstrap__AdminPassword یا " +
                   "Bootstrap__AdminPasswordFile تنظیم شده باشند (زمانی که مدیر فعالی وجود ندارد). " +
                   "In production mode Bootstrap__AdminUsername and either Bootstrap__AdminPassword or " +
                   "Bootstrap__AdminPasswordFile are required while no active Admin exists.";
        }

        if (adminPw!.Length < MinPasswordLength)
        {
            return $"گذرواژهٔ مدیر اولیه (Bootstrap__AdminPassword یا Bootstrap__AdminPasswordFile) باید حداقل {MinPasswordLength} کاراکتر باشد. " +
                   $"The Bootstrap admin password must be at least {MinPasswordLength} characters long.";
        }

        return null;
    }

    /// <summary>
    /// Creates the first Admin when required. Returns true when an account was created. Throws
    /// <see cref="InvalidOperationException"/> (startup refusal) when the values are missing or short, or when a user
    /// already has the same login key (the message names the key, never the value).
    /// </summary>
    public static async Task<bool> EnsureAsync(
        DataModeOptions dataMode,
        IConfiguration configuration,
        ZharfaDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!dataMode.IsProduction)
        {
            return false;
        }

        var hasActiveAdmin = await db.AppUsers
            .AsNoTracking()
            .AnyAsync(user => user.Role == Roles.Admin && user.IsActive, cancellationToken);
        if (hasActiveAdmin)
        {
            // The keys are ignored once an active Admin exists; they never touch an existing password.
            return false;
        }

        var error = Validate(configuration);
        if (error is not null)
        {
            logger.LogCritical("Bootstrap admin configuration is incomplete: {Error}", RedactionHelper.Scrub(error));
            throw new InvalidOperationException(error);
        }

        var values = new BootstrapValues(
            ConfigurationKeys.Get(configuration, AdminUsernameKey)!.Trim(),
            ReadPassword(configuration)!);

        var key = UsernameRules.Key(values.AdminUsername);
        var keyTaken = await db.AppUsers
            .AsNoTracking()
            .AnyAsync(user => user.UsernameKey == key, cancellationToken);
        if (keyTaken)
        {
            throw new InvalidOperationException(
                $"کاربری با نام‌کاربری مشخص‌شده در {AdminUsernameKey} از پیش وجود دارد؛ آن را تغییر دهید یا حذف کنید. " +
                $"A user with the login named by {AdminUsernameKey} already exists; change or remove that user.");
        }

        db.AppUsers.Add(new AppUser
        {
            Username = values.AdminUsername,
            UsernameKey = key,
            PasswordHash = PasswordHasher.Hash(values.AdminPassword),
            Role = Roles.Admin,
            IsActive = true,
            MustChangePassword = true,
        });
        await db.SaveChangesAsync(cancellationToken);

        // The password and username values are never logged.
        logger.LogInformation(
            "Production bootstrap Admin account created. The password must be changed at first sign-in.");
        return true;
    }
}
