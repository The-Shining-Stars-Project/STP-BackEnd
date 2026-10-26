using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// One occurrence of a repeating <see cref="CalendarEvent"/> that has been removed — "delete just
/// this one", or the original slot of an occurrence edited on its own (which becomes a separate
/// one-off event). Occurrences are expanded at read time, so a skip is the only record of the gap.
/// </summary>
public class CalendarEventSkip : BaseEntity
{
    public Guid CalendarEventId { get; set; }
    /// <summary>The occurrence date being skipped (date only).</summary>
    public DateTime Date { get; set; }

    public CalendarEvent? CalendarEvent { get; set; }
}
