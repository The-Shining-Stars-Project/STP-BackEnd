using CRM.Application.DTOs.Calendar;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

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

    /// <summary>
    /// The month's events. One-off events land on their date; a repeating event is expanded into
    /// every occurrence in the month (Oct 2026), minus the occurrences that were skipped.
    /// </summary>
    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(int month, int year, CancellationToken ct = default)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        var all = await _uow.CalendarEvents.GetAllAsync(ct);
        var repeatingIds = all.Where(e => e.RepeatFrequency != RepeatFrequency.None).Select(e => e.Id).ToHashSet();
        var skips = repeatingIds.Count == 0
            ? new Dictionary<Guid, HashSet<DateOnly>>()
            : (await _uow.CalendarEventSkips.ListAsync(s => repeatingIds.Contains(s.CalendarEventId), ct))
                .GroupBy(s => s.CalendarEventId)
                .ToDictionary(g => g.Key, g => g.Select(s => DateOnly.FromDateTime(s.Date)).ToHashSet());

        var placed = new List<(CalendarEvent Event, DateOnly On)>();
        foreach (var e in all)
        {
            var skipped = skips.GetValueOrDefault(e.Id);
            foreach (var on in RecurrenceRule.Occurrences(DateOnly.FromDateTime(e.Date), RuleOf(e), first, last))
                if (skipped is null || !skipped.Contains(on))
                    placed.Add((e, on));
        }

        var programMap = (await _uow.Programs.GetAllAsync(ct)).ToDictionary(p => p.Id, p => p.Name);
        var sites = await SitesForAsync(placed.Select(p => p.Event.Id).Distinct().ToList());
        var today = DateOnly.FromDateTime(_clock.Today);

        return placed
            .OrderBy(p => p.On).ThenBy(p => p.Event.Title)
            .Select(p => ToDto(p.Event, p.On, programMap, sites, today))
            .ToList();
    }

    public async Task<CalendarEventDto> CreateEventAsync(CreateCalendarEventDto dto)
    {
        var date = ParseDate(dto.Date, "date");
        var ev = new CalendarEvent { Date = date.ToDateTime(TimeOnly.MinValue) };
        Apply(ev, dto, date);

        await _uow.CalendarEvents.AddAsync(ev);
        await _uow.ReplaceCalendarEventSitesAsync(ev.Id, dto.SiteIds ?? new List<Guid>());
        await _uow.SaveChangesAsync();

        return await OneAsync(ev, date);
    }

    public async Task<CalendarEventDto?> UpdateEventAsync(Guid id, UpdateCalendarEventDto dto, EventEditScope scope = EventEditScope.All, DateOnly? occurrence = null)
    {
        var ev = await _uow.CalendarEvents.GetByIdAsync(id);
        if (ev is null) return null;
        var newDate = ParseDate(dto.Date, "date");
        var start = DateOnly.FromDateTime(ev.Date);

        // A one-off event only has "all"; so does the very first occurrence's "and following".
        var occ = RequireOccurrence(ev, scope, occurrence);
        if (scope == EventEditScope.Following && occ <= start) scope = EventEditScope.All;

        switch (scope)
        {
            case EventEditScope.This:
            {
                // Lift this occurrence out of the series and save the edit as its own event.
                await SkipAsync(ev.Id, occ);
                var single = new CalendarEvent { Date = newDate.ToDateTime(TimeOnly.MinValue) };
                Apply(single, dto, newDate, oneOff: true);
                await _uow.CalendarEvents.AddAsync(single);
                await _uow.ReplaceCalendarEventSitesAsync(single.Id, dto.SiteIds ?? new List<Guid>());
                await _uow.SaveChangesAsync();
                return await OneAsync(single, newDate);
            }
            case EventEditScope.Following:
            {
                // End the old series the day before, and start a new one with the edit.
                ev.RepeatUntil = occ.AddDays(-1).ToDateTime(TimeOnly.MinValue);
                await _uow.CalendarEvents.UpdateAsync(ev);
                var rest = new CalendarEvent { Date = newDate.ToDateTime(TimeOnly.MinValue) };
                Apply(rest, dto, newDate);
                await _uow.CalendarEvents.AddAsync(rest);
                await _uow.ReplaceCalendarEventSitesAsync(rest.Id, dto.SiteIds ?? new List<Guid>());
                await _uow.SaveChangesAsync();
                return await OneAsync(rest, newDate);
            }
            default:
            {
                // The form shows the clicked occurrence's date. Moving it by N days moves the whole
                // series by N days; leaving it alone keeps the series' original start.
                var newStart = occurrence is { } clicked && ev.RepeatFrequency != RepeatFrequency.None
                    ? start.AddDays(newDate.DayNumber - clicked.DayNumber)
                    : newDate;
                ev.Date = newStart.ToDateTime(TimeOnly.MinValue);
                Apply(ev, dto, newStart);
                await _uow.CalendarEvents.UpdateAsync(ev);
                await _uow.ReplaceCalendarEventSitesAsync(ev.Id, dto.SiteIds ?? new List<Guid>());
                await _uow.SaveChangesAsync();
                return await OneAsync(ev, newStart);
            }
        }
    }

    public async Task<bool> DeleteEventAsync(Guid id, EventEditScope scope = EventEditScope.All, DateOnly? occurrence = null)
    {
        var ev = await _uow.CalendarEvents.GetByIdAsync(id);
        if (ev is null) return false;
        var start = DateOnly.FromDateTime(ev.Date);
        var occ = RequireOccurrence(ev, scope, occurrence);

        if (scope == EventEditScope.This)
        {
            await SkipAsync(ev.Id, occ);
            await _uow.SaveChangesAsync();
            return true;
        }
        if (scope == EventEditScope.Following && occ > start)
        {
            ev.RepeatUntil = occ.AddDays(-1).ToDateTime(TimeOnly.MinValue);
            await _uow.CalendarEvents.UpdateAsync(ev);
            await _uow.SaveChangesAsync();
            return true;
        }

        // The site rows and skips cascade with the event.
        await _uow.CalendarEvents.DeleteAsync(ev);
        await _uow.SaveChangesAsync();
        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static RecurrenceRule.Rule RuleOf(CalendarEvent e) =>
        new(e.RepeatFrequency, e.RepeatInterval, e.RepeatWeekdays, e.RepeatUntil is { } u ? DateOnly.FromDateTime(u) : null);

    /// <summary>Copies the form's fields onto <paramref name="ev"/> and validates its repeat against <paramref name="start"/>.</summary>
    private void Apply(CalendarEvent ev, CreateCalendarEventDto dto, DateOnly start, bool oneOff = false)
    {
        var title = dto.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) throw new ArgumentException("Title is required.");

        var until = string.IsNullOrWhiteSpace(dto.RepeatUntil) ? (DateOnly?)null : ParseDate(dto.RepeatUntil, "repeat end date");
        var rule = oneOff
            ? new RecurrenceRule.Rule(RepeatFrequency.None, 1, 0, null)
            : RecurrenceRule.Validate(dto.RepeatFrequency ?? RepeatFrequency.None, dto.RepeatInterval, dto.RepeatWeekdays, until, start);

        ev.Title = title;
        ev.ProgramId = dto.ProgramId;
        ev.Location = dto.Location;
        ev.Meta = dto.Meta;
        ev.TimeRange = dto.TimeRange;
        ev.RepeatFrequency = rule.Frequency;
        ev.RepeatInterval = rule.SafeInterval;
        ev.RepeatWeekdays = rule.WeekdayMask;
        ev.RepeatUntil = rule.Until?.ToDateTime(TimeOnly.MinValue);
        // Compare dates, not instants (#7). Kept for the column; the DTO computes its own.
        ev.IsUpcoming = start >= DateOnly.FromDateTime(_clock.Today);
    }

    /// <summary>
    /// For This/Following the caller must name an occurrence that the series really has (a stale
    /// page could send a date the series no longer covers). One-off events ignore the scope.
    /// </summary>
    private static DateOnly RequireOccurrence(CalendarEvent ev, EventEditScope scope, DateOnly? occurrence)
    {
        var start = DateOnly.FromDateTime(ev.Date);
        if (scope == EventEditScope.All || ev.RepeatFrequency == RepeatFrequency.None) return occurrence ?? start;
        if (occurrence is not { } occ) throw new ArgumentException("Say which occurrence to change (occurrence=yyyy-MM-dd).");
        if (!RecurrenceRule.Occurrences(start, RuleOf(ev), occ, occ).Any())
            throw new ArgumentException("That date isn't part of this repeating event any more. Refresh the calendar and try again.");
        return occ;
    }

    private async Task SkipAsync(Guid eventId, DateOnly on)
    {
        var at = on.ToDateTime(TimeOnly.MinValue);
        if (await _uow.CalendarEventSkips.FirstOrDefaultAsync(s => s.CalendarEventId == eventId && s.Date == at) is not null) return;
        await _uow.CalendarEventSkips.AddAsync(new CalendarEventSkip { CalendarEventId = eventId, Date = at });
    }

    private static DateOnly ParseDate(string? value, string what)
    {
        if (!DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d))
            throw new ArgumentException($"Invalid {what}: '{value}'. Expected yyyy-MM-dd.");
        return DateOnly.FromDateTime(d);
    }

    private async Task<CalendarEventDto> OneAsync(CalendarEvent ev, DateOnly on)
    {
        var programMap = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);
        var sites = await SitesForAsync(new[] { ev.Id });
        return ToDto(ev, on, programMap, sites, DateOnly.FromDateTime(_clock.Today));
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
        CalendarEvent e, DateOnly on, Dictionary<Guid, string> programMap,
        Dictionary<Guid, List<(Guid Id, string Name)>> sites, DateOnly today)
    {
        var mine = sites.GetValueOrDefault(e.Id) ?? new();
        return new()
        {
            Id = e.Id,
            Title = e.Title,
            Location = e.Location,
            Meta = e.Meta,
            Date = on.ToString("yyyy-MM-dd"),
            TimeRange = e.TimeRange,
            ProgramId = e.ProgramId,
            ProgramName = e.ProgramId.HasValue ? programMap.GetValueOrDefault(e.ProgramId.Value) : null,
            IsUpcoming = on >= today,
            SiteIds = mine.Select(s => s.Id).ToList(),
            SiteNames = mine.Select(s => s.Name).ToList(),
            RepeatFrequency = e.RepeatFrequency,
            RepeatInterval = Math.Max(1, e.RepeatInterval),
            RepeatWeekdays = RecurrenceRule.WeekdaysOf(e.RepeatWeekdays),
            RepeatUntil = e.RepeatUntil?.ToString("yyyy-MM-dd"),
            SeriesStart = e.Date.ToString("yyyy-MM-dd"),
        };
    }
}
