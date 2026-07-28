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

        // --- Time ---
        // "Today" is a local-calendar question, not a UTC one (#7).
        services.Configure<Time.OrgTimeSettings>(configuration.GetSection(Time.OrgTimeSettings.SectionName));
        services.AddSingleton<IOrgClock, Time.OrgClock>();

        // TODO: Register external service clients here (Blob storage, email, etc.)

        return services;
    }
}
