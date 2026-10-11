using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AssetPro.Api.Auth;
using AssetPro.Api.Common;
using AssetPro.Api.Data;
using AssetPro.Api.Domain;
using AssetPro.Api.Features.Assets;
using AssetPro.Api.Features.Requests;
using AssetPro.Api.Infrastructure;
using AssetPro.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging: console + SQLite application log ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<SqliteLogQueue>();
builder.Services.AddSingleton<ILoggerProvider, SqliteLoggerProvider>();
builder.Services.AddHostedService<SqliteLogWriter>();

// ---------- Data ----------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=assetpro.db"));

// ---------- Auth ----------
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be configured with at least 32 characters (use user-secrets or an environment variable).");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwt.SigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Email,
            RoleClaimType = ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            // Reject tokens of disabled users or tokens issued before a role/permission/password change.
            OnTokenValidated = async context =>
            {
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var id = int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : 0;
                var stamp = context.Principal?.FindFirstValue(CurrentUser.StampClaim);
                var user = await db.Users.AsNoTracking().Where(u => u.Id == id)
                    .Select(u => new { u.IsActive, u.SecurityStamp }).FirstOrDefaultAsync();
                if (user is null || !user.IsActive || user.SecurityStamp != stamp)
                    context.Fail("Session is no longer valid.");
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
            ctx.User.IsInRole(nameof(UserRole.Admin)) || ctx.User.HasClaim(Permissions.ClaimType, permission)));
});

var loginPermits = builder.Configuration.GetValue("RateLimit:LoginPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiError(429, "Too many requests", "Too many sign-in attempts. Please wait a minute and try again.", context.HttpContext.TraceIdentifier), ct);
    };
});

// ---------- App services ----------
builder.Services.AddScoped<ActivityLogger>();
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<RecordDeletionService>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddScoped<DemoSeeder>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    })
    .ConfigureApiBehaviorOptions(o =>
    {
        // Model validation failures use the same error shape as everything else.
        o.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Where(e => e.Value?.Errors.Count > 0).ToDictionary(
                e => string.IsNullOrEmpty(e.Key) ? "body" : char.ToLowerInvariant(e.Key[0]) + e.Key[1..].Replace("$.", ""),
                e => e.Value!.Errors.Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? "Invalid value." : x.ErrorMessage).ToArray());
            var first = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "Please check the highlighted fields.";
            return new BadRequestObjectResult(new ApiError(400, "Validation failed", first, context.HttpContext.TraceIdentifier, errors));
        };
    });

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
// Uniform JSON bodies for bare 401/403/404 produced by the framework (not by our exceptions).
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (response.ContentLength is > 0 || response.ContentType is not null) return;
    var message = response.StatusCode switch
    {
        401 => "Your session has expired. Please sign in again.",
        403 => "You do not have permission to perform this action.",
        404 => "The requested resource was not found.",
        _ => "The request could not be completed."
    };
    await response.WriteAsJsonAsync(new ApiError(response.StatusCode, "Error", message, context.HttpContext.TraceIdentifier));
});

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow })).AllowAnonymous();

// ---------- Database: migrate + seed ----------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    var seedDemo = app.Configuration.GetValue("Seed:DemoData", true);
    // Days of simulated day-to-day activity to generate on a fresh database (about ten events per day).
    var historyDays = app.Configuration.GetValue("Seed:HistoryDays", HistorySimulator.DefaultDays);
    await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync(seedDemo, historyDays);
}

app.Run();

public partial class Program;
