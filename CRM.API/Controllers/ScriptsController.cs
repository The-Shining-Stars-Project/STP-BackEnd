using CRM.Application.DTOs.Scripts;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ScriptsController : ControllerBase
{
    // The service enforces the 25 MB rule on the file; these limits sit one megabyte above it
    // so multipart framing never trips them first, and so an oversize body is refused by the
    // framework before it is buffered rather than after.
    private const long MaxUploadRequestBytes = ScriptService.MaxPdfBytes + 1024 * 1024;

    private readonly IScriptService _service;

    public ScriptsController(IScriptService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScriptDto>>> GetAll() =>
        Ok(await _service.GetAllAsync());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ScriptDto>> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("script.create", "Script")]
    public async Task<ActionResult<ScriptDto>> Create([FromBody] CreateScriptDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("script.update", "Script")]
    public async Task<ActionResult<ScriptDto>> Update(Guid id, [FromBody] UpdateScriptDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    // ── PDF attachment ────────────────────────────────────────────────────────────
    // The PDF is a sub-resource of the script: one file per script, replaced by uploading
    // again. Bytes are streamed through the API rather than handed out as storage URLs so the
    // container stays private and the existing cookie auth is the only access control.

    /// <summary>
    /// Attaches (or replaces) the script's PDF. multipart/form-data with one part named "file".
    /// 400 for anything that is not a PDF or is over the size limit; 503 until storage is configured.
    /// </summary>
    [HttpPost("{id:guid}/pdf")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("script.pdf.upload", "Script")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<ActionResult<ScriptDto>> UploadPdf(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var result = await _service.AttachPdfAsync(id, content, file.FileName, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>Downloads the script's PDF. 404 when the script has none.</summary>
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> DownloadPdf(Guid id, CancellationToken ct)
    {
        var pdf = await _service.OpenPdfAsync(id, ct);
        if (pdf is null) return NotFound();

        // FileStreamResult disposes the stream after writing it, and builds a Content-
        // Disposition that encodes the name safely whatever characters it contains.
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }

    /// <summary>Removes the script's PDF. Idempotent — a script with no PDF is returned unchanged.</summary>
    [HttpDelete("{id:guid}/pdf")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("script.pdf.delete", "Script")]
    public async Task<ActionResult<ScriptDto>> DeletePdf(Guid id, CancellationToken ct)
    {
        var result = await _service.RemovePdfAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
