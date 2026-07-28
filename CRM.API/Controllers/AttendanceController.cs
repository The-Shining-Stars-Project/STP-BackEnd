using System.Security.Claims;
using CRM.Application.DTOs.Attendance;
using CRM.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AttendanceController : ControllerBase
{
    private readonly IAttendanceService _service;

    public AttendanceController(IAttendanceService service) => _service = service;

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
        var when = date?.Date ?? DateTime.UtcNow.Date;
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
        var when = date?.Date ?? DateTime.UtcNow.Date;
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
    public async Task<ActionResult<SessionRosterDto>> OpenSession([FromBody] OpenSessionDto dto)
    {
        var when = dto.Date?.Date ?? DateTime.UtcNow.Date;
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

    [HttpPut("{recordId:guid}")]
    public async Task<IActionResult> UpdateRecord(Guid recordId, [FromBody] UpdateAttendanceDto dto)
    {
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
