using CRM.Application.DTOs.Planning;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

/// <summary>
/// Per-Star plans — a Star's monthly goal, priority skill, and how their teacher intends to
/// support it.
/// </summary>
/// <remarks>
/// Deliberately NOT behind the ManagementWrite policy: the teacher who runs the room writes
/// these, so any signed-in staff member may author a plan for a child in a program they are
/// assigned to. The program scoping in <see cref="IPlanningService"/> is what keeps that
/// safe — it is the whole access control here, so do not remove it on the assumption that a
/// role check is covering this endpoint.
///
/// Contrast with roster assignment (site, Star group, staffing), which is a management
/// decision and does carry ManagementWrite.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/planning")]
public class PlanningController : ControllerBase
{
    private readonly IPlanningService _service;

    public PlanningController(IPlanningService service) => _service = service;

    [HttpGet("per-star")]
    public async Task<ActionResult<IReadOnlyList<PerStarPlanDto>>> GetPerStar(
        [FromQuery] string month, [FromQuery] Guid? programId)
    {
        if (string.IsNullOrWhiteSpace(month)) return BadRequest("month is required (yyyy-MM).");
        return Ok(await _service.GetPerStarPlansAsync(User.GetUserId(), month, programId));
    }

    [HttpPut("per-star")]
    [Audited("planning.perstar.update", "PerStarPlan")]
    public async Task<ActionResult<PerStarPlanDto>> UpsertPerStar([FromBody] UpsertPerStarPlanDto dto)
    {
        if (dto.ParticipantId == Guid.Empty || string.IsNullOrWhiteSpace(dto.MonthKey))
            return BadRequest("participantId and monthKey are required.");
        var result = await _service.UpsertPerStarPlanAsync(User.GetUserId(), dto);
        return result is null ? NotFound() : Ok(result);
    }
}
