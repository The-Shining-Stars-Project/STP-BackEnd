using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

/// <summary>
/// One combined attendance register for a production or event — deliberately NOT a
/// <see cref="Session"/>.
///
/// A class register derives its roster from a programme; an event register cannot, because the
/// client asked to "pull students from any location" for a single combined list. That inverts
/// the relationship: membership here is explicit rather than derived, which is why this is a
/// separate table rather than a Kind column on Session. Keeping it separate also means the
/// unique index guarding the class check-then-insert race (SessionConfiguration) is never
/// touched, and class attendance percentages cannot accidentally absorb event records.
///
/// Event attendance is tracked SEPARATELY from class attendance at the client's request
/// (Aug 2026). Nothing here feeds <see cref="Common"/> attendance statistics.
/// </summary>
public class EventSession : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public EventCategory Category { get; set; }

    /// <summary>Normalized to midnight by the service, matching the class-session convention.</summary>
    public DateTime Date { get; set; }

    public string? Venue { get; set; }
    public string? TimeRange { get; set; }
    public decimal? HoursLogged { get; set; }

    /// <summary>Open until submitted; a submitted register is locked to further marking.</summary>
    public SessionStatus Status { get; set; } = SessionStatus.Open;
    public DateTime? SubmittedAt { get; set; }

    /// <summary>
    /// Optional link to the calendar entry this register belongs to. Nullable and NOT unique:
    /// SQL Server treats NULLs as equal in a unique index, so a unique constraint here would
    /// permit exactly one unlinked register in the entire table.
    /// </summary>
    public Guid? CalendarEventId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public ICollection<EventSessionSite> Sites { get; set; } = new List<EventSessionSite>();
    public ICollection<EventAttendanceRecord> AttendanceRecords { get; set; } = new List<EventAttendanceRecord>();
}
