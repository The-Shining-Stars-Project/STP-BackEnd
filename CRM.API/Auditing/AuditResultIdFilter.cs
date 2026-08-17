using CRM.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CRM.API.Auditing;

/// <summary>
/// Stashes the entity id of a record an audited action just created, for
/// <see cref="AuditMiddleware"/> to pick up.
///
/// This is the one thing the middleware cannot do for itself. A create has no id in its route
/// — the new record's id exists only in the response body — and reading a body from middleware
/// means buffering every response on the off-chance. An action filter already holds the
/// unserialized result object, so it costs one property read.
///
/// It records nothing. All recording lives in the middleware, because an action filter never
/// runs on a request that authorization refused, which is the case the audit log most needs.
/// </summary>
public sealed class AuditResultIdFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = context.ActionDescriptor.EndpointMetadata.OfType<AuditedAttribute>().FirstOrDefault();
        if (attribute is null)
        {
            await next();
            return;
        }

        var executed = await next();

        if (executed.Exception is not null && !executed.ExceptionHandled) return;

        var entityId = AuditEventFactory.ResolveEntityId(
            executed.RouteData.Values,
            attribute.IdRouteKey,
            (executed.Result as ObjectResult)?.Value);

        if (entityId is { } id) context.HttpContext.Items[AuditMiddleware.EntityIdKey] = id;
    }
}
