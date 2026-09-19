namespace CRM.Domain.Enums;

public enum AttendanceStatus
{
    Present,
    Absent,
    Unmarked,
    /// <summary>
    /// The class was moved (Sep 2026 client ask). Counts as marked — the register can be
    /// submitted — but is excluded from present/absent rates. Management-only to set.
    /// </summary>
    Rescheduled,
    /// <summary>No class was expected for this star that day. Same counting rule as Rescheduled; management-only.</summary>
    NotScheduled,
}
