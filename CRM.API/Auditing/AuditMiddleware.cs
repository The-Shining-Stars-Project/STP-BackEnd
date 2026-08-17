using CRM.Application.DTOs.Audit;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;

namespace CRM.API.Auditing;

/// <summary>
/// Turns <see cref="AuditedAttribute"/> into audit rows.
///
/// WHY MIDDLEWARE AND NOT AN MVC FILTER. This started life as an IAsyncActionFilter, which
/// records only requests that reach the action — and the requests most worth recording are
/// precisely the ones that do not. Three stages short-circuit ahead of the action stage:
///
///   1. AuthorizationMiddleware, which enforces [Authorize(Roles = ...)] and
///      [Authorize(Policy = ...)] from endpoint metadata before ANY MVC filter runs. A Staff
///      account walking DELETE /api/participants/{id} over guessed ids, or probing the
///      Admin-only GET /api/audit, produced no rows at all.
///   2. MfaEnforcementFilter, an authorization filter, which 403s an unenrolled session. After
///      an admin MFA reset a stolen access cookie still authenticates, so the whole window in
///      which the thief probes the API was invisible.
///   3. [ApiController]'s model-validation filter, so 400s on audited endpoints were unlogged.
///
/// No placement inside the MVC filter pipeline catches the first of those — resource filters
/// run AFTER authorization filters, not before, and nothing in MVC runs before
/// AuthorizationMiddleware. Middleware does, provided it is registered so that it WRAPS the
/// authorization stage: a denied request never calls next(), so anything registered after
/// UseAuthorization is skipped along with the endpoint. See Program.cs for the placement.
///
/// It is still a thin adapter: every decision with a bug in it (which route value is the
/// entity id, which status codes count as success, sanitizing and truncating strings) lives in
/// <see cref="AuditEventFactory"/> in CRM.Application, where CRM.Tests can reach it.
/// </summary>
public sealed class AuditMiddleware
{
    /// <summary>
    /// HttpContext.Items key holding the entity id a created record was assigned, stashed by
    /// <see cref="AuditResultIdFilter"/>. A POST has no route id — the new record's id exists
    /// only in the response body, which middleware cannot read without buffering it.
    /// </summary>
    public const string EntityIdKey = "ss.audit.entityId";

    private readonly RequestDelegate _next;
    private readonly ILogger<AuditMiddleware> _logger;

    public AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IAuditService audit)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // The exception is still in flight; GlobalExceptionHandler runs further out, so no
            // final status exists yet. Record the failure with the exception TYPE only, then
            // let it propagate untouched.
            if (AuditedEndpoint(context) is { } failing)
            {
                await SafeRecordAsync(
                    audit, failing, context,
                    succeeded: false,
                    metadata: AuditEventFactory.RequestMetadata(
                        null, context.Request.RouteValues!, failing.MetadataRouteKeys, ex.GetType().Name));
            }
            throw;
        }

        // The endpoint is read AFTER the pipeline rather than before it, and deliberately: it
        // is set by the routing middleware, and reading it on the way out means this class
        // makes no assumption about whether routing is ordered before it. Requests to an
        // endpoint without [Audited] — nearly all of them — cost one metadata lookup here.
        if (AuditedEndpoint(context) is not { } attribute) return;

        var status = context.Response.StatusCode;
        await SafeRecordAsync(
            audit, attribute, context,
            AuditEventFactory.SucceededFrom(status),
            metadata: AuditEventFactory.RequestMetadata(
                status, context.Request.RouteValues!, attribute.MetadataRouteKeys));
    }

    private static AuditedAttribute? AuditedEndpoint(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<AuditedAttribute>();

    private async Task SafeRecordAsync(
        IAuditService audit,
        AuditedAttribute attribute,
        HttpContext context,
        bool succeeded,
        string? metadata)
    {
        try
        {
            // A created record's id, if the action stage got far enough to produce one;
            // otherwise resolved from the route, which is populated by routing and therefore
            // available even on a request that was refused before the action ran.
            var entityId = context.Items.TryGetValue(EntityIdKey, out var stashed) && stashed is Guid id
                ? id
                : AuditEventFactory.ResolveEntityId(context.Request.RouteValues!, attribute.IdRouteKey, null);

            // Actor, address and user-agent are left null: AuditService fills them from the
            // ambient request context, which is this same HttpContext. Authentication has
            // already run by the time this middleware is reached, so a denied request still
            // records who was denied.
            await audit.RecordAsync(new AuditEntry
            {
                Action = attribute.Action,
                EntityType = attribute.EntityType,
                EntityId = entityId,
                Summary = attribute.Summary,
                Succeeded = succeeded,
                Metadata = metadata,
            });
        }
        catch (Exception ex)
        {
            // Belt and braces on top of AuditService's own guard. Middleware that throws breaks
            // every request that passes through it, so nothing here is allowed to escape — and
            // on the exception path above, swallowing the original would be worse still.
            _logger.LogError(ex, "Audit middleware failed for {Action}; event lost.", attribute.Action);
        }
    }
}
