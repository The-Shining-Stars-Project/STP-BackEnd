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
        var events = (await _uow.CalendarEvents.GetAllAsync(ct))
            .Where(e => e.Date.Month == month && e.Date.Year == year)
            .OrderBy(e => e.Date)
            .ToList();
        var programMap = (await _uow.Programs.GetAllAsync(ct)).ToDictionary(p => p.Id, p => p.Name);
        var sites = await SitesForAsync(events.Select(e => e.Id).ToList());
        var today = _clock.Today;

        return events.Select(e => ToDto(e, programMap, sites, today)).ToList();
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
        await _uow.ReplaceCalendarEventSitesAsync(ev.Id, dto.SiteIds ?? new List<Guid>());
        await _uow.SaveChangesAsync();

        return await OneAsync(ev);
    }

    public async Task<CalendarEventDto?> UpdateEventAsync(Guid id, UpdateCalendarEventDto dto)
    {
        var ev = await _uow.CalendarEvents.GetByIdAsync(id);
        if (ev is null) return null;
        if (!DateTime.TryParse(dto.Date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var date))
            throw new ArgumentException($"Invalid date format: '{dto.Date}'. Expected ISO 8601.");

        ev.Title = dto.Title;
        ev.Date = date;
        ev.ProgramId = dto.ProgramId;
        ev.Location = dto.Location;
        ev.Meta = dto.Meta;
        ev.TimeRange = dto.TimeRange;
        ev.IsUpcoming = date.Date >= _clock.Today;
        await _uow.CalendarEvents.UpdateAsync(ev);
        await _uow.ReplaceCalendarEventSitesAsync(ev.Id, dto.SiteIds ?? new List<Guid>());
        await _uow.SaveChangesAsync();

        return await OneAsync(ev);
    }

    public async Task<bool> DeleteEventAsync(Guid id)
    {
        var ev = await _uow.CalendarEvents.GetByIdAsync(id);
        if (ev is null) return false;
        // The site rows cascade with the event.
        await _uow.CalendarEvents.DeleteAsync(ev);
        await _uow.SaveChangesAsync();
        return true;
    }

    private async Task<CalendarEventDto> OneAsync(CalendarEvent ev)
    {
        var programMap = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);
        var sites = await SitesForAsync(new[] { ev.Id });
        return ToDto(ev, programMap, sites, _clock.Today);
    }

    /// <summary>Site ids and names per event, in site sort order.</summary>
    private async Task<Dictionary<Guid, List<(Guid Id, string Name)>>> SitesForAsync(IReadOnlyCollection<Guid> eventIds)
    {
        if (eventIds.Count == 0) return new();
        var siteMap = (await _uow.Sites.GetAllAsync()).ToDictionary(s => s.Id);
        var links = await _uow.GetCalendarEventSitesAsync(eventIds);
        return links
            .GroupBy(l => l.CalendarEventId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => siteMap.GetValueOrDefault(l.SiteId))
                      .OfType<Site>()
                      .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
                      .Select(s => (s.Id, s.Name))
                      .ToList());
    }

    private static CalendarEventDto ToDto(
        CalendarEvent e, Dictionary<Guid, string> programMap,
        Dictionary<Guid, List<(Guid Id, string Name)>> sites, DateTime today)
    {
        var mine = sites.GetValueOrDefault(e.Id) ?? new();
        return new()
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
            SiteIds = mine.Select(s => s.Id).ToList(),
            SiteNames = mine.Select(s => s.Name).ToList(),
        };
    }
}
