using CRM.API.Auditing;
using CRM.Application.DTOs.Events;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

/// <summary>
/// Attendance for productions and events. Separate from AttendanceController by design — the
/// class path is deliberately left byte-for-byte unchanged, and event attendance is tracked
/// apart from class attendance at the client's request.
///
/// Reads are open to any signed-in user and filtered per entry by programme scope inside the
/// service. Structural writes — creating a register, adding or removing Stars, submitting —
/// carry ManagementWrite, matching how the roster's assignment endpoint is gated. Marking a
/// Star is NOT management-only: a teacher must be able to mark their own Stars at a
/// performance, and the service enforces which ones those are.
/// </summary>
[ApiController]
[Authorize]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IEventAttendanceService _service;
    private readonly IOrgClock _clock;

    public EventsController(IEventAttendanceService service, IOrgClock clock)
    {
        _service = service;
        _clock = clock;
    }

    /// <summary>Events in a date range, defaulting to the current month.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventSessionSummaryDto>>> GetEvents(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var today = _clock.Today;
        var start = from?.Date ?? new DateTime(today.Year, today.Month, 1);
        var end = to?.Date ?? start.AddMonths(1).AddDays(-1);
        if (end < start) return BadRequest(new { message = "The end date must not be before the start date." });

        return Ok(await _service.GetEventsAsync(User.GetUserId(), start, end));
    }

    /// <summary>One register with its Stars, filtered to the caller's scope.</summary>
    [HttpGet("{id:guid}")]
    [Audited("event.roster.view", "EventSession")]
    public async Task<ActionResult<EventRosterDto>> GetRoster(Guid id)
    {
        try
        {
            var roster = await _service.GetRosterAsync(User.GetUserId(), id);
            return roster is null ? NotFound() : Ok(roster);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
    }

    /// <summary>Stars the caller could add to this register.</summary>
    [HttpGet("{id:guid}/candidates")]
    public async Task<ActionResult<IReadOnlyList<EventCandidateDto>>> GetCandidates(Guid id) =>
        Ok(await _service.GetCandidatesAsync(User.GetUserId(), id));

    [HttpPost]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("event.create", "EventSession")]
    public async Task<ActionResult<EventSessionSummaryDto>> Create([FromBody] CreateEventSessionDto dto)
    {
        try
        {
            var created = await _service.CreateAsync(User.GetUserId(), dto);
            return CreatedAtAction(nameof(GetRoster), new { id = created.Id }, created);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("event.update", "EventSession")]
    public async Task<ActionResult<EventSessionSummaryDto>> Update(Guid id, [FromBody] UpdateEventSessionDto dto)
    {
        try
        {
            var updated = await _service.UpdateAsync(User.GetUserId(), id, dto);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/participants")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("event.participants.add", "EventSession")]
    public async Task<ActionResult<EventRosterDto>> AddParticipants(Guid id, [FromBody] AddEventParticipantsDto dto)
    {
        try
        {
            var roster = await _service.AddParticipantsAsync(User.GetUserId(), id, dto);
            return roster is null ? NotFound() : Ok(roster);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpDelete("{id:guid}/participants/{participantId:guid}")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("event.participants.remove", "EventSession")]
    public async Task<IActionResult> RemoveParticipant(Guid id, Guid participantId)
    {
        try
        {
            var ok = await _service.RemoveParticipantAsync(User.GetUserId(), id, participantId);
            return ok ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    /// <summary>
    /// Marks one Star present or absent. Deliberately NOT ManagementWrite — a teacher marks
    /// their own Stars at a performance, and the service decides which those are.
    /// </summary>
    [HttpPut("records/{recordId:guid}")]
    [Audited("event.record.update", "EventAttendanceRecord")]
    public async Task<IActionResult> UpdateRecord(Guid recordId, [FromBody] UpdateEventRecordDto dto)
    {
        try
        {
            var ok = await _service.UpdateRecordAsync(User.GetUserId(), recordId, dto);
            return ok ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("event.submit", "EventSession")]
    public async Task<ActionResult<EventSessionSummaryDto>> Submit(Guid id)
    {
        try
        {
            var submitted = await _service.SubmitAsync(User.GetUserId(), id);
            return submitted is null ? NotFound() : Ok(submitted);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
}
