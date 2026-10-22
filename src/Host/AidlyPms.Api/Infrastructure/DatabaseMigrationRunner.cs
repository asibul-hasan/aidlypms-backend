using System.Reflection;
using System.Text.RegularExpressions;
using AidlyPms.Api.Common.Database;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AidlyPms.Api.Infrastructure;

public record MigrationRecord(string MigrationId, DateTime AppliedAt);

public static class DatabaseMigrationRunner
{
    public const long AdvisoryLockId = 849201948271049281L;

    private static readonly List<MigrationRecord> _appliedMigrations = new();
    private static readonly object _lock = new();

    public static IReadOnlyList<MigrationRecord> AppliedMigrations
    {
        get
        {
            lock (_lock)
            {
                return _appliedMigrations.ToList().AsReadOnly();
            }
        }
    }

    private static string? _lastError;
    public static string? LastError
    {
        get => _lastError;
        set => _lastError = value != null ? SanitizeMessage(value) : null;
    }

    public static async Task ApplyMigrationsAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetService<IDbConnectionFactory>();
        if (dbFactory == null)
        {
            return;
        }

        var config = scope.ServiceProvider.GetService<IConfiguration>();
        var failOpen = string.Equals(Environment.GetEnvironmentVariable("MIGRATIONS_FAIL_OPEN"), "true", StringComparison.OrdinalIgnoreCase)
            || config?.GetValue<bool>("Migrations:FailOpen", false) == true;

        NpgsqlConnection? conn = null;
        var lockAcquired = false;

        try
        {
            conn = await dbFactory.CreateOpenConnectionAsync();

            // 1. Take PostgreSQL advisory lock before executing migrations
            logger.LogInformation("Acquiring PostgreSQL advisory lock {LockId} for database migrations...", AdvisoryLockId);
            await conn.ExecuteAsync("SELECT pg_advisory_lock(@lockId);", new { lockId = AdvisoryLockId });
            lockAcquired = true;

            // 2. Ensure schema migrations tracking table exists
            const string createHistoryTableSql = @"
                CREATE TABLE IF NOT EXISTS __schema_migrations (
                    migration_id VARCHAR(255) PRIMARY KEY,
                    applied_at TIMESTAMP WITH TIME ZONE NOT NULL
                );";
            await conn.ExecuteAsync(createHistoryTableSql);

            var rows = (await conn.QueryAsync<MigrationRecord>(
                "SELECT migration_id, applied_at FROM __schema_migrations ORDER BY applied_at ASC;")).ToList();

            lock (_lock)
            {
                _appliedMigrations.Clear();
                _appliedMigrations.AddRange(rows);
            }

            var appliedIds = rows.Select(r => r.MigrationId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 3. Discover embedded migration resources
            var assembly = typeof(DatabaseMigrationRunner).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(r => r.StartsWith("AidlyPms.Api.Migrations.", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (resourceNames.Count == 0)
            {
                logger.LogInformation("No embedded database migrations discovered.");
                return;
            }

            foreach (var resource in resourceNames)
            {
                var migrationFileName = resource.Replace("AidlyPms.Api.Migrations.", "", StringComparison.OrdinalIgnoreCase);

                if (appliedIds.Contains(migrationFileName))
                {
                    continue;
                }

                logger.LogInformation("Applying database migration: {MigrationName}...", migrationFileName);

                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(sql)) continue;

                // 4. Run each migration file in its own transaction together with its __schema_migrations insert
                await using var tran = await conn.BeginTransactionAsync();
                try
                {
                    await conn.ExecuteAsync(sql, transaction: tran);
                    await conn.ExecuteAsync(
                        "INSERT INTO __schema_migrations (migration_id, applied_at) VALUES (@migrationFileName, CURRENT_TIMESTAMP) ON CONFLICT (migration_id) DO NOTHING;",
                        new { migrationFileName }, transaction: tran);
                    await tran.CommitAsync();

                    var newRecord = new MigrationRecord(migrationFileName, DateTime.UtcNow);
                    appliedIds.Add(migrationFileName);
                    lock (_lock)
                    {
                        _appliedMigrations.Add(newRecord);
                    }

                    logger.LogInformation("Successfully applied database migration: {MigrationName}.", migrationFileName);
                }
                catch (Exception ex)
                {
                    await tran.RollbackAsync();
                    LastError = $"Migration '{migrationFileName}' failed: {SanitizeMessage(ex.Message)}";
                    logger.LogError(ex, "Failed to apply database migration: {MigrationName}.", migrationFileName);
                    throw;
                }
            }

            LastError = null; // Clear last error on clean completion
        }
        catch (Exception ex)
        {
            LastError = SanitizeMessage(ex.Message);

            if (failOpen)
            {
                logger.LogCritical(ex, "Database migration runner failed, but MIGRATIONS_FAIL_OPEN=true is active. Continuing application startup.");
            }
            else
            {
                logger.LogCritical(ex, "Database migration runner failed! Failing fast and stopping application from starting.");
                throw;
            }
        }
        finally
        {
            // Always release pg_advisory_lock
            if (lockAcquired && conn != null)
            {
                try
                {
                    await conn.ExecuteAsync("SELECT pg_advisory_unlock(@lockId);", new { lockId = AdvisoryLockId });
                    logger.LogInformation("Released PostgreSQL advisory lock {LockId}.", AdvisoryLockId);
                }
                catch (Exception unlockEx)
                {
                    logger.LogWarning(unlockEx, "Failed to release pg_advisory_lock({LockId}).", AdvisoryLockId);
                }
            }

            if (conn != null)
            {
                await conn.DisposeAsync();
            }
        }
    }

    private static string SanitizeMessage(string? msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return "Unknown migration error";
        var sanitized = Regex.Replace(
            msg,
            @"(Password|Pwd|Secret|Key)=[^;]+",
            "$1=[REDACTED]",
            RegexOptions.IgnoreCase);
        return sanitized.Length > 300 ? sanitized.Substring(0, 300) + "..." : sanitized;
    }
}
