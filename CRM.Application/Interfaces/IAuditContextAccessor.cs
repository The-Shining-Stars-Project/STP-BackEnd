namespace CRM.Application.Interfaces;

/// <summary>
/// Who is making the current request and from where.
/// </summary>
/// <param name="ClientAssertedIp">
/// The raw X-Forwarded-For header exactly as the client sent it. Named to make its status
/// unmissable: it is attacker-controllable and is recorded as evidence-of-a-claim, never
/// used as the client identity for rate limiting or blocking. It exists because the frontend
/// proxies every call through its own origin, so <c>IpAddress</c> is the proxy's address
/// until an operator pins the proxy in ForwardedHeaders:KnownProxies.
/// </param>
public record AuditContext(
    Guid? UserId,
    string? UserEmail,
    string? UserRole,
    string? IpAddress,
    string? UserAgent,
    string? ClientAssertedIp);

/// <summary>
/// Supplies the ambient request context to services that must stay free of ASP.NET types.
/// CRM.Application has no ASP.NET package reference and must not gain one, so the HTTP
/// implementation lives in CRM.API — the same split as IStatsQueries/StatsQueries.
/// Returns null outside a request (background work, tests).
/// </summary>
public interface IAuditContextAccessor
{
    AuditContext? Current { get; }
}
