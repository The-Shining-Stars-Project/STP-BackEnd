using CRM.API.Auditing;
using CRM.Application.DTOs.Participants;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ParticipantsController : ControllerBase
{
    private readonly IParticipantService _service;
    private readonly IArtsProfileService _artsProfile;

    public ParticipantsController(IParticipantService service, IArtsProfileService artsProfile)
    {
        _service = service;
        _artsProfile = artsProfile;
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
}
