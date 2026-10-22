using System.Reflection;
using AidlyPms.Api.Common.Database;
using Dapper;
using Npgsql;

namespace AidlyPms.Api.Infrastructure;

public static class DatabaseMigrationRunner
{
    public static async Task ApplyMigrationsAsync(IServiceProvider services, ILogger logger)
    {
        try
        {
            using var scope = services.CreateScope();
            var dbFactory = scope.ServiceProvider.GetService<IDbConnectionFactory>();
            if (dbFactory == null)
            {
                return;
            }

            await using var conn = await dbFactory.CreateOpenConnectionAsync();

            // 1. Ensure migrations tracking table exists
            const string createHistoryTableSql = @"
                CREATE TABLE IF NOT EXISTS __schema_migrations (
                    migration_id VARCHAR(255) PRIMARY KEY,
                    applied_at TIMESTAMP WITH TIME ZONE NOT NULL
                );";
            await conn.ExecuteAsync(createHistoryTableSql);

            var appliedMigrations = (await conn.QueryAsync<string>(
                "SELECT migration_id FROM __schema_migrations;"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 2. Discover embedded migration resources
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

                if (appliedMigrations.Contains(migrationFileName))
                {
                    continue;
                }

                logger.LogInformation("Applying database migration: {MigrationName}...", migrationFileName);

                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(sql)) continue;

                await conn.ExecuteAsync(sql);

                await conn.ExecuteAsync(
                    "INSERT INTO __schema_migrations (migration_id, applied_at) VALUES (@migrationFileName, CURRENT_TIMESTAMP) ON CONFLICT (migration_id) DO NOTHING;",
                    new { migrationFileName });

                logger.LogInformation("Successfully applied database migration: {MigrationName}.", migrationFileName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database migration runner completed with warnings or was skipped.");
        }
    }
}
