using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.API;

/// <summary>
/// Central exception → ProblemDetails mapping (#24). Controllers still catch where they
/// need a specific shape; anything that escapes lands here and gets a consistent
/// ProblemDetails body instead of a raw 500 — and business-rule exceptions keep their
/// intended status codes even when a controller forgot a try/catch.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict"),
            // Must stay below DbUpdateConcurrencyException — it derives from this.
            DbUpdateException => (StatusCodes.Status409Conflict, "Conflict"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Conflict"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        // EF's own messages here are developer-speak ("See the inner exception for details"),
        // so substitute user-facing text.
        var detail = exception switch
        {
            // RowVersion mismatch (#26): someone saved this row after we read it.
            DbUpdateConcurrencyException =>
                "This item was changed by someone else while you were editing. Refresh and try again.",
            // Usually a unique-index violation — e.g. two submissions of the same weekly
            // score racing each other (#6). Saving again after a refresh resolves it.
            DbUpdateException =>
                "That change conflicts with data already saved. Refresh and try again.",
            _ => exception.Message,
        };

        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}.",
                httpContext.Request.Method, httpContext.Request.Path);
        else if (exception is DbUpdateException)
            // Log the exception object, not just its message: the useful part is the inner
            // SqlException, and a benign unique-violation is indistinguishable from a real
            // FK bug without it.
            _logger.LogWarning(exception, "Database rejected a write for {Method} {Path}.",
                httpContext.Request.Method, httpContext.Request.Path);
        else
            _logger.LogWarning("Business-rule rejection ({Status}) for {Method} {Path}: {Message}",
                status, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            // Never leak internals on 500s; domain exception messages are user-facing.
            Detail = status == StatusCodes.Status500InternalServerError ? null : detail,
        }, cancellationToken);

        return true;
    }
}
