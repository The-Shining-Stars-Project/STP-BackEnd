using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class CalendarEvent : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Meta { get; set; }
    public DateTime Date { get; set; }
    public string? TimeRange { get; set; }
    public Guid? ProgramId { get; set; }
    public bool IsUpcoming { get; set; } = true;

    // Repeat (Oct 2026). When RepeatFrequency is not None, Date is the FIRST occurrence and the
    // rest are expanded at read time (see RecurrenceRule) — no copies are stored and nothing runs
    // on a timer. RepeatWeekdays is a Sun=1 … Sat=64 bitmask used by Weekly (0 = Date's weekday).
    public RepeatFrequency RepeatFrequency { get; set; } = RepeatFrequency.None;
    public int RepeatInterval { get; set; } = 1;
    public int RepeatWeekdays { get; set; }
    /// <summary>Last date an occurrence may fall on (inclusive). Null = no end.</summary>
    public DateTime? RepeatUntil { get; set; }

    public CrmProgram? Program { get; set; }
    public ICollection<CalendarEventSite> Sites { get; set; } = new List<CalendarEventSite>();
    public ICollection<CalendarEventSkip> Skips { get; set; } = new List<CalendarEventSkip>();
}
