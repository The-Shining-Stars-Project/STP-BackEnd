namespace CRM.Application.DTOs.Calendar;

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
}

/// <summary>Full replacement of an event's fields (the form always sends every field).</summary>
public class UpdateCalendarEventDto : CreateCalendarEventDto
{
}
