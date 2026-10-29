using Dapper;
using Npgsql;
using Xunit;

namespace AidlyPms.Api.Tests;

[Collection("PostgresDatabaseIntegration")]
public class PostgresTestSchemaHarnessTests
{
    [Fact]
    public async Task Guard_RejectsPort6543()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await PostgresTestSchemaHarness.CreateAsync(
                "Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=6543;Database=postgres;Username=user;Password=pass;");
        });

        Assert.Contains("Port 6543 detected (transaction pooler)", ex.Message);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("test_schema")]
    [InlineData("aidly_prod")]
    [InlineData("my_aidly_test")]
    [InlineData("")]
    public void Guard_RejectsInvalidSchemaName(string invalidName)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            PostgresTestSchemaHarness.ValidateSchemaName(invalidName);
        });
    }

    [Fact]
    public void Guard_RejectsDropSchemaPublic()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            PostgresTestSchemaHarness.ValidateDropSchemaName("public");
        });

        Assert.Contains("CRITICAL SAFETY GUARD", ex.Message);
    }

    [Fact]
    public void Guard_AcceptsValidTestSchemaName()
    {
        PostgresTestSchemaHarness.ValidateSchemaName("aidly_test_a1b2c3d4");
        PostgresTestSchemaHarness.ValidateDropSchemaName("aidly_test_a1b2c3d4");
    }

    [Fact]
    public void Guard_RejectsDdlWithPublicQualifier()
    {
        var badDdl = "CREATE TABLE public.sale_invoices (id int);";
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            PostgresTestSchemaHarness.ValidateNoPublicDdl(badDdl);
        });

        Assert.Contains("CRITICAL DDL COLLISION GUARD", ex.Message);
    }

    [Fact]
    public void Guard_AcceptsCleanDdl()
    {
        var cleanDdl = "CREATE TABLE IF NOT EXISTS sale_invoices (sale_invoice_no bigint primary key);";
        PostgresTestSchemaHarness.ValidateNoPublicDdl(cleanDdl);
    }

    [Fact]
    public async Task DbScriptSql_LoadsIntoIsolatedSchema_WithoutErrors()
    {
        var connStr = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connStr)) return;

        var dbScriptPath = PostgresTestSchemaHarness.ResolveDatabaseFilePath("db_script.sql");
        var dbScriptSql = await System.IO.File.ReadAllTextAsync(dbScriptPath);
        PostgresTestSchemaHarness.ValidateNoPublicDdl(dbScriptSql);

        var randomBytes = new byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
        var schemaName = $"aidly_test_{Convert.ToHexString(randomBytes).ToLowerInvariant()}";

        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync();
        try
        {
            await Dapper.SqlMapper.ExecuteAsync(conn, $"CREATE SCHEMA {schemaName};");
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connStr) { SearchPath = schemaName };
            await using var isolatedConn = new Npgsql.NpgsqlConnection(builder.ConnectionString);
            await isolatedConn.OpenAsync();
            await Dapper.SqlMapper.ExecuteAsync(isolatedConn, dbScriptSql);

            var tableCount = await Dapper.SqlMapper.ExecuteScalarAsync<int>(isolatedConn,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = @schemaName;", new { schemaName });
            Assert.Equal(63, tableCount);
        }
        finally
        {
            await Dapper.SqlMapper.ExecuteAsync(conn, $"DROP SCHEMA IF EXISTS {schemaName} CASCADE;");
        }
    }

    [Fact]
    public async Task SchemaSql_LoadsIntoIsolatedSchema_WithoutErrors()
    {
        var connStr = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connStr)) return;

        var schemaPath = PostgresTestSchemaHarness.ResolveDatabaseFilePath("schema.sql");
        var schemaSql = await System.IO.File.ReadAllTextAsync(schemaPath);
        PostgresTestSchemaHarness.ValidateNoPublicDdl(schemaSql);

        var randomBytes = new byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
        var schemaName = $"aidly_test_{Convert.ToHexString(randomBytes).ToLowerInvariant()}";

        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync();
        try
        {
            await Dapper.SqlMapper.ExecuteAsync(conn, $"CREATE SCHEMA {schemaName};");
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connStr) { SearchPath = schemaName };
            await using var isolatedConn = new Npgsql.NpgsqlConnection(builder.ConnectionString);
            await isolatedConn.OpenAsync();
            await Dapper.SqlMapper.ExecuteAsync(isolatedConn, schemaSql);

            var tableCount = await Dapper.SqlMapper.ExecuteScalarAsync<int>(isolatedConn,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = @schemaName;", new { schemaName });
            Assert.Equal(63, tableCount);
        }
        finally
        {
            await Dapper.SqlMapper.ExecuteAsync(conn, $"DROP SCHEMA IF EXISTS {schemaName} CASCADE;");
        }
    }
    [Fact]
    public async Task AuthenticationMigration_UpgradesOldSchemaIdempotentlyWithoutChangingPasswords()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var schemaName = "aidly_test_" + Guid.NewGuid().ToString("N")[..8];
        PostgresTestSchemaHarness.ValidateSchemaName(schemaName);
        await using var baseConnection = new NpgsqlConnection(connectionString);
        await baseConnection.OpenAsync();
        await baseConnection.ExecuteAsync($"CREATE SCHEMA {schemaName};");
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schemaName };
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            var schemaPath = PostgresTestSchemaHarness.ResolveDatabaseFilePath("schema.sql");
            var current = await File.ReadAllTextAsync(schemaPath);
            var oldSchema = current.Split("-- Authentication and query hardening (20261029_18)")[0];
            await connection.ExecuteAsync(oldSchema);
            await connection.ExecuteAsync(@"
                INSERT INTO sys_users (user_no, uuid, pharmacy_no, branch_no, role_no, first_name, last_name, phone, email, password_hash, is_active, created_at, updated_at)
                SELECT 990019, gen_random_uuid(), pharmacy_no, branch_no, role_no, 'Migration', 'Probe', 'migration-probe', 'migration@example.test', @Hash, TRUE, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM sys_roles WHERE role_no = 1;", new { Hash = BCrypt.Net.BCrypt.HashPassword("SyntheticMigrationPassword!") });
            var hashesBefore = (await connection.QueryAsync<(long UserNo, string PasswordHash)>("SELECT user_no, password_hash FROM sys_users ORDER BY user_no;")).ToArray();
            Assert.NotEmpty(hashesBefore);
            await connection.ExecuteAsync("SELECT setval('seq_product_no', 990050, true);");
            var migration = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(schemaPath)!, "migrations", "20261029_18_authentication_and_query_hardening.sql"));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await connection.ExecuteAsync(migration, transaction: transaction);
                await transaction.CommitAsync();
            }
            var hashesAfter = (await connection.QueryAsync<(long UserNo, string PasswordHash)>("SELECT user_no, password_hash FROM sys_users ORDER BY user_no;")).ToArray();
            Assert.True(hashesBefore.SequenceEqual(hashesAfter));
            Assert.Equal(990051L, await connection.ExecuteScalarAsync<long>("SELECT nextval('seq_product_no');"));
            Assert.True(await connection.ExecuteScalarAsync<long>("SELECT nextval('seq_sys_user_no');") > 990019);
            Assert.Equal("PLATFORM_OWNER", await connection.ExecuteScalarAsync<string>("SELECT access_scope FROM sys_role_capabilities WHERE role_no = 1;"));
            Assert.True(await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = 'sys_auth_sessions');"));
            Assert.False(await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_indexes WHERE schemaname = current_schema() AND indexname = 'idx_prod_products_lookup');"));
        }
        finally
        {
            PostgresTestSchemaHarness.ValidateDropSchemaName(schemaName);
            await baseConnection.ExecuteAsync($"DROP SCHEMA {schemaName} CASCADE;");
        }
    }

}
