using Xunit;

namespace AidlyPms.Api.Tests;

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
}
