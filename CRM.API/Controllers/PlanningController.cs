using CRM.Application.DTOs.Planning;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

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
    public async Task<ActionResult<PerStarPlanDto>> UpsertPerStar([FromBody] UpsertPerStarPlanDto dto)
    {
        if (dto.ParticipantId == Guid.Empty || string.IsNullOrWhiteSpace(dto.MonthKey))
            return BadRequest("participantId and monthKey are required.");
        var result = await _service.UpsertPerStarPlanAsync(User.GetUserId(), dto);
        return result is null ? NotFound() : Ok(result);
    }
}
