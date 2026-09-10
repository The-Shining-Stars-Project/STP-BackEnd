using CRM.API.Auditing;
using CRM.Application.DTOs.Participants;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ParticipantsController : ControllerBase
{
    // One megabyte above the service's file ceiling so multipart framing never trips first.
    private const long MaxUploadRequestBytes = ParticipantDocumentService.MaxFileBytes + 1024 * 1024;

    private const long MaxImportRequestBytes = ParticipantImportService.MaxCsvBytes + 1024 * 1024;

    private readonly IParticipantService _service;
    private readonly IArtsProfileService _artsProfile;
    private readonly IParticipantDocumentService _documents;
    private readonly IParticipantImportService _import;

    public ParticipantsController(
        IParticipantService service, IArtsProfileService artsProfile,
        IParticipantDocumentService documents, IParticipantImportService import)
    {
        _service = service;
        _artsProfile = artsProfile;
        _documents = documents;
        _import = import;
    }

    // ── Bulk import ───────────────────────────────────────────────────────────────

    /// <summary>The empty spreadsheet to fill in: one header row, the columns the import understands.</summary>
    [HttpGet("import/template")]
    [Authorize(Policy = "ManagementWrite")]
    public IActionResult ImportTemplate()
    {
        var csv = "\uFEFF" + Application.Files.Csv.Line(_import.TemplateHeaders) + "\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "stars-import-template.csv");
    }

    /// <summary>
    /// Validates a Stars spreadsheet and, with <c>?commit=true</c>, creates every row — or none,
    /// if any row has a problem. multipart/form-data, one part named "file". The response is the
    /// same report either way. Audited as one batch; each created star also gets its own
    /// participant.import row.
    /// </summary>
    [HttpPost("import")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.import.batch", "Participant")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxImportRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportRequestBytes)]
    public async Task<ActionResult<ParticipantImportReportDto>> Import(IFormFile file, [FromQuery] bool commit, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        return Ok(await _import.ImportAsync(User.GetUserId(), content, file.FileName, commit, ct));
    }

    /// <summary>
    /// Lists participants in the caller's programs (#1). Optional paging (#25): pass
    /// <c>?page=1&amp;pageSize=50</c> (pageSize capped at 200) to get one page plus an
    /// <c>X-Total-Count</c> header; omit both to get the full in-scope list.
    /// </summary>
    // Audited as well as the detail view, and this is the more important of the two. This is
    // the endpoint every client-side CSV export is built from — the Students page roster and
    // the Reports page roster export both call it — so auditing only the single-record view
    // would leave "somebody downloaded the roster" with no server-side evidence at all.
    // Its own action string keeps it filterable out of the viewer: it is the highest-volume
    // audit event, firing on most admin page loads (React Query caches it, so it is roughly
    // per-session rather than per-render).
    [HttpGet]
    [Audited("participant.list", "Participant")]
    public async Task<ActionResult<IReadOnlyList<ParticipantSummaryDto>>> GetAll(
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        var all = await _service.GetAllAsync(User.GetUserId(), ct);
        if (page is null && pageSize is null) return Ok(all);

        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var pageNo = Math.Max(page ?? 1, 1);

        Response.Headers["X-Total-Count"] = all.Count.ToString();
        return Ok(all.Skip((pageNo - 1) * size).Take(size).ToList());
    }

    [HttpGet("{id:guid}")]
    [Audited("participant.view", "Participant")]
    public async Task<ActionResult<ParticipantDetailDto>> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(User.GetUserId(), id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.create", "Participant")]
    public async Task<ActionResult<ParticipantDetailDto>> Create([FromBody] CreateParticipantDto dto)
    {
        var result = await _service.CreateAsync(User.GetUserId(), dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.update", "Participant")]
    public async Task<ActionResult<ParticipantDetailDto>> Update(Guid id, [FromBody] UpdateParticipantDto dto)
    {
        var result = await _service.UpdateAsync(User.GetUserId(), id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    // Permanent deletion of a child's PII — restricted to Admins.
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [Audited("participant.delete", "Participant")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _service.DeleteAsync(User.GetUserId(), id);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>The participant's Student Frame (IPP summary, current level, TSSP arts goal).</summary>
    [HttpGet("{id:guid}/arts-profile")]
    [Audited("participant.artsprofile.view", "ParticipantArtsProfile")]
    public async Task<ActionResult<ParticipantArtsProfileDto>> GetArtsProfile(Guid id)
    {
        var profile = await _artsProfile.GetAsync(User.GetUserId(), id);
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>Sets the Student Frame. Admin only — management authors these personalised fields.</summary>
    [HttpPut("{id:guid}/arts-profile")]
    [Authorize(Roles = "Admin")]
    [Audited("participant.artsprofile.update", "ParticipantArtsProfile")]
    public async Task<ActionResult<ParticipantArtsProfileDto>> UpsertArtsProfile(Guid id, [FromBody] UpsertArtsProfileDto dto)
    {
        var profile = await _artsProfile.UpsertAsync(User.GetUserId(), id, dto);
        return profile is null ? NotFound() : Ok(profile);
    }
    // ── Documents ─────────────────────────────────────────────────────────────────
    // A star's paperwork. Scoping is the participant's (#1): the service resolves the
    // caller's programs and 403s out-of-scope reads and writes alike. Files stream through
    // the API rather than as storage URLs so the container stays private. The entity id on
    // every audit row is the PARTICIPANT — "who touched this child's records" is the question.

    [HttpGet("{id:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<DocumentRecordDto>>> ListDocuments(Guid id, CancellationToken ct)
    {
        var result = await _documents.ListAsync(User.GetUserId(), id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{id:guid}/documents")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.document.create", "Participant")]
    public async Task<ActionResult<DocumentRecordDto>> CreateDocument(Guid id, [FromBody] CreateDocumentRecordDto dto, CancellationToken ct)
    {
        var result = await _documents.CreateAsync(User.GetUserId(), id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("{id:guid}/documents/{docId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.document.update", "Participant")]
    public async Task<ActionResult<DocumentRecordDto>> UpdateDocument(Guid id, Guid docId, [FromBody] UpdateDocumentRecordDto dto, CancellationToken ct)
    {
        var result = await _documents.UpdateAsync(User.GetUserId(), id, docId, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}/documents/{docId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.document.delete", "Participant")]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid docId, CancellationToken ct) =>
        await _documents.DeleteAsync(User.GetUserId(), id, docId, ct) ? NoContent() : NotFound();

    /// <summary>Attaches (or replaces) the file: multipart/form-data, one part named "file". PDF, PNG or JPG.</summary>
    [HttpPost("{id:guid}/documents/{docId:guid}/file")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.document.upload", "Participant")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<ActionResult<DocumentRecordDto>> UploadDocumentFile(Guid id, Guid docId, IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var result = await _documents.AttachFileAsync(User.GetUserId(), id, docId, content, file.FileName, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // Reads are audited here, unlike script PDFs: a script is shared teaching material, a
    // star's intake packet is a child's medical and legal paperwork.
    [HttpGet("{id:guid}/documents/{docId:guid}/file")]
    [Audited("participant.document.download", "Participant")]
    public async Task<IActionResult> DownloadDocumentFile(Guid id, Guid docId, CancellationToken ct)
    {
        var file = await _documents.OpenFileAsync(User.GetUserId(), id, docId, ct);
        if (file is null) return NotFound();
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}/documents/{docId:guid}/file")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("participant.document.file.delete", "Participant")]
    public async Task<ActionResult<DocumentRecordDto>> DeleteDocumentFile(Guid id, Guid docId, CancellationToken ct)
    {
        var result = await _documents.RemoveFileAsync(User.GetUserId(), id, docId, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
