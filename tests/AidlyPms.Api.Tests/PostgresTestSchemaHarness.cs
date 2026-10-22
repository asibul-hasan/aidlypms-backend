using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace AidlyPms.Api.Tests;

public class PostgresTestSchemaHarness : IAsyncDisposable
{
    public string BaseConnectionString { get; }
    public string IsolatedConnectionString { get; }
    public string SchemaName { get; }

    private readonly HashSet<string> _beforePublicTables;

    private PostgresTestSchemaHarness(string baseConnStr, string isolatedConnStr, string schemaName, HashSet<string> beforePublicTables)
    {
        BaseConnectionString = baseConnStr;
        IsolatedConnectionString = isolatedConnStr;
        SchemaName = schemaName;
        _beforePublicTables = beforePublicTables;
    }

    public static async Task<PostgresTestSchemaHarness> CreateAsync(string? rawConnStr = null)
    {
        var connStr = rawConnStr ?? Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new InvalidOperationException(
                "TEST_POSTGRES_CONNECTION environment variable is NOT set. " +
                "Integration tests require a PostgreSQL test instance (e.g., CI service container or Supabase session-mode pooler) " +
                "and will run strictly within an isolated throwaway schema.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connStr);

        // Guard 1: port 6543 transaction-mode pooler rejection
        if (builder.Port == 6543)
        {
            throw new InvalidOperationException(
                "Port 6543 detected (transaction pooler). TEST_POSTGRES_CONNECTION must use session pooler or direct connection on port 5432.");
        }

        // Generate 8 random hex chars
        var randomBytes = new byte[4];
        RandomNumberGenerator.Fill(randomBytes);
        var hex = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var schemaName = $"aidly_test_{hex}";

        // Hard Guard: schema name must start with "aidly_test_"
        ValidateSchemaName(schemaName);

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync();

        // Drop leftover schemas aidly_test_% older than 1 hour at start
        await DropLeftoverSchemasAsync(conn);

        // Pre-run Canary measurement: capture public table set and verify no test pharmacy rows
        var beforePublicTables = await GetPublicTablesAsync(conn);
        await AssertNoTestPharmacyRowsInPublicAsync(conn);

        // Create isolated schema
        await conn.ExecuteAsync($"CREATE SCHEMA {schemaName};");

        // Clone connection string with Search Path
        var isolatedBuilder = new NpgsqlConnectionStringBuilder(connStr)
        {
            SearchPath = schemaName
        };
        var isolatedConnStr = isolatedBuilder.ConnectionString;

        // Load schema.sql into isolated schema
        var schemaPath = ResolveDatabaseFilePath("schema.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath);

        // Hard Guard: refuse any DDL line that contains schema-qualified "public."
        ValidateNoPublicDdl(schemaSql);

        await using (var isolatedConn = new NpgsqlConnection(isolatedConnStr))
        {
            await isolatedConn.OpenAsync();
            await isolatedConn.ExecuteAsync(schemaSql);

            // Hard Guard: assert SELECT current_schema() equals test schema
            var currentSchema = await isolatedConn.ExecuteScalarAsync<string>("SELECT current_schema();");
            if (!string.Equals(currentSchema, schemaName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Schema isolation assertion failed! Expected current_schema() to be '{schemaName}', but was '{currentSchema}'.");
            }
        }

        return new PostgresTestSchemaHarness(connStr, isolatedConnStr, schemaName, beforePublicTables);
    }

    public static void ValidateSchemaName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("aidly_test_", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Schema name must start with 'aidly_test_'. Invalid name: '{name}'", nameof(name));
        }

        if (name.Equals("public", StringComparison.OrdinalIgnoreCase) || name.Contains(';') || name.Contains(' ') || name.Contains('-'))
        {
            throw new ArgumentException($"Invalid or unsafe schema name: '{name}'", nameof(name));
        }
    }

    public static void ValidateDropSchemaName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("public", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("CRITICAL SAFETY GUARD: Attempted to execute DROP SCHEMA on 'public' or invalid schema!");
        }
        ValidateSchemaName(name);
    }

    public static void ValidateNoPublicDdl(string ddl)
    {
        var lines = ddl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--")) continue;

            if (Regex.IsMatch(trimmed, @"\bpublic\.[a-zA-Z0-9_]+", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CRITICAL DDL COLLISION GUARD: DDL contains schema-qualified 'public.' reference: '{trimmed}'. " +
                    "All DDL must be schema-agnostic to support isolated test schemas.");
            }
        }
    }

