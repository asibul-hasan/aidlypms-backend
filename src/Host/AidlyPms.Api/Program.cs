using System.Text.Json;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using Npgsql;

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.AddScoped<IDocumentNumberService, DocumentNumberService>();
builder.Services.AddScoped<ILedgerPostingService, LedgerPostingService>();
builder.Services.AddScoped<IStockPostingService, StockPostingService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();


app.MapGet("/api/health/db", async (IConfiguration config) =>
{
    var connectionString = config.GetConnectionString("DefaultConnection");
    try
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT version();", conn);
        var version = await cmd.ExecuteScalarAsync();

        await using var tableCmd = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;", conn);
        await using var reader = await tableCmd.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return Results.Ok(new { status = "Connected", version = version?.ToString(), tableCount = tables.Count, tables });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Database Connection Failed");
    }
});

app.MapPost("/api/admin/run-schema", async (IConfiguration config) =>
{
    var connectionString = config.GetConnectionString("DefaultConnection");
    var sqlPath = @"c:\pridesys\apps\sme\aidlyErp\sme-software-frontend\apps\aidlyPms\database\schema.sql";
    if (!File.Exists(sqlPath))
    {
        return Results.NotFound(new { error = $"File not found at {sqlPath}" });
    }

    var sql = await File.ReadAllTextAsync(sqlPath);
    try
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 120;
        await cmd.ExecuteNonQueryAsync();

        // Get table count
        await using var countCmd = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';", conn);
        var tableCount = await countCmd.ExecuteScalarAsync();

        return Results.Ok(new { status = "Schema applied successfully", tableCount });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.ToString(), title: "Schema Execution Failed");
    }
});

app.Run();

