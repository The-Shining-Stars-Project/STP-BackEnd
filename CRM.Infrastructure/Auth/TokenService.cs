using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CRM.Application.Interfaces;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CRM.Infrastructure.Auth;

public class TokenService : ITokenService
{
    // Short, unambiguous claim names. The API validates with MapInboundClaims = false,
    // so these names survive the round-trip verbatim.
    public const string SubClaim = "sub";
    public const string NameClaim = "name";
    public const string RoleClaim = "role";
    /// <summary>The linked staff member's role (Teacher/Coordinator/Admin), for management-write policies.</summary>
    public const string StaffRoleClaim = "staffRole";

    /// <summary>
    /// Whether the account has an enrolled second factor, as "true"/"false".
    ///
    /// INFORMATIONAL ONLY. It is stamped when the token is minted and stays that way for the
    /// token's whole life (60 minutes in production), which means it is stale in the dangerous
    /// direction: after an admin MFA reset the outstanding access cookie still claims true.
    /// The server-side gate reads the current value from the database on every request
    /// (see Program.cs OnTokenValidated and MfaEnforcementFilter). This claim exists so the
    /// frontend's edge middleware can redirect an unenrolled user without a round-trip.
    /// </summary>
    public const string MfaClaim = "mfa";

    private readonly JwtSettings _settings;

    public TokenService(IOptions<JwtSettings> settings) => _settings = settings.Value;

    public (string Token, DateTime ExpiresAt) CreateToken(User user, StaffRole? staffRole = null)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(SubClaim, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(NameClaim, user.FullName),
            new(RoleClaim, user.Role.ToString()),
            new(MfaClaim, user.MfaEnabled ? "true" : "false"),
        };
        if (staffRole is { } sr)
            claims.Add(new Claim(StaffRoleClaim, sr.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
