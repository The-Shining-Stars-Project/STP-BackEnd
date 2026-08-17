using CRM.API.Auditing;
using CRM.Application.DTOs.Audit;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CRM.API.Controllers;

/// <summary>
/// Read access to the audit log, plus the client-reported export endpoint.
///
/// There is no PUT and no DELETE here, and none should be added. The table is append-only,
/// and the guarantee is structural rather than a rule somebody has to remember: AuditEvent
/// has no repository on IUnitOfWork, IAuditQueries reads AsNoTracking with a projection so
/// it cannot hand back a tracked entity, and no code path anywhere calls Update or Remove on
/// it. The belt-and-braces version is a database grant (DENY UPDATE, DELETE ON AuditEvents
/// to the application login), which is worth doing but cannot be done from here: this app's
/// login owns the schema and runs its own migrations, so it would need a separate migration
/// identity. That is an infrastructure change to hand the client, not a code change.
/// </summary>
// Authenticated at the class level rather than Admin-only, so the two actions can differ:
// reading the log is Admin-only, but reporting an export must be open to anyone who can
// perform one. The Students page (participant roster, with DOB, guardian contacts and
// allergies) and the Staff page are reachable by Staff accounts, so an Admin-only report
// endpoint would silently drop the single most sensitive export in the product.
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly IAuditQueries _queries;
    private readonly IAuditService _audit;

    public AuditController(IAuditQueries queries, IAuditService audit)
    {
        _queries = queries;
        _audit = audit;
    }

    /// <summary>
    /// Searches the audit log, newest first. Filters combine with AND; all are optional.
    /// Paging follows the same contract as the participants endpoint — <c>?page=&amp;pageSize=</c>
    /// with an <c>X-Total-Count</c> header carrying the pre-paging total — with one deliberate
    /// difference: omitting both gives page 1 at size 50 rather than the full list, because
    /// the alternative is dumping an ever-growing table on every call.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [Audited("audit.view", "AuditEvent")]
    public async Task<ActionResult<IReadOnlyList<AuditEventDto>>> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? userId,
        [FromQuery] string? userEmail,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] bool? succeeded,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var query = new AuditQuery
        {
            From = from,
            To = to,
            UserId = userId,
            UserEmail = userEmail,
            Action = action,
            EntityType = entityType,
            Succeeded = succeeded,
            Page = Math.Max(page ?? 1, 1),
            PageSize = Math.Clamp(pageSize ?? 50, 1, 200),
        };

        var (rows, total) = await _queries.SearchAsync(query, ct);

        // X-Total-Count is not in the CORS WithExposedHeaders list, and does not need to be:
        // the browser reaches this API through the frontend's same-origin rewrite, so CORS
        // never applies. Matches how ParticipantsController already behaves. A direct
        // cross-origin caller would not be able to read the header.
        Response.Headers["X-Total-Count"] = total.ToString();
        return Ok(rows);
    }

    /// <summary>
    /// Records that a user exported data. The Reports, Students and Staff pages build their
    /// CSVs in the browser from data they already hold, so the server never sees the file —
    /// this is the only way to capture what was in it.
    ///
    /// IT IS A CLIENT SELF-REPORT, NOT EVIDENCE. It fires after the client already has the
    /// data, so a broken, offline, or hostile client simply does not call it. Treat it as a
    /// useful detail record (which file, how many rows) layered on top of the authoritative
    /// server-side rows for GET /api/participants, /api/reports and /api/staff, which cannot
    /// be skipped. The Summary is prefixed to keep that distinction visible in the viewer.
    ///
    /// Not the [Audited] attribute, because the interesting content is in the request body
    /// and the filter deliberately never reads bodies.
    ///
    /// The action takes no CancellationToken, and that is deliberate — it is the one call site
    /// where it matters. HttpContext.RequestAborted fires when
    /// this is the one call site where that matters. HttpContext.RequestAborted fires when the
    /// client hangs up — which the browser does routinely here, because the page often
    /// navigates the instant the download starts — and AuditService swallows the resulting
    /// OperationCanceledException with only a log line. The endpoint whose entire job is
    /// capturing what a client cannot be forced to report would then be skippable by closing
    /// the socket, and it would look like a network blip. The entry is fully materialised
    /// before the write, so nothing here needs the request to still be alive. Every other audit
    /// call site passes CancellationToken.None for the same reason.
    /// </summary>
    [HttpPost("export")]
    [EnableRateLimiting("audit-write")]
    public async Task<IActionResult> RecordExport([FromBody] RecordExportDto dto)
    {
        if (!ExportAuditValidation.TryValidate(dto, out var validated, out var error))
            return BadRequest(new { message = error });

        // Metadata is built from the validated, typed values — never by echoing the raw
        // request body into the log.
        var metadata = System.Text.Json.JsonSerializer.Serialize(new
        {
            exportKind = validated.ExportKind,
            rowCount = validated.RowCount,
            fileName = validated.FileName,
            scope = validated.Scope,
            clientReported = true,
        });

        await _audit.RecordAsync(new AuditEntry
        {
            Action = "export.csv",
            EntityType = "Export",
            Summary = $"Client-reported export: {validated.ExportKind} ({validated.RowCount} rows)",
            Succeeded = true,
            Metadata = metadata,
        });

        return NoContent();
    }
}
