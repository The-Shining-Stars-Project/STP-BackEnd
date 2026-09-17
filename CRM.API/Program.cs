using System.Net;
using System.Text;
using System.Threading.RateLimiting;
using CRM.API;
using CRM.API.Auditing;
using CRM.API.Filters;
using CRM.Application;
using CRM.Application.Interfaces;
using CRM.Infrastructure;
using CRM.Infrastructure.Auth;
using CRM.Persistence;
using CRM.Persistence.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddControllers(options =>
    {
        // Captures the id of a record a [Audited] create just made, for AuditMiddleware to
        // record. It writes nothing itself — see AuditResultIdFilter.
        options.Filters.Add<AuditResultIdFilter>();

        // Mandatory MFA. Global on purpose and NOT an authorization policy — a policy would
        // silently miss every [Authorize(Roles=...)] and [Authorize(Policy=...)] endpoint,
        // which is all the admin and management-write surface. See MfaEnforcementFilter.
        options.Filters.Add<MfaEnforcementFilter>();
    })
    .AddJsonOptions(opts =>
        opts.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ---------------------------------------------------------
// Audit logging — the request context the audit writer reads its actor from.
// IAuditService itself is registered in AddPersistenceServices, next to the DbContext it needs.
// ---------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditContextAccessor, HttpAuditContextAccessor>();

// ---------------------------------------------------------
// Forwarded headers — recovering the real client IP behind the frontend proxy.
//
// The frontend rewrites every API call through its own origin so auth cookies stay
// first-party. The consequence is that Connection.RemoteIpAddress is the proxy for all
// traffic: audit rows record one address for the whole organisation, and the login rate
// limiter partitions on a single key (a global 10/min cap rather than 10/min per client).
//
// TRUST BOUNDARY, stated honestly: this API is publicly reachable, so anyone can call it
// directly and put whatever they like in X-Forwarded-For. Trusting that header from any
// caller would REMOVE the login rate limit rather than fix it — an attacker would forge a
// fresh address per request and mint an unlimited number of 10-attempt windows. So trust is
// opt-in per deployment: KnownProxies is empty by default, leaving the framework's
// loopback-only default. Local development works with no configuration because the Next.js
// dev server genuinely calls from 127.0.0.1; production behaviour is byte-identical to
// before this was added until an operator names the proxy's address.
//
// The complete fix is not a config value: restrict network access to this API so that only
// the frontend can reach it (App Service access restrictions or a private endpoint). Once
// no attacker can send a request here directly, trusting the proxy's XFF is safe. Until
// then, HttpAuditContextAccessor records the raw header into audit metadata as an explicitly
// client-asserted, untrusted field, so forensics get the real client address without any of
// it feeding the rate limiter.
// ---------------------------------------------------------
// Parsed here rather than inside the Configure callback, which does not run until the options
// are first resolved. A typo in the proxy address should stop the app at startup like the
// Jwt:Key guard does, not surface as a confusing failure on the first request — and silently
// skipping an unparseable entry would leave an operator believing they had pinned the proxy
// when they had not.
var knownProxies = (builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    .Select(proxy => IPAddress.TryParse(proxy, out var address)
        ? address
        : throw new InvalidOperationException(
            $"ForwardedHeaders:KnownProxies contains '{proxy}', which is not a valid IP address."))
    .ToList();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // XForwardedFor ONLY. XForwardedProto and XForwardedHost rewrite Request.Scheme and
    // Request.Host, which UseHttpsRedirection and the cookie Secure flag key off — nothing
    // here needs them, and getting them wrong turns local http development into a redirect
    // loop.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;

    // One hop: the frontend's rewrite proxy. Anything further left in the chain was supplied
    // by the client.
    options.ForwardLimit = 1;

    // Added to, not replacing, the framework's loopback default — so local development keeps
    // working whether or not anything is configured here.
    foreach (var address in knownProxies)
        options.KnownProxies.Add(address);
});

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

// ---------------------------------------------------------
// MFA secret-encryption key — the same guard as Jwt:Key, for the same reason.
//
// This key is what stands between a SQL-injection read of the Users table and the ability to
// mint working TOTP codes for every enrolled account. Azure SQL TDE does not help there: it
// encrypts the files on disk and hands plaintext columns to anyone who can run a query.
//
// The length semantics differ from Jwt:Key and the difference is not cosmetic. Jwt:Key is an
// HMAC secret, so any sufficiently long string works and the check counts UTF-8 bytes. This
// is an AES-256 key, so it must decode from base64 to EXACTLY 32 bytes — nothing else is a
// valid key, and a 31- or 33-byte value is a startup failure rather than a weaker key.
//
// In Development a malformed (but present) key is left to fail at first use, where
// MfaSecretProtector's constructor says precisely what is wrong. That mirrors Jwt:Key, whose
// short-key check is also production-only.
// ---------------------------------------------------------
const string DevPlaceholderMfaKey = "ZGV2LW9ubHktbWZhLWtleS1jaGFuZ2UtbWUtMzJieXQ=";
var mfaKey = builder.Configuration.GetSection("Mfa")["EncryptionKey"];
if (string.IsNullOrWhiteSpace(mfaKey))
    throw new InvalidOperationException(
        "Missing configuration: Mfa:EncryptionKey — set the Mfa__EncryptionKey environment variable in Azure App Service. "
        + "Generate one with `openssl rand -base64 32`.");

