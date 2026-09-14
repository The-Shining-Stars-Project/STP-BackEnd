namespace CRM.Domain.Entities;

/// <summary>
/// Which sites a calendar event is for — "multiple locations per event" (client ask, Sep
/// 2026). Composite primary key, no BaseEntity: the EventSessionSite pattern.
/// </summary>
public class CalendarEventSite
{
    public Guid CalendarEventId { get; set; }
    public Guid SiteId { get; set; }

    public CalendarEvent CalendarEvent { get; set; } = null!;
    public Site Site { get; set; } = null!;
}
