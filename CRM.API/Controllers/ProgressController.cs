using CRM.Application.DTOs.Progress;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

/// <summary>
/// Weekly scores and month-end progress levels for individual children. Every action passes
/// the caller through to the service, which rejects participants and programs outside their
/// scope with a 403 (#1). A missing participant is a 404.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ProgressController : ControllerBase
{
    private readonly IProgressTrackingService _service;

    public ProgressController(IProgressTrackingService service) => _service = service;

    [HttpGet("focus-skills")]
    public async Task<ActionResult<IReadOnlyList<WeeklyFocusSkillDto>>> GetFocusSkills(
        [FromQuery] Guid programId, [FromQuery] string month)
    {
        if (programId == Guid.Empty || string.IsNullOrWhiteSpace(month)) return BadRequest("programId and month are required.");
        return Ok(await _service.GetFocusSkillsAsync(User.GetUserId(), programId, month));
    }

    [HttpPut("focus-skills")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("progress.focusskills.update", "WeeklyFocusSkill")]
    public async Task<ActionResult<IReadOnlyList<WeeklyFocusSkillDto>>> SetFocusSkills([FromBody] SetFocusSkillsDto dto)
    {
        if (dto.ProgramId == Guid.Empty || string.IsNullOrWhiteSpace(dto.MonthKey) || dto.WeekNumber < 1)
            return BadRequest("programId, monthKey and a weekNumber ≥ 1 are required.");
        return Ok(await _service.SetFocusSkillsAsync(User.GetUserId(), dto));
    }

    [HttpPost("weekly")]
    [Audited("progress.weekly.record", "WeeklyDataEntry")]
    public async Task<ActionResult<WeeklyDataEntryDto>> RecordWeekly([FromBody] RecordWeeklyScoreDto dto)
    {
        if (dto.ParticipantId == Guid.Empty || dto.SubSkillId == Guid.Empty || string.IsNullOrWhiteSpace(dto.MonthKey) || dto.WeekNumber < 1)
            return BadRequest("participantId, subSkillId, monthKey and a weekNumber ≥ 1 are required.");
        var result = await _service.RecordWeeklyScoreAsync(User.GetUserId(), dto);
        return result is null ? NotFound() : Ok(result);
    }

    // A named child's month of scores, notes and narrative. Read-auditing here for the same
    // reason as GET /api/participants/{id}: this is the record, not a list page.
    [HttpGet("star/{participantId:guid}")]
    [Audited("progress.star.view", "Participant")]
    public async Task<ActionResult<StarMonthDto>> GetStarMonth(Guid participantId, [FromQuery] string month)
    {
        if (string.IsNullOrWhiteSpace(month)) return BadRequest("month is required.");
        var result = await _service.GetStarMonthAsync(User.GetUserId(), participantId, month);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("star/{participantId:guid}/compute")]
    [Audited("progress.monthend.compute", "Participant")]
    public async Task<ActionResult<IReadOnlyList<MonthlyProgressSnapshotDto>>> ComputeMonthEnd(Guid participantId, [FromQuery] string month)
    {
        if (string.IsNullOrWhiteSpace(month)) return BadRequest("month is required.");
        var result = await _service.ComputeMonthEndAsync(User.GetUserId(), participantId, month);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("star/{participantId:guid}/confirm")]
    [Audited("progress.monthend.confirm", "Participant")]
    public async Task<ActionResult<MonthlyProgressSnapshotDto>> ConfirmMonthEnd(
        Guid participantId, [FromQuery] string month, [FromBody] ConfirmMonthEndDto dto)
    {
        if (string.IsNullOrWhiteSpace(month) || dto.SubSkillId == Guid.Empty) return BadRequest("month and subSkillId are required.");
        var result = await _service.ConfirmMonthEndAsync(User.GetUserId(), participantId, month, dto);
        return result is null ? NotFound() : Ok(result);
    }

    // Free-text, clinical-adjacent narrative about a named child — the most sensitive thing
    // written anywhere in the app after the participant record itself.
    [HttpPost("star/{participantId:guid}/note")]
    [Audited("progress.note.update", "Participant")]
    public async Task<ActionResult<WeeklyNoteSelectionDto>> UpsertNote(
        Guid participantId, [FromQuery] string month, [FromBody] UpsertNoteSelectionDto dto)
    {
        if (string.IsNullOrWhiteSpace(month) || dto.WeekNumber < 1) return BadRequest("month and a weekNumber ≥ 1 are required.");
        var result = await _service.UpsertNoteSelectionAsync(User.GetUserId(), participantId, month, dto);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("star/{participantId:guid}/summary")]
    [Audited("progress.summary.update", "Participant")]
    public async Task<ActionResult<MonthlySummaryDto>> UpsertSummary(
        Guid participantId, [FromQuery] string month, [FromBody] UpsertMonthlySummaryDto dto)
    {
        if (string.IsNullOrWhiteSpace(month)) return BadRequest("month is required.");
        var result = await _service.UpsertMonthlySummaryAsync(User.GetUserId(), participantId, month, dto);
        return result is null ? NotFound() : Ok(result);
    }
}
