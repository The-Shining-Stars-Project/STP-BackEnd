using CRM.Application.Interfaces;
using CRM.Infrastructure.Auth;
using CRM.Infrastructure.Storage;
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

        // --- File storage (Azure Blob) ---
        // Optional on purpose, unlike Jwt:Key: the API must boot and serve everything else in an
        // environment where the storage account does not exist yet. Program.cs logs a warning at
        // startup when it is missing, and file endpoints answer 503 naming the setting to add.
        // The choice is made here, once, rather than by an "if configured" branch on every call.
        var blobSection = configuration.GetSection(BlobStorageSettings.SectionName);
        services.Configure<BlobStorageSettings>(blobSection);
        var blobSettings = blobSection.Get<BlobStorageSettings>() ?? new BlobStorageSettings();
        if (blobSettings.IsConfigured)
            services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        else
            services.AddSingleton<IFileStorage, UnconfiguredFileStorage>();

        return services;
    }
}
