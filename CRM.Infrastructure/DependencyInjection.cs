using CRM.Application.Interfaces;
using CRM.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CRM.Infrastructure;

/// <summary>
/// Extension method to register all Infrastructure layer services.
/// Called from Program.cs: builder.Services.AddInfrastructureServices(config)
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // --- Authentication primitives ---
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();

        // --- Multi-factor authentication ---
        // Both are stateless and hold only configuration, so singleton is right — and it
        // means MfaSecretProtector's key validation runs once rather than per request.
        services.Configure<MfaSettings>(configuration.GetSection("Mfa"));
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<IMfaSecretProtector, MfaSecretProtector>();

        // --- Time ---
        // "Today" is a local-calendar question, not a UTC one (#7).
        services.Configure<Time.OrgTimeSettings>(configuration.GetSection(Time.OrgTimeSettings.SectionName));
        services.AddSingleton<IOrgClock, Time.OrgClock>();

        // TODO: Register external service clients here (Blob storage, email, etc.)

        return services;
    }
}