if (!builder.Environment.IsDevelopment()
    && (mfaKey == DevPlaceholderMfaKey
        || !Convert.TryFromBase64String(mfaKey, new byte[64], out var mfaKeyBytes)
        || mfaKeyBytes != 32))
    throw new InvalidOperationException(
        "Refusing to start: Mfa:EncryptionKey is the development placeholder, is not valid base64, or does not "
        + "decode to exactly 32 bytes. Set Mfa__EncryptionKey to `openssl rand -base64 32` output in Azure App Service. "
        + "Note there is no key rotation: changing it makes every enrolled TOTP secret undecryptable.");

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
                var row = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => new { u.IsActive, u.MfaEnabled, u.Role })
                    .FirstOrDefaultAsync();
                if (row is null || !row.IsActive)
                {
                    ctx.Fail("Account is deactivated or no longer exists.");
                    return;
                }

                // One extra column on a query that was already happening, and it is what makes
                // the MFA gate honest. The JWT's "mfa" claim is fixed for the token's whole
                // life (60 minutes in production), so after an admin MFA reset the outstanding
                // access cookie still says the user is enrolled — and would sail through the
                // gate for the rest of the hour. That is precisely the lost-or-stolen-phone
                // case the reset exists for. MfaEnforcementFilter reads this value, not the claim.
                ctx.HttpContext.Items[MfaEnforcementFilter.EnrollmentKey] = row.MfaEnabled;

                // Same reasoning, one more column on the same query. The "role" claim is fixed
                // when the token is minted and lives 8 hours, and nothing revokes a session on a
                // role change — so after an admin demotes another admin at 09:00 (correctly
                // producing a user.role.change row), every audit row that account generated
                // until 16:00 said "Admin". A reviewer filtering the log for what admins did got
                // false positives indistinguishable from real ones, on a timeline that appeared
                // to contradict the role-change row. AuditEvent.UserRole documents itself as the
                // actor's role AT THE TIME OF THE ACTION; this is what makes that true.
                ctx.HttpContext.Items[HttpAuditContextAccessor.RoleKey] = row.Role.ToString();
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
        || ctx.User.HasClaim("staffRole", "TechnologySystemsCoordinator")
        || ctx.User.HasClaim("staffRole", "Admin")));

    // There is deliberately NO MFA requirement here, and adding one would create a gate with a
    // hole in it. AuthorizationOptions.DefaultPolicy is consulted only for a bare [Authorize];
    // the endpoints that matter most — everything with [Authorize(Roles = "Admin")] or
    // [Authorize(Policy = "ManagementWrite")] — build their own policies and would skip it
    // entirely, while the read endpoints would appear to work. Mandatory MFA is enforced by
    // MfaEnforcementFilter, which sees every request regardless of which [Authorize] flavour
    // the endpoint used.
});

