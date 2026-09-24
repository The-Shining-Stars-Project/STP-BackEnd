using CRM.API.Auditing;
using CRM.Application.DTOs.Progress;
using CRM.Domain.Enums;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/cohort")]
public class CohortController : ControllerBase
{
    private readonly ICohortRollUpService _service;

    public CohortController(ICohortRollUpService service) => _service = service;

    /// <summary>The cohort roll-up for a month (per-skill level counts from confirmed snapshots), optionally by program.</summary>
    [HttpGet("roll-up")]
    public async Task<ActionResult<CohortRollUpDto>> GetRollUp([FromQuery] string month, [FromQuery] Guid? programId)
    {
        if (string.IsNullOrWhiteSpace(month)) return BadRequest("month is required (yyyy-MM).");
        return Ok(await _service.GetRollUpAsync(User.GetUserId(), month, programId));
    }

    /// <summary>
    /// The Stars behind one roll-up count — answers "which students", not just "how many".
    /// Audited: this returns named children at a named developmental level, which is exactly
    /// the kind of read the audit log exists to record.
    /// </summary>
    [HttpGet("roll-up/stars")]
    [Audited("cohort.stars.view", "Participant")]
    public async Task<ActionResult<IReadOnlyList<CohortStarDto>>> GetStarsAtLevel(
        [FromQuery] string month, [FromQuery] Guid subSkillId,
        [FromQuery] ProgressLevel level, [FromQuery] Guid? programId)
    {
        if (string.IsNullOrWhiteSpace(month)) return BadRequest("month is required (yyyy-MM).");
        if (subSkillId == Guid.Empty) return BadRequest("subSkillId is required.");
        return Ok(await _service.GetStarsAtLevelAsync(User.GetUserId(), month, subSkillId, level, programId));
    }
}
