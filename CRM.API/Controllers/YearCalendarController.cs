using CRM.Application.DTOs.Calendar;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/year-calendar")]
public class YearCalendarController : ControllerBase
{
    private readonly IYearCalendarService _service;

    public YearCalendarController(IYearCalendarService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<YearCalendarDto>> GetCalendar() => Ok(await _service.GetCalendarAsync());

    [HttpGet("key-arts-dates")]
    public async Task<ActionResult<IReadOnlyList<KeyArtsDateDto>>> GetKeyArtsDates() =>
        Ok(await _service.GetKeyArtsDatesAsync());

    [HttpPut("theme")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("calendar.theme.update", "CalendarTheme")]
    public async Task<ActionResult<CalendarThemeDto>> UpsertTheme([FromBody] UpsertCalendarThemeDto dto)
    {
        if (dto.Month < 1 || dto.Month > 12) return BadRequest("Month must be 1–12.");
        if (string.IsNullOrWhiteSpace(dto.ThemeTitle)) return BadRequest("Theme title is required.");
        return Ok(await _service.UpsertThemeAsync(dto));
    }
}
