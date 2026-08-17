using System.Security.Claims;

namespace CRM.API;

/// <summary>
/// Shared claims helpers — replaces the CurrentUserId() helper that was copy-pasted
/// into five controllers.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id from the token ("sub", or the mapped NameIdentifier).
    /// Returns <see cref="Guid.Empty"/> when absent/invalid — callers treat that as
    /// "no access" (it matches no user row).
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var idClaim = user.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? user.FindFirstValue("sub");
        return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// The authenticated user's email from the token. TokenService puts "email" in the JWT
    /// and the API validates with MapInboundClaims = false, so the short name survives
    /// verbatim — which means audit logging gets the actor's identity with no database hit
    /// per request. Null when unauthenticated.
    /// </summary>
    public static string? GetUserEmail(this ClaimsPrincipal user) =>
        user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email);

    /// <summary>The authenticated user's role claim ("Staff"/"Admin"). Null when unauthenticated.</summary>
    public static string? GetUserRole(this ClaimsPrincipal user) =>
        user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role);
}
