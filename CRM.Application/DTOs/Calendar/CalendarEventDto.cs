using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Calendar;

/// <summary>
/// One event on one day. A repeating event comes back once per occurrence in the requested
/// month (Oct 2026): <see cref="Date"/> is that occurrence, <see cref="SeriesStart"/> the first
/// one, and <see cref="Id"/> the series — so the grid renders repeats like any other event.
/// </summary>
public class CalendarEventDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Meta { get; set; }
    public string Date { get; set; } = string.Empty;
    public string? TimeRange { get; set; }
    public Guid? ProgramId { get; set; }
    public string? ProgramName { get; set; }
    public bool IsUpcoming { get; set; }
    /// <summary>The sites this event is for — none means org-wide.</summary>
    public List<Guid> SiteIds { get; set; } = new();
    public List<string> SiteNames { get; set; } = new();

    public RepeatFrequency RepeatFrequency { get; set; }
    public int RepeatInterval { get; set; } = 1;
    /// <summary>0 = Sunday … 6 = Saturday; empty = the start's weekday (Weekly only).</summary>
    public List<int> RepeatWeekdays { get; set; } = new();
    public string? RepeatUntil { get; set; }
    /// <summary>The series' first occurrence (= Date for one-off events).</summary>
    public string SeriesStart { get; set; } = string.Empty;
}

public class CreateCalendarEventDto
{
    public string Title { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public Guid? ProgramId { get; set; }
    public string? Location { get; set; }
    /// <summary>Free-text details; URLs in here render as links.</summary>
    public string? Meta { get; set; }
    public string? TimeRange { get; set; }
    public List<Guid>? SiteIds { get; set; }

    /// <summary>Omitted = None (a one-off event).</summary>
    public RepeatFrequency? RepeatFrequency { get; set; }
    public int? RepeatInterval { get; set; }
    public List<int>? RepeatWeekdays { get; set; }
    /// <summary>yyyy-MM-dd, inclusive; omitted = no end.</summary>
    public string? RepeatUntil { get; set; }
}

/// <summary>Full replacement of an event's fields (the form always sends every field).</summary>
public class UpdateCalendarEventDto : CreateCalendarEventDto
{
}

/// <summary>Which occurrences an edit or delete of a repeating event applies to.</summary>
public enum EventEditScope
{
    /// <summary>The whole series (the only behaviour before Repeat, and still the default).</summary>
    All,
    /// <summary>Just the occurrence on the given date: it is skipped, and an edit becomes a one-off copy.</summary>
    This,
    /// <summary>The occurrence on the given date and everything after: the series is split there.</summary>
    Following,
}
