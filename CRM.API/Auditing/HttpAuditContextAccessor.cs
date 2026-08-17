using CRM.Application.Interfaces;
using CRM.Application.Services;

namespace CRM.API.Auditing;

/// <summary>
/// Reads the actor and origin of the current request out of HttpContext. This is the ASP.NET
/// half of <see cref="IAuditContextAccessor"/>; it lives here because CRM.Application must
/// stay free of ASP.NET types.
/// </summary>
public class HttpAuditContextAccessor : IAuditContextAccessor
{
    /// <summary>
    /// Longest client-asserted forwarded chain kept. An IPv6 address is at most 45 characters,
    /// so this holds five hops with room to spare; anything longer is not a proxy chain.
    /// </summary>
    public const int ClientAssertedIpMaxLength = 256;

    /// <summary>
    /// HttpContext.Items key holding the DATABASE-FRESH role, stamped by OnTokenValidated —
    /// the same arrangement as MfaEnforcementFilter.EnrollmentKey and for the same reason. The
    /// JWT's "role" claim is minted once and lives for the token's 8 hours, and a role change
    /// does not revoke sessions, so the claim can be wrong for most of a working day. Falls
    /// back to the claim when the key is absent, which can only mean authentication came from a
    /// path that skipped OnTokenValidated.
    /// </summary>
    public const string RoleKey = "ss.audit.role";

    private readonly IHttpContextAccessor _http;

    public HttpAuditContextAccessor(IHttpContextAccessor http) => _http = http;

    public AuditContext? Current
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is null) return null;

            var userId = ctx.User.GetUserId();

            return new AuditContext(
                UserId: userId == Guid.Empty ? null : userId,
                UserEmail: ctx.User.GetUserEmail(),
                UserRole: ctx.Items.TryGetValue(RoleKey, out var role) && role is string fresh
                    ? fresh
                    : ctx.User.GetUserRole(),
                // Rewritten in place by the forwarded-headers middleware, but only for hops
                // the operator has explicitly trusted. Untrusted deployments see the proxy's
                // address here, which is honest — it is what the server actually observed.
                IpAddress: ctx.Connection.RemoteIpAddress?.ToString(),
                UserAgent: ctx.Request.Headers.UserAgent.ToString(),
                // Captured unconditionally, and named to make its status unmissable. Until an
                // operator pins the proxy in ForwardedHeaders:KnownProxies, this is the only
                // place the real client address appears — so forensics get it immediately —
                // but it is a claim by the caller, it goes into audit metadata rather than
                // IpAddress, and nothing security-critical (rate limiting, blocking) reads it.
                //
                // Capped HERE, at capture, and that placement is the point. Kestrel accepts a
                // header of roughly 32 KB, and this is the only audit field fed straight from a
                // request header — every other one (IpAddress, UserAgent, Summary) is already
                // bounded at ingestion. Left uncapped it was a log-flooding primitive reachable
                // without authenticating at all (a failed login writes a row), and the later
                // blunt truncation of the assembled metadata would land mid-string and leave
                // unparseable JSON behind. A legitimate proxy chain is a handful of addresses,
                // so 256 characters is generous.
                ClientAssertedIp: AuditEventFactory.Sanitize(
                    ctx.Request.Headers["X-Forwarded-For"].ToString(),
                    ClientAssertedIpMaxLength));
        }
    }
}
