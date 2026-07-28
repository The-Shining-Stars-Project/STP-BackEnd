using System.Text;
using System.Threading.RateLimiting;
using CRM.API;
using CRM.Application;
using CRM.Application.Interfaces;
using CRM.Infrastructure;
using CRM.Infrastructure.Auth;
using CRM.Persistence;
using CRM.Persistence.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Don't advertise the server implementation (#7).
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// HSTS (#7): once a browser has seen this, it refuses to talk to the API over plain HTTP.
// No preload and no includeSubDomains — the app lives on a shared azurewebsites.net suffix
// and neither is needed here. UseHsts() below already skips localhost.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

// ---------------------------------------------------------
// Register services from each layer via extension methods
// ---------------------------------------------------------
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddPersistenceServices(builder.Configuration);

// ---------------------------------------------------------
// Controllers + problem details
// ---------------------------------------------------------
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ---------------------------------------------------------
// JWT authentication
// ---------------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException("Missing configuration: Jwt:Key — set the Jwt__Key environment variable in Azure App Service.");

// Outside Development, refuse to run on the publicly-known placeholder key (it was
// committed to the repo, so anyone who has seen it could forge tokens) or on any
// key too short for HS256.
const string DevPlaceholderJwtKey = "dev-only-super-secret-signing-key-change-me-32+chars";
if (!builder.Environment.IsDevelopment()
    && (jwtKey == DevPlaceholderJwtKey || Encoding.UTF8.GetByteCount(jwtKey) < 32))
    throw new InvalidOperationException(
        "Refusing to start: Jwt:Key is the development placeholder or shorter than 32 bytes. "
        + "Set Jwt__Key to a long random secret (e.g. 'openssl rand -base64 48') in Azure App Service.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep token claim names verbatim ("sub", "role", "name") instead of
        // remapping them to the long WS-* URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"] ?? "ShinyStarCRM",
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"] ?? "ShinyStarCRM",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = TokenService.NameClaim,
            RoleClaimType = TokenService.RoleClaim,
        };

        // #20: bearer tokens live 8 hours and cannot be revoked, so deactivating a user
        // (IsActive = false) would otherwise leave their token working until it expires.
        // Check IsActive on every authenticated request — one indexed PK lookup.
        options.Events = new JwtBearerEvents
        {
            // #15: the JWT normally arrives in an httpOnly cookie. The Authorization
            // header still wins when present (Swagger, scripts, older frontend builds).
            OnMessageReceived = ctx =>
            {
                if (string.IsNullOrEmpty(ctx.Token)
                    && !ctx.Request.Headers.ContainsKey("Authorization")
                    && ctx.Request.Cookies.TryGetValue(CRM.API.Controllers.AuthController.AccessCookie, out var cookieToken))
                {
                    ctx.Token = cookieToken;
                }
                return Task.CompletedTask;
            },

            OnTokenValidated = async ctx =>
            {
                var idClaim = ctx.Principal?.FindFirst("sub")?.Value
                              ?? ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!Guid.TryParse(idClaim, out var userId))
                {
                    ctx.Fail("Token has no valid user id.");
                    return;
                }

                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var isActive = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => (bool?)u.IsActive)
                    .FirstOrDefaultAsync();
                if (isActive != true)
                    ctx.Fail("Account is deactivated or no longer exists.");
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Management-write surfaces (roster, games, calendar themes, focus skills) are open to
    // Admins (user role) and Coordinators/Admins (linked staff role). Teachers are read-only there.
    options.AddPolicy("ManagementWrite", policy => policy.RequireAssertion(ctx =>
        ctx.User.IsInRole("Admin")
        || ctx.User.HasClaim("staffRole", "Coordinator")
        || ctx.User.HasClaim("staffRole", "Admin")));
});

// ---------------------------------------------------------
// Rate limiting (#7) — throttle login to blunt credential stuffing / brute force.
// Partitioned by client IP: at most 10 login attempts per minute per address, no queue
// (excess requests get 429). Applied to the login endpoint via the "login" policy.
// ---------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

// ---------------------------------------------------------
// Swagger / OpenAPI
// ---------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ShinyStarCRM API",
        Version = "v1",
        Description = "ASP.NET Core Web API for the ShinyStarCRM application"
    });

    // Bearer token support in the Swagger UI ("Authorize" button).
    var scheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Paste your JWT here (no 'Bearer ' prefix needed).",
        Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
            Id = "Bearer",
        },
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        [scheme] = Array.Empty<string>(),
    });
});

// ---------------------------------------------------------
// CORS — allow the Next.js frontend (localhost:3000)
// ---------------------------------------------------------
var frontendOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "https://ssp-mock-up.vercel.app"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(frontendOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ---------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ShinyStarCRM API v1");
        options.RoutePrefix = "swagger";
    });
}

// Apply pending migrations on startup (#12).
//
// EF Core 7+ takes a database-level lock (sp_getapplock on SQL Server) for the duration of
// a migration, so parallel instances queue behind each other instead of racing — the
// original concern here is handled by the framework. What remains is that a bad migration
// takes the app down at boot instead of failing a deployment step, so this is now
// switchable: run `dotnet ef database update --project CRM.Persistence --startup-project
// CRM.API` from your release pipeline and set Database:MigrateOnStartup to false
// (Azure App Service setting: Database__MigrateOnStartup). Defaults to true so existing
// deploys keep working untouched.
var migrateOnStartup = builder.Configuration.GetValue("Database:MigrateOnStartup", true);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (migrateOnStartup)
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        // Seeders below assume the schema is current; say so plainly rather than failing
        // later with a confusing "invalid column name".
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"Database:MigrateOnStartup is false but {pending.Count} migration(s) are pending "
                + $"({string.Join(", ", pending)}). Run `dotnet ef database update` before deploying.");
    }

    // The Games Library is real reference data (the ~57 games from the programming
    // calendar), so it is seeded in every environment. It is idempotent — it no-ops once
    // any game exists.
    await DataSeeder.SeedGamesLibraryAsync(db);

    // Master onboarding checklist template — reference data used to issue each new
    // staff member's checklist. Idempotent — no-ops once any template item exists.
    await DataSeeder.SeedChecklistTemplateAsync(db);

    // Demo data is DEVELOPMENT-ONLY. These seeders create demo participants/staff/attendance
    // (#4) and default logins with the publicly-known password `ChangeMe!123` (#3). Running
    // them in production once filled the live CRM with fake records ("Kezia Morales") and
    // created takeover-able accounts.
    if (app.Environment.IsDevelopment())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await DataSeeder.SeedAsync(db);
        await DataSeeder.SeedAdminUserAsync(db, hasher);
        await DataSeeder.SeedSampleAttendanceAsync(db);
        await DataSeeder.SeedRosterAsync(db);
        await DataSeeder.SeedTrackerAsync(db);
        await DataSeeder.SeedYearCalendarAsync(db);
    }

    // The Script Library is kept populated in every environment so the Scripts page stays a
    // working demo (#18, explicit client request). Runs AFTER programs exist (dev: seeded
    // above; prod: real programs) so each script links to the programs it names by slug.
    // Idempotent — no-ops once any script exists.
    await DataSeeder.SeedScriptsAsync(db);
}

app.UseExceptionHandler();

// Headers first, so error responses carry them too (#7).
app.UseSecurityHeaders(app.Environment);
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseCors("FrontendPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
