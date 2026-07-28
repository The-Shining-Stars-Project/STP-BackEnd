using CRM.Application.DTOs.Calendar;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class CalendarService : ICalendarService
{
    private readonly IUnitOfWork _uow;
    private readonly IOrgClock _clock;

    public CalendarService(IUnitOfWork uow, IOrgClock clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(int month, int year, CancellationToken ct = default)
    {
        var events = await _uow.CalendarEvents.GetAllAsync(ct);
        var programs = await _uow.Programs.GetAllAsync(ct);
        var programMap = programs.ToDictionary(p => p.Id, p => p.Name);

        var today = _clock.Today;
        return events
            .Where(e => e.Date.Month == month && e.Date.Year == year)
            .OrderBy(e => e.Date)
            .Select(e => ToDto(e, programMap, today))
            .ToList();
    }

    public async Task<CalendarEventDto> CreateEventAsync(CreateCalendarEventDto dto)
    {
        if (!DateTime.TryParse(dto.Date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var date))
            throw new ArgumentException($"Invalid date format: '{dto.Date}'. Expected ISO 8601.");

        var ev = new CalendarEvent
        {
            Title = dto.Title,
            Date = date,
            ProgramId = dto.ProgramId,
            Location = dto.Location,
            Meta = dto.Meta,
            TimeRange = dto.TimeRange,
            // Compare dates, not instants (#7): an event dated today is upcoming until the
            // day is over, and "today" is a local question.
            IsUpcoming = date.Date >= _clock.Today,
        };

        await _uow.CalendarEvents.AddAsync(ev);
        await _uow.SaveChangesAsync();

        var programs = await _uow.Programs.GetAllAsync();
        var programMap = programs.ToDictionary(p => p.Id, p => p.Name);

        return ToDto(ev, programMap, _clock.Today);
    }

    private static CalendarEventDto ToDto(CalendarEvent e, Dictionary<Guid, string> programMap, DateTime today) => new()
    {
        Id = e.Id,
        Title = e.Title,
        Location = e.Location,
        Meta = e.Meta,
        Date = e.Date.ToString("yyyy-MM-dd"),
        TimeRange = e.TimeRange,
        ProgramId = e.ProgramId,
        ProgramName = e.ProgramId.HasValue ? programMap.GetValueOrDefault(e.ProgramId.Value) : null,
        IsUpcoming = e.Date.Date >= today,
    };
}
