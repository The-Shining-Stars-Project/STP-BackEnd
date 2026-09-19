using System.Security.Claims;
using CRM.Application.DTOs.Attendance;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using CRM.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;
    private readonly IOrgClock _clock;
    private readonly IAuthorizationService _auth;

    public AttendanceController(IAttendanceService service, IOrgClock clock, IAuthorizationService auth)
    {
        _service = service;
        _clock = clock;
        _auth = auth;
    }

    /// <summary>Rescheduled / Not scheduled are management calls (client rule, Sep 2026); teachers keep Present / Absent.</summary>
    private async Task<bool> MayUseAsync(AttendanceStatus status)
    {
        if (status is not (AttendanceStatus.Rescheduled or AttendanceStatus.NotScheduled)) return true;
        return (await _auth.AuthorizeAsync(User, null, "ManagementWrite")).Succeeded;
    }

    // GET today was removed (#2): it returned every program's participants to any signed-in
    // user, and — being a GET that lazily created sessions and records — let a prefetch open
    // sessions for programs that never met. Use GET scheduled + GET/POST session instead.

    /// <summary>
    /// The session cards for a date (defaults to today), scoped to the caller's programs.
    /// Lists programs scheduled to meet that day plus any with a session already started.
    /// </summary>
    [HttpGet("scheduled")]
    public async Task<ActionResult<IReadOnlyList<ScheduledSessionDto>>> GetScheduled([FromQuery] DateTime? date)
    {
        var when = date?.Date ?? _clock.Today;
        return Ok(await _service.GetScheduledForUserAsync(User.GetUserId(), when));
    }

    /// <summary>
    /// Reads a program's session roster for a date — creates nothing (#23).
    /// Returns 404 if no session has been opened; use <c>POST session</c> to open one.
    /// </summary>
    [HttpGet("session")]
    public async Task<ActionResult<SessionRosterDto>> GetSessionByProgram(
        [FromQuery] Guid programId, [FromQuery] DateTime? date)
    {
        var when = date?.Date ?? _clock.Today;
        try
        {
            var roster = await _service.GetProgramSessionReadOnlyAsync(User.GetUserId(), programId, when);
            return roster is null ? NotFound() : Ok(roster);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>Opens (gets or creates) the session for a program on a date and returns its roster.</summary>
    [HttpPost("session")]
    [Audited("attendance.session.open", "Session")]
    public async Task<ActionResult<SessionRosterDto>> OpenSession([FromBody] OpenSessionDto dto)
    {
        var when = dto.Date?.Date ?? _clock.Today;
        try
        {
            var roster = await _service.GetOrCreateSessionAsync(User.GetUserId(), dto.ProgramId, when);
            return roster is null ? NotFound() : Ok(roster);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>Existing session roster by id (no creation).</summary>
    [HttpGet("session/{sessionId:guid}")]
    public async Task<ActionResult<AttendanceSessionDto>> GetSession(Guid sessionId)
    {
        try
        {
            var result = await _service.GetSessionAsync(User.GetUserId(), sessionId);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>Finalizes a session, locking its records.</summary>
    [HttpPost("session/{sessionId:guid}/submit")]
    [Audited("attendance.session.submit", "Session")]
    public async Task<IActionResult> SubmitSession(Guid sessionId)
    {
        try
        {
            var ok = await _service.SubmitSessionAsync(User.GetUserId(), sessionId);
            return ok ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    /// <summary>Records the session's total hours (Pathways attendance-time reporting).</summary>
    [HttpPut("session/{sessionId:guid}/hours")]
    [Audited("attendance.session.hours", "Session")]
    public async Task<IActionResult> SetSessionHours(Guid sessionId, [FromBody] SetSessionHoursDto dto)
    {
        if (dto.Hours is < 0 or > 24) return BadRequest(new { message = "Hours must be between 0 and 24." });
        try
        {
            var ok = await _service.SetSessionHoursAsync(User.GetUserId(), sessionId, dto.Hours);
            return ok ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    [HttpPut("{recordId:guid}")]
    [Audited("attendance.record.update", "AttendanceRecord")]
    public async Task<IActionResult> UpdateRecord(Guid recordId, [FromBody] UpdateAttendanceDto dto)
    {
        if (!await MayUseAsync(dto.Status))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only coordinators and admins can mark a star Rescheduled or Not scheduled." });
        try
        {
            var updated = await _service.UpdateRecordAsync(User.GetUserId(), recordId, dto);
            return updated ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPost("{recordId:guid}/notes")]
    [Audited("attendance.note.create", "AttendanceRecord")]
    public async Task<ActionResult<AttendanceNoteDto>> AddNote(Guid recordId, [FromBody] CreateAttendanceNoteDto dto)
    {
        try
        {
            var note = await _service.AddNoteAsync(User.GetUserId(), recordId, dto);
            return note is null ? NotFound() : Ok(note);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }
}
