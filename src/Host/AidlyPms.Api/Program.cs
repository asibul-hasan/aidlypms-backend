using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using AidlyPms.Api.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Threading.RateLimiting;
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
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var address in (builder.Configuration["TRUSTED_PROXY_ADDRESSES"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!IPAddress.TryParse(address, out var proxy)) throw new InvalidOperationException("TRUSTED_PROXY_ADDRESSES must contain trusted proxy IP addresses.");
        options.KnownProxies.Add(proxy);
    }
});
builder.Services.AddScoped<IDocumentNumberService, DocumentNumberService>();
builder.Services.AddScoped<ILedgerPostingService, LedgerPostingService>();
builder.Services.AddScoped<IStockPostingService, StockPostingService>();
builder.Services.AddScoped<ISalePostingEngine, SalePostingEngine>();
builder.Services.AddScoped<IPaymentAccountResolver, PaymentAccountResolver>();
builder.Services.AddScoped<IPharmacyChartSeeder, PharmacyChartSeeder>();
builder.Services.AddScoped<IStockIntegrityService, StockIntegrityService>();
builder.Services.AddSingleton<IExpiryMaintenanceService, ExpiryMaintenanceService>();

var runBgJobsStr = builder.Configuration["RUN_BACKGROUND_JOBS"] ?? Environment.GetEnvironmentVariable("RUN_BACKGROUND_JOBS");
var runBgJobs = !string.Equals(runBgJobsStr, "false", StringComparison.OrdinalIgnoreCase);
if (runBgJobs)
{
    builder.Services.AddHostedService(sp => (ExpiryMaintenanceService)sp.GetRequiredService<IExpiryMaintenanceService>());
}

// Fail closed: deployment must supply a fresh private signing key.
builder.Services.AddSingleton<JwtSettings>(services => JwtSettings.Load(services.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<IAuthSessions, AuthSessions>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "AidlyPms.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = builder.Environment.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
.Configure<JwtSettings>((options, jwtSettings) =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = false;
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(jwtSettings.SigningKey),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateLifetime = true,
        ValidAlgorithms = new[] { Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256 },
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (!context.Request.Headers.ContainsKey("Authorization") && context.Request.Cookies.TryGetValue(BrowserSession.CookieName, out var cookie))
                context.Token = cookie;
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            try
            {
                var access = await context.HttpContext.RequestServices.GetRequiredService<IAuthSessions>()
                    .ValidateAsync(context.Principal!, context.HttpContext.RequestAborted);
                if (access == null) context.Fail("Session is no longer valid.");
                else context.HttpContext.Items[RequestAccess.ItemKey] = access;
            }
            catch (Exception ex)
            {
                context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Authentication")
                    .LogError(ex, "Session validation unavailable.");
                context.Fail("Session validation unavailable.");
            }
        }
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
var configuredOrigins = builder.Environment.IsDevelopment() ? builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() : null;
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
        options.Filters.Add<BrowserRequestValidationFilter>();
        options.Filters.Add<PlatformIdentityPrivacyFilter>();
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
_ = app.Services.GetRequiredService<JwtSettings>(); // Validate final host configuration before serving any requests.

if (allowedOrigins.Length == 0)
{
    app.Logger.LogWarning("No CORS AllowedOrigins configured! Cross-origin requests will not be permitted.");
}

// Item 8: Global Exception Handler Middleware
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseForwardedHeaders();

app.UseCors("ConfiguredOrigins");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
    await next();
});
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
