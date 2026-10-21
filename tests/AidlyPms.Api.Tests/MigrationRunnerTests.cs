using System.Data;
using Dapper;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace AidlyPms.Api.Tests;

public class MigrationRunnerTests
{
    private readonly ITestOutputHelper _output;
    private const string ConnectionString = "Host=aws-0-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.rjxkplqdubxxxhyubdjq;Password=Asibul@2303;SSL Mode=Require;Trust Server Certificate=true";

    public MigrationRunnerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ApplyAndVerifyDgdaBmdcMigration()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        _output.WriteLine("Connected to PostgreSQL successfully.");

        // 1. Check existing columns before migration
        var prodColumnsBefore = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'prod_products'")).ToList();
        var docColumnsBefore = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'doc_doctors'")).ToList();

        _output.WriteLine($"prod_products existing columns count before: {prodColumnsBefore.Count}");
        _output.WriteLine($"doc_doctors existing columns count before: {docColumnsBefore.Count}");
        _output.WriteLine($"dar_number exists before: {prodColumnsBefore.Contains("dar_number")}");
        _output.WriteLine($"drug_schedule exists before: {prodColumnsBefore.Contains("drug_schedule")}");
        _output.WriteLine($"bmdc_reg_no exists before: {docColumnsBefore.Contains("bmdc_reg_no")}");

        // 2. Apply migration cleanly: plain VARCHAR(50) columns, no DB defaults, no DB check constraints
        var migrationSql = @"
            ALTER TABLE prod_products 
            ADD COLUMN IF NOT EXISTS dar_number VARCHAR(50),
            ADD COLUMN IF NOT EXISTS drug_schedule VARCHAR(50);

            CREATE INDEX IF NOT EXISTS idx_prod_products_dar_number ON prod_products(dar_number);
            CREATE INDEX IF NOT EXISTS idx_prod_products_drug_schedule ON prod_products(drug_schedule);

            ALTER TABLE doc_doctors 
            ADD COLUMN IF NOT EXISTS bmdc_reg_no VARCHAR(50);

            CREATE INDEX IF NOT EXISTS idx_doc_doctors_bmdc_reg_no ON doc_doctors(bmdc_reg_no);
        ";

        await conn.ExecuteAsync(migrationSql);
        _output.WriteLine("Executed 20261021_dgda_bmdc_compliance.sql migration on live database.");

        // 3. Drop any legacy constraint or default if someone ever added it
        await conn.ExecuteAsync(@"
            ALTER TABLE prod_products DROP CONSTRAINT IF EXISTS chk_prod_products_drug_schedule;
            ALTER TABLE prod_products ALTER COLUMN drug_schedule DROP DEFAULT;
        ");

        // 4. Verify columns exist now
        var updatedProdCols = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'prod_products'")).ToHashSet();
        var updatedDocCols = (await conn.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'doc_doctors'")).ToHashSet();

        Assert.True(updatedProdCols.Contains("dar_number"), "dar_number must exist in prod_products");
        Assert.True(updatedProdCols.Contains("drug_schedule"), "drug_schedule must exist in prod_products");
        Assert.True(updatedDocCols.Contains("bmdc_reg_no"), "bmdc_reg_no must exist in doc_doctors");

        // 5. Verify no default exists on drug_schedule
        var drugScheduleDefault = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT column_default FROM information_schema.columns WHERE table_name = 'prod_products' AND column_name = 'drug_schedule'");
        _output.WriteLine($"prod_products.drug_schedule column_default: '{drugScheduleDefault ?? "NULL (No Default)"}'");
        Assert.True(string.IsNullOrEmpty(drugScheduleDefault), "drug_schedule must not have a database default");

        _output.WriteLine("MIGRATION COMPLETED & VERIFIED SUCCESSFULLY ON LIVE DATABASE!");
    }
}
