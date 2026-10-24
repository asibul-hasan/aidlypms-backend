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
builder.Services.AddScoped<IStockIntegrityService, StockIntegrityService>();
builder.Services.AddSingleton<IExpiryMaintenanceService, ExpiryMaintenanceService>();

var runBgJobsStr = builder.Configuration["RUN_BACKGROUND_JOBS"] ?? Environment.GetEnvironmentVariable("RUN_BACKGROUND_JOBS");
if (string.Equals(runBgJobsStr, "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService(sp => (ExpiryMaintenanceService)sp.GetRequiredService<IExpiryMaintenanceService>());
}

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

// Item 1: CORS allowed origins strictly from configuration / environment
var envOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")
    ?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var configuredOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
var allowedOrigins = (envOrigins != null && envOrigins.Length > 0)
    ? envOrigins
    : (configuredOrigins != null && configuredOrigins.Length > 0 ? configuredOrigins : Array.Empty<string>());

builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredOrigins", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
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

if (allowedOrigins.Length == 0)
{
    app.Logger.LogWarning("No CORS AllowedOrigins configured! Cross-origin requests will not be permitted.");
}

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
app.UseMiddleware<SubscriptionExpiryMiddleware>();

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
        await using var cmd = new NpgsqlCommand("SELECT 1;", conn);
        await cmd.ExecuteScalarAsync();

        return Results.Ok(new { status = "Connected" });
    }
    catch (Exception)
    {
        return Results.Json(new { status = "Unavailable" }, statusCode: 503);
    }
}).AllowAnonymous();

// Run pending database migrations automatically on startup
await DatabaseMigrationRunner.ApplyMigrationsAsync(app.Services, app.Logger);

app.Run();

public partial class Program { }
