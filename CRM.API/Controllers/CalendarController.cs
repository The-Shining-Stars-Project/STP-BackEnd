using CRM.Application.DTOs.Calendar;
using CRM.Application.Interfaces.Services;
using CRM.API.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class CalendarController : ControllerBase
{
    private readonly ICalendarService _service;

    public CalendarController(ICalendarService service) => _service = service;

    [HttpGet("events")]
    public async Task<ActionResult<IReadOnlyList<CalendarEventDto>>> GetEvents(
        [FromQuery] int month,
        [FromQuery] int year)
    {
        if (month < 1 || month > 12 || year < 2020)
            return BadRequest("Invalid month or year.");

        return Ok(await _service.GetEventsAsync(month, year));
    }

    // Org calendar planning is a management action; teachers are read-only here (#6).
    [HttpPost("events")]
    [Authorize(Policy = "ManagementWrite")]
    [Audited("calendar.event.create", "CalendarEvent")]
    public async Task<ActionResult<CalendarEventDto>> CreateEvent([FromBody] CreateCalendarEventDto dto)
    {
        var result = await _service.CreateEventAsync(dto);
        return Ok(result);
    }
}
