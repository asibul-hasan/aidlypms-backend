using System.Text.Json;
using AidlyPms.Api.Common;
using AidlyPms.Api.Common.Database;
using AidlyPms.Api.Infrastructure;
using AidlyPms.Api.Modules.Sales.Services;
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
builder.Services.AddScoped<ISalePostingEngine, SalePostingEngine>();

// JWT Authentication
var jwtConfig = builder.Configuration.GetSection("Jwt");
var secretKey = jwtConfig["Key"] ?? "AidlyPMS_SuperSecret_Jwt_SigningKey_2026_Enterprise_Secure!";
var keyBytes = System.Text.Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes),
        ValidateIssuer = true,
        ValidIssuer = jwtConfig["Issuer"] ?? "AidlyPmsBackend",
        ValidateAudience = true,
        ValidAudience = jwtConfig["Audience"] ?? "AidlyPmsFrontend",
        ClockSkew = TimeSpan.Zero
    };
});

// Item 1: FallbackPolicy = RequireAuthenticatedUser
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Item 9: CORS allowed origins from configuration only (no AllowAnyOrigin)
var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200", "http://localhost:4202", "http://localhost:4203" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddControllers(options =>
    {
        options.ModelBinderProviders.Insert(0, new FlexibleDateTimeModelBinderProvider());
        options.Filters.Add<AuditActionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new FlexibleDateTimeJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new FlexibleNullableDateTimeJsonConverter());
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Item 8: Global Exception Handler Middleware
app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseCors("ConfiguredOrigins");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health endpoints kept AllowAnonymous
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

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
    catch (Exception)
    {
        return Results.Problem(detail: "Database connection failed. Please contact administrator.", title: "Database Connection Failed");
    }
}).AllowAnonymous();

app.Run();