    private static async Task DropLeftoverSchemasAsync(NpgsqlConnection conn)
    {
        try
        {
            var leftoverSchemas = (await conn.QueryAsync<string>(
                "SELECT schema_name FROM information_schema.schemata WHERE schema_name LIKE 'aidly_test_%';")).ToList();

            foreach (var s in leftoverSchemas)
            {
                try
                {
                    var createdAt = await conn.ExecuteScalarAsync<DateTime?>(
                        $"SELECT created_at FROM {s}.sys_pharmacies LIMIT 1;");

                    if (createdAt.HasValue && createdAt.Value < DateTime.UtcNow.AddHours(-1))
                    {
                        ValidateDropSchemaName(s);
                        await conn.ExecuteAsync($"DROP SCHEMA IF EXISTS {s} CASCADE;");
                    }
                }
                catch
                {
                    // Suppress individual schema cleanup failures
                }
            }
        }
        catch
        {
            // Non-blocking cleanup
        }
    }

    public static async Task<HashSet<string>> GetPublicTablesAsync(NpgsqlConnection conn)
    {
        try
        {
            var tables = await conn.QueryAsync<string>(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';");
            return tables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static async Task AssertNoTestPharmacyRowsInPublicAsync(NpgsqlConnection conn)
    {
        try
        {
            var tablesWithPharmacyNo = (await conn.QueryAsync<string>(@"
                SELECT table_name
                FROM information_schema.columns
                WHERE table_schema = 'public' AND column_name = 'pharmacy_no';")).ToList();

            foreach (var table in tablesWithPharmacyNo)
            {
                var count = await conn.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM public.\"{table}\" WHERE pharmacy_no >= 9000;");
                if (count > 0)
                {
                    throw new InvalidOperationException(
                        $"CANARY VIOLATION: Found {count} test rows (pharmacy_no >= 9000) leaked into public.\"{table}\"!");
                }
            }
        }
        catch (Exception ex) when (ex.Message.Contains("CANARY VIOLATION"))
        {
            throw;
        }
        catch
        {
            // Suppress schema query lookup errors if public catalog is empty
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(BaseConnectionString);
            await conn.OpenAsync();

            ValidateDropSchemaName(SchemaName);
            await conn.ExecuteAsync($"DROP SCHEMA IF EXISTS {SchemaName} CASCADE;");

            var afterTables = await GetPublicTablesAsync(conn);
            if (!afterTables.SetEquals(_beforePublicTables))
            {
                var added = afterTables.Except(_beforePublicTables).ToList();
                var removed = _beforePublicTables.Except(afterTables).ToList();
                throw new InvalidOperationException(
                    $"CANARY VIOLATION: Public tables set was altered during test execution! " +
                    $"Added: [{string.Join(", ", added)}], Removed: [{string.Join(", ", removed)}].");
            }

            await AssertNoTestPharmacyRowsInPublicAsync(conn);
        }
        catch (Exception ex) when (!ex.Message.Contains("CANARY VIOLATION"))
        {
            // Suppress cleanup connection teardown exceptions unless canary violation
        }
    }

    public static string ResolveDatabaseFilePath(string relativePath)
    {
        var current = AppContext.BaseDirectory;
        for (int i = 0; i < 7; i++)
        {
            var candidate1 = Path.Combine(current, "database", relativePath);
            if (File.Exists(candidate1)) return candidate1;

            var candidate2 = Path.Combine(current, "apps", "aidlyPms", "database", relativePath);
            if (File.Exists(candidate2)) return candidate2;

            var candidate3 = Path.Combine(current, "aidlypms-backend", "database", relativePath);
            if (File.Exists(candidate3)) return candidate3;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        throw new FileNotFoundException($"Could not locate database file: '{relativePath}'. Checked upwards from {AppContext.BaseDirectory}.");
    }
}
