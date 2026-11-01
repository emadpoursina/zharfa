namespace Zharfa.Api.Data;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

/// <summary>
/// Forward-only SQL migration runner for PostgreSQL.
/// Discovers migrations/ and seed/ directories, maintains schema_migrations,
/// and applies pending migrations sequentially in individual transactions.
/// Never logs connection strings, passwords, or secrets.
/// </summary>
public static class MigrationRunner
{
    public static async Task RunAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var dbRoot = ResolveDbRoot(logger);
        var migrationsDir = Path.Combine(dbRoot, "migrations");

        var connectionString = configuration.GetConnectionString("Zharfa");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogError("Connection string 'Zharfa' is not configured.");
            throw new InvalidOperationException("Connection string 'Zharfa' is missing.");
        }

        int appliedCount = 0;

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);

            // Ensure schema_migrations table exists
            const string createTableSql = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    name text PRIMARY KEY,
                    applied_at timestamptz NOT NULL DEFAULT now()
                );
                """;

            await using (var cmd = new NpgsqlCommand(createTableSql, conn))
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Retrieve previously applied migration and seed names
            var applied = new HashSet<string>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand("SELECT name FROM schema_migrations;", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            // Apply migrations/*.sql in ascending filename order
            var migrationFiles = Directory.GetFiles(migrationsDir, "*.sql")
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                .ToArray();

            foreach (var file in migrationFiles)
            {
                var fileName = Path.GetFileName(file);
                if (applied.Contains(fileName))
                {
                    continue;
                }

                string sql;
                try
                {
                    sql = await File.ReadAllTextAsync(file, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError("Failed reading migration file '{FileName}' [{ExceptionType}]: {ErrorMessage}", fileName, ex.GetType().Name, RedactionHelper.Scrub(ex.Message));
                    throw;
                }

                await using var tx = await conn.BeginTransactionAsync(cancellationToken);
                try
                {
                    await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                    {
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var recordCmd = new NpgsqlCommand("INSERT INTO schema_migrations (name) VALUES (@name);", conn, tx))
                    {
                        recordCmd.Parameters.AddWithValue("name", fileName);
                        await recordCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await tx.CommitAsync(cancellationToken);
                    applied.Add(fileName);
                    appliedCount++;
                    logger.LogInformation("Applied migration: {FileName}", fileName);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(cancellationToken);
                    logger.LogError("Failed applying migration file '{FileName}' [{ExceptionType}]: {ErrorMessage}", fileName, ex.GetType().Name, RedactionHelper.Scrub(ex.Message));
                    throw;
                }
            }

            // Seed step: only run when app_user exists and has 0 rows
            bool appUserExists = false;
            await using (var regCmd = new NpgsqlCommand("SELECT to_regclass('app_user') IS NOT NULL;", conn))
            {
                var regResult = await regCmd.ExecuteScalarAsync(cancellationToken);
                if (regResult is bool exists && exists)
                {
                    appUserExists = true;
                }
            }

            if (!appUserExists)
            {
                logger.LogInformation("Seed skipped: app_user table does not exist.");
            }
            else
            {
                long userCount = 0;
                await using (var countCmd = new NpgsqlCommand("SELECT count(*) FROM app_user;", conn))
                {
                    var countResult = await countCmd.ExecuteScalarAsync(cancellationToken);
                    if (countResult is long l) userCount = l;
                    else if (countResult is int i) userCount = i;
                    else if (countResult != null) userCount = Convert.ToInt64(countResult);
                }

                if (userCount == 0)
                {
                    var seedDir = Path.Combine(dbRoot, "seed");
                    if (Directory.Exists(seedDir))
                    {
                        var seedFiles = Directory.GetFiles(seedDir, "*.sql")
                            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                            .ToArray();

                        foreach (var file in seedFiles)
                        {
                            var fileName = Path.GetFileName(file);
                            if (applied.Contains(fileName))
                            {
                                continue;
                            }

                            string sql;
                            try
                            {
                                sql = await File.ReadAllTextAsync(file, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                logger.LogError("Failed reading seed file '{FileName}' [{ExceptionType}]: {ErrorMessage}", fileName, ex.GetType().Name, RedactionHelper.Scrub(ex.Message));
                                throw;
                            }

                            await using var tx = await conn.BeginTransactionAsync(cancellationToken);
                            try
                            {
                                await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                                {
                                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                                }

                                await using (var recordCmd = new NpgsqlCommand("INSERT INTO schema_migrations (name) VALUES (@name);", conn, tx))
                                {
                                    recordCmd.Parameters.AddWithValue("name", fileName);
                                    await recordCmd.ExecuteNonQueryAsync(cancellationToken);
                                }

                                await tx.CommitAsync(cancellationToken);
                                applied.Add(fileName);
                                appliedCount++;
                                logger.LogInformation("Applied seed: {FileName}", fileName);
                            }
                            catch (Exception ex)
                            {
                                await tx.RollbackAsync(cancellationToken);
                                logger.LogError("Failed applying seed file '{FileName}' [{ExceptionType}]: {ErrorMessage}", fileName, ex.GetType().Name, RedactionHelper.Scrub(ex.Message));
                                throw;
                            }
                        }
                    }
                }
                else
                {
                    logger.LogInformation("Seed skipped: app_user table already contains records.");
                }
            }

            logger.LogInformation("Database migration complete. Total files applied: {Count}.", appliedCount);
        }
        catch (Exception ex)
        {
            logger.LogError("Migration runner terminated with error [{ExceptionType}]: {ErrorMessage}", ex.GetType().Name, RedactionHelper.Scrub(ex.Message));
            throw;
        }
    }

    private static string ResolveDbRoot(ILogger logger)
    {
        var explicitRoot = Environment.GetEnvironmentVariable("Migration__Root");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            if (Directory.Exists(Path.Combine(explicitRoot, "migrations")))
            {
                return explicitRoot;
            }

            if (Directory.Exists(Path.Combine(explicitRoot, "db", "migrations")))
            {
                return Path.Combine(explicitRoot, "db");
            }

            logger.LogError("Configured Migration__Root does not contain a 'migrations/' subdirectory.");
            throw new DirectoryNotFoundException("Migration__Root does not contain a migrations directory.");
        }

        var candidateOrigins = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var origin in candidateOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                continue;
            }

            var current = new DirectoryInfo(origin);
            while (current != null)
            {
                var dbMigrations = Path.Combine(current.FullName, "db", "migrations");
                if (Directory.Exists(dbMigrations))
                {
                    return Path.Combine(current.FullName, "db");
                }

                var directMigrations = Path.Combine(current.FullName, "migrations");
                if (Directory.Exists(directMigrations))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        logger.LogError("Failed to resolve database migrations directory. Set Migration__Root environment variable.");
        throw new DirectoryNotFoundException("Database root directory containing 'migrations/' could not be resolved. Please set Migration__Root.");
    }
}
