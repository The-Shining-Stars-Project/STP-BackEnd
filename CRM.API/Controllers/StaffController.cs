using CRM.API.Auditing;
using CRM.Application.DTOs.Staff;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class StaffController : ControllerBase
{
    private const long MaxUploadRequestBytes = StaffService.MaxFileBytes + 1024 * 1024;

    private readonly IStaffService _service;

    public StaffController(IStaffService service) => _service = service;

    // The source of the staff-onboarding CSV export.
    [HttpGet]
    [Audited("staff.list", "StaffMember")]
    public async Task<ActionResult<IReadOnlyList<StaffSummaryDto>>> GetAll(CancellationToken ct)
    {
        var staff = await _service.GetAllAsync(ct);
        // Onboarding completion is admin-only (client rule) — non-admins still get the
        // roster (names/roles/programs) but never anyone's checklist progress.
        if (!User.IsInRole("Admin"))
            foreach (var s in staff) s.OnboardingProgressPct = 0;
        return Ok(staff);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [Audited("staff.view", "StaffMember")]
    public async Task<ActionResult<StaffDetailDto>> GetById(Guid id)
    {
        var result = await _service.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("staff.create", "StaffMember")]
    public async Task<ActionResult<StaffDetailDto>> Create([FromBody] CreateStaffDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("staff.update", "StaffMember")]
    public async Task<ActionResult<StaffDetailDto>> Update(Guid id, [FromBody] UpdateStaffDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    // Admin-only like GetById — the response carries the full checklist.
    // EntityId resolves to the staff member's id rather than the checklist item's, which is
    // the right subject: the question this row answers is "who changed this person's record".
    [HttpPut("{id:guid}/onboarding/{itemId:guid}")]
    [Authorize(Roles = "Admin")]
    [Audited("staff.onboarding.update", "StaffMember")]
    public async Task<ActionResult<StaffDetailDto>> SetOnboardingItem(Guid id, Guid itemId, [FromBody] SetOnboardingItemDto dto)
    {
        var result = await _service.SetOnboardingItemAsync(id, itemId, dto);
        return result is null ? NotFound() : Ok(result);
    }

    // ── Onboarding paperwork ──────────────────────────────────────────────────────
    // Admin-only like the checklist itself: these are HR files (I-9, background check).
    // Downloads are audited — the same reasoning as a star's intake packet.

    /// <summary>Attaches (or replaces) the file behind a checklist item. multipart/form-data, part "file". PDF, PNG or JPG.</summary>
    [HttpPost("{id:guid}/onboarding/{itemId:guid}/file")]
    [Authorize(Roles = "Admin")]
    [Audited("staff.onboarding.file.upload", "StaffMember")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadRequestBytes)]
    public async Task<ActionResult<StaffDetailDto>> UploadOnboardingFile(Guid id, Guid itemId, IFormFile file, CancellationToken ct)
    {
        await using var content = file.OpenReadStream();
        var result = await _service.AttachOnboardingFileAsync(id, itemId, content, file.FileName, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{id:guid}/onboarding/{itemId:guid}/file")]
    [Authorize(Roles = "Admin")]
    [Audited("staff.onboarding.file.download", "StaffMember")]
    public async Task<IActionResult> DownloadOnboardingFile(Guid id, Guid itemId, CancellationToken ct)
    {
        var file = await _service.OpenOnboardingFileAsync(id, itemId, ct);
        if (file is null) return NotFound();
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete("{id:guid}/onboarding/{itemId:guid}/file")]
    [Authorize(Roles = "Admin")]
    [Audited("staff.onboarding.file.delete", "StaffMember")]
    public async Task<ActionResult<StaffDetailDto>> DeleteOnboardingFile(Guid id, Guid itemId, CancellationToken ct)
    {
        var result = await _service.RemoveOnboardingFileAsync(id, itemId, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // Not audited: reference data, the same for everyone, no PII in the response.
    [HttpGet("checklist-template")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<ChecklistTemplateItemDto>>> GetChecklistTemplate() =>
        Ok(await _service.GetChecklistTemplateAsync());

    [HttpPut("checklist-template")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("staff.checklist.update", "ChecklistTemplateItem")]
    public async Task<ActionResult<IReadOnlyList<ChecklistTemplateItemDto>>> UpdateChecklistTemplate([FromBody] UpdateChecklistTemplateDto dto) =>
        Ok(await _service.UpdateChecklistTemplateAsync(dto));
}