// ---------------------------------------------------------
// Rate limiting (#7) — throttle the anonymous auth surface to blunt credential stuffing.
//
// Partitioned by client IP, which behind the frontend's rewrite proxy is ONE key for all
// legitimate traffic until an operator pins the proxy in ForwardedHeaders:KnownProxies (see
// the trust-boundary note above). Read that as: these are organisation-wide budgets today.
// They are a blunt instrument for exactly that reason, and none of them is the brute-force
// control — the per-ACCOUNT second-factor throttle in AuthService (MaxMfaFailures) is what
// bounds TOTP guessing, because an attacker calling this public API directly from rotating
// source addresses gets a fresh budget per address and never touches these buckets at all.
//
// The budgets are split rather than shared. When MFA arrived, five new endpoints joined the
// "login" policy and one enrolled sign-in went from costing 1 permit to as many as 6 (password
// plus five code attempts) against a bucket the whole organisation shares. On rollout day,
// when everybody enrolls at once and mistypes most, the fourth person to enroll in a minute
// locked out the rest of the team — while an attacker in their own partition was unaffected by
// the budget staff were exhausting. Separate buckets mean one flow cannot starve another.
// ---------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // The password step (and refresh). Unchanged at 10/min: this is the anonymous
    // credential-guessing surface and it is the one budget that should not grow.
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // The code step. Its own bucket because a sign-in spends up to MfaChallenge.MaxAttempts
    // requests here for ONE password request there, so sharing meant a handful of typos ate
    // the whole organisation's ability to sign in. Anonymous, so still partitioned by address.
    options.AddPolicy("login-mfa", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Authenticated MFA management: setup, enable, regenerate recovery codes, disable.
    // Partitioned on the authenticated user id, so one person enrolling can neither starve the
    // anonymous login path nor their colleagues — which is the whole failure mode of rollout
    // day. UseRateLimiter is ordered after UseAuthentication so this principal exists; falls
    // back to the address if it somehow does not, rather than lumping callers under one key.
    options.AddPolicy("mfa-manage", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.GetUserId() is { } uid && uid != Guid.Empty
                ? $"user:{uid}"
                : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // POST /api/audit/export is the one endpoint where a caller writes strings of their
    // choosing into the audit log. Capping it keeps it from becoming a log-flooding
    // primitive — a cheap way to bury a real event under thousands of plausible rows.
    // 30/min is far above what a human clicking Export can produce.
    options.AddPolicy("audit-write", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
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
// File storage (Azure Blob) — optional, unlike Jwt:Key, so the API can boot before the
// storage account exists. But a deployment that is missing it should say so once, here at
// startup, rather than only when the first PDF upload comes back 503. Resolving the service
// now also means a malformed BlobStorage:AccountUrl fails the boot loudly instead of the
// first upload.
// ---------------------------------------------------------
if (!app.Services.GetRequiredService<IFileStorage>().IsConfigured)
    app.Logger.LogWarning(
        "BlobStorage is not configured (no BlobStorage:AccountUrl or BlobStorage:ConnectionString). "
        + "Script PDF uploads will answer 503 until it is set — see appsettings.json.template.");

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
// (Azure App Service setting: Database__MigrateOnStartup).
//
// DEFAULTS BY ENVIRONMENT, deliberately asymmetric. Development defaults to true, because a
// developer pulling a branch wants the schema to follow it. Everything else defaults to
// FALSE: outside development a restart must never be able to alter the schema, and the
// failure mode of getting that wrong is a boot loop against a half-migrated database with
// the client's records in it. The else-branch below turns a missing migration into a loud
// startup error naming the exact command to run, which is the behaviour you want at deploy
// time — fail the release, not the data.
var migrateOnStartup = builder.Configuration.GetValue(
    "Database:MigrateOnStartup", builder.Environment.IsDevelopment());

// `dotnet ef` resolves the DbContext by executing this file up to app.Run(), so without this
// guard EVERY `migrations add`, `migrations list` or `database update --dry-run` would also
// migrate whatever database the connection string happens to point at. That is not
// hypothetical: it is how AddEventAttendance reached the shared database before anyone had
// chosen to apply it. EF Core's design-time host runs under an entry assembly named "ef".
var isDesignTime = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "ef";

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (isDesignTime)
    {
        // Scaffolding a migration must not apply one. Nothing else in this block should run
        // at design time either — the seeders below would write to the live database.
        return;
    }

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

// First middleware in the pipeline, before the exception handler: every later stage —
// security headers, rate limiter, authentication, controllers, audit — must see the same
// client address, and the ones that run earliest are the ones that most need it.
app.UseForwardedHeaders();

app.UseExceptionHandler();

// Headers first, so error responses carry them too (#7).
app.UseSecurityHeaders(app.Environment);
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseCors("FrontendPolicy");
app.UseAuthentication();

// After authentication, so the "mfa-manage" policy can partition on the authenticated user id
// instead of on an address that is the same proxy for the whole organisation. The cost is that
// a request carrying a valid token is authenticated (one indexed lookup in OnTokenValidated)
// before it can be throttled; the anonymous flood this protects against carries no token and
// still costs nothing.
app.UseRateLimiter();

// Audit recording sits between the rate limiter and authorization, and each side of that
// matters.
//
// After the rate limiter, so a 429 does not write a row: a throttled endpoint whose rejections
// were audited would still be a way to fill the audit table, which is the thing the throttle is
// protecting.
//
// After UseAuthentication, so HttpContext.User is populated and a refused request still
// records WHO was refused. Before UseAuthorization, because AuthorizationMiddleware answers a
// denied request itself and never calls next() — anything registered after it is skipped on
// exactly the 401s and 403s the audit log exists to capture. Wrapping it means this sees the
// final Response.StatusCode whether the endpoint ran, authorization refused it, the MFA gate
// refused it, or model validation rejected the body.
app.UseMiddleware<AuditMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();
