namespace Zharfa.Api.Platform;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Zharfa.Api.Data;

/// <summary>
/// Production first-run accounts (research R12): when the install runs in
/// production mode and <c>app_user</c> is empty, the Admin and Manager
/// accounts are created from <c>Bootstrap__AdminUsername/Password</c> and
/// <c>Bootstrap__ManagerUsername/Password</c>. Passwords are at least 12
/// characters, hashed with the existing <see cref="PasswordHasher"/> and
/// never logged. Missing or too-short values refuse startup with a clear
/// message (nothing is created in that case).
/// </summary>
public static class BootstrapAccounts
{
    public const int MinPasswordLength = 12;

    public sealed record BootstrapValues(
        string AdminUsername,
        string AdminPassword,
        string ManagerUsername,
        string ManagerPassword);

    /// <summary>Pure: reads and validates the four Bootstrap keys (null = valid).</summary>
    public static string? Validate(IConfiguration configuration)
    {
        var adminUser = ConfigurationKeys.Get(configuration, "Bootstrap__AdminUsername")?.Trim();
        var adminPw = ConfigurationKeys.Get(configuration, "Bootstrap__AdminPassword");
        var managerUser = ConfigurationKeys.Get(configuration, "Bootstrap__ManagerUsername")?.Trim();
        var managerPw = ConfigurationKeys.Get(configuration, "Bootstrap__ManagerPassword");

        if (string.IsNullOrWhiteSpace(adminUser) || string.IsNullOrWhiteSpace(adminPw) ||
            string.IsNullOrWhiteSpace(managerUser) || string.IsNullOrWhiteSpace(managerPw))
        {
            return "در حالت production باید Bootstrap__AdminUsername، Bootstrap__AdminPassword، " +
                   "Bootstrap__ManagerUsername و Bootstrap__ManagerPassword تنظیم شده باشند " +
                   "(زمانی که جدول app_user خالی است). " +
                   "In production mode Bootstrap__AdminUsername/Password and Bootstrap__ManagerUsername/Password " +
                   "are required when app_user is empty.";
        }

        if (adminPw!.Length < MinPasswordLength || managerPw!.Length < MinPasswordLength)
        {
            return $"گذرواژه‌های Bootstrap باید حداقل {MinPasswordLength} کاراکتر باشند. " +
                   $"Bootstrap passwords must be at least {MinPasswordLength} characters long.";
        }

        return null;
    }

    /// <summary>
    /// Creates the two accounts when required. Returns true when accounts
    /// were created. Throws <see cref="InvalidOperationException"/> (startup
    /// refusal) when production needs the values and they are missing/short.
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

        var anyUsers = await db.AppUsers.AsNoTracking().AnyAsync(cancellationToken);
        if (anyUsers)
        {
            return false;
        }

        var error = Validate(configuration);
        if (error is not null)
        {
            logger.LogCritical("Bootstrap account configuration is incomplete: {Error}", RedactionHelper.Scrub(error));
            throw new InvalidOperationException(error);
        }

        var values = new BootstrapValues(
            ConfigurationKeys.Get(configuration, "Bootstrap__AdminUsername")!.Trim(),
            ConfigurationKeys.Get(configuration, "Bootstrap__AdminPassword")!,
            ConfigurationKeys.Get(configuration, "Bootstrap__ManagerUsername")!.Trim(),
            ConfigurationKeys.Get(configuration, "Bootstrap__ManagerPassword")!);

        db.AppUsers.Add(new AppUser
        {
            Username = values.AdminUsername,
            PasswordHash = PasswordHasher.Hash(values.AdminPassword),
            Role = "admin",
            IsActive = true
        });
        db.AppUsers.Add(new AppUser
        {
            Username = values.ManagerUsername,
            PasswordHash = PasswordHasher.Hash(values.ManagerPassword),
            Role = "manager",
            IsActive = true
        });
        await db.SaveChangesAsync(cancellationToken);

        // Usernames only — passwords are never logged (AGENTS.md secrets rule).
        logger.LogInformation(
            "Production bootstrap accounts created: admin '{AdminUsername}', manager '{ManagerUsername}'.",
            RedactionHelper.Scrub(values.AdminUsername),
            RedactionHelper.Scrub(values.ManagerUsername));
        return true;
    }
}
