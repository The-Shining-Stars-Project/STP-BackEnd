using System.Security.Claims;
using CRM.Application.DTOs.Roster;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class RosterController : ControllerBase
{
    private readonly IRosterService _service;

    public RosterController(IRosterService service) => _service = service;

    /// <summary>
    /// The roster for a term (management view), scoped to the caller's programs.
    /// Optionally filtered to one site.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RosterEntryDto>>> GetRoster(
        [FromQuery] int year, [FromQuery] int quarter, [FromQuery] Guid? siteId)
    {
        if (year < 2020 || quarter < 1 || quarter > 4)
            return BadRequest("Provide a valid year and a quarter of 1–4.");
        return Ok(await _service.GetRosterAsync(User.GetUserId(), year, quarter, siteId));
    }

    /// <summary>The caller's in-scope Stars for a term.</summary>
    [HttpGet("my-stars")]
    public async Task<ActionResult<IReadOnlyList<RosterEntryDto>>> GetMyStars(
        [FromQuery] int year, [FromQuery] int quarter)
    {
        if (year < 2020 || quarter < 1 || quarter > 4)
            return BadRequest("Provide a valid year and a quarter of 1–4.");
        return Ok(await _service.GetMyStarsAsync(User.GetUserId(), year, quarter));
    }

    /// <summary>Creates or updates a participant's assignment for a term. Management only.</summary>
    [HttpPut("assignment")]
    [Authorize(Policy = "ManagementWrite")]
    public async Task<ActionResult<RosterEntryDto>> UpsertAssignment([FromBody] UpsertRosterAssignmentDto dto)
    {
        if (dto.Year < 2020 || dto.Quarter < 1 || dto.Quarter > 4)
            return BadRequest("Provide a valid year and a quarter of 1–4.");
        var result = await _service.UpsertAssignmentAsync(User.GetUserId(), dto);
        return result is null ? NotFound() : Ok(result);
    }
}
