using System.Security.Claims;
using CRM.Application.DTOs.Programs;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProgramsController : ControllerBase
{
    private readonly IProgramService _service;

    public ProgramsController(IProgramService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProgramSummaryDto>>> GetAll(CancellationToken ct) =>
        Ok(await _service.GetAllAsync(ct));

    /// <summary>The caller's in-scope programs (all for an Admin; assigned programs for staff). Literal route wins over {slug}.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<ProgramSummaryDto>>> GetMine(CancellationToken ct) =>
        Ok(await _service.GetForUserAsync(User.GetUserId(), ct));

    [HttpGet("{slug}")]
    public async Task<ActionResult<ProgramSummaryDto>> GetBySlug(string slug)
    {
        var result = await _service.GetBySlugAsync(slug);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("{slug}/detail")]
    public async Task<ActionResult<ProgramDetailDto>> GetDetail(string slug)
    {
        var result = await _service.GetDetailAsync(slug);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("program.create", "Program")]
    public async Task<ActionResult<ProgramSummaryDto>> Create([FromBody] CreateProgramDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return CreatedAtAction(nameof(GetBySlug), new { slug = result.Slug }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("program.update", "Program")]
    public async Task<ActionResult<ProgramSummaryDto>> Update(Guid id, [FromBody] UpdateProgramDto dto)
    {
        var result = await _service.UpdateAsync(id, dto);
        return result is null ? NotFound() : Ok(result);
    }

    // Program membership decides which children's records a staff account may read, so these
    // two are authorization changes, not roster edits. Without them the log can show an account
    // reading twelve participant detail records with nothing explaining how it gained the scope
    // to do so. EntityId is the account whose access changed; the program rides in metadata.
    [HttpPost("{id:guid}/staff/{staffId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("program.staff.grant", "StaffMember",
        IdRouteKey = "staffId",
        MetadataRouteKeys = ["id"],
        Summary = "Granted a staff account access to a program")]
    public async Task<IActionResult> AssignStaff(Guid id, Guid staffId) =>
        await _service.AssignStaffAsync(id, staffId) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}/staff/{staffId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("program.staff.revoke", "StaffMember",
        IdRouteKey = "staffId",
        MetadataRouteKeys = ["id"],
        Summary = "Revoked a staff account's access to a program")]
    public async Task<IActionResult> UnassignStaff(Guid id, Guid staffId) =>
        await _service.UnassignStaffAsync(id, staffId) ? NoContent() : NotFound();
}
