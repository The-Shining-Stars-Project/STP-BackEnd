namespace CRM.Application.Interfaces;

/// <summary>
/// The organisation's wall clock (#7).
///
/// Attendance, calendars, meeting days and "today" are local-calendar concepts, but the code
/// derived them from <c>DateTime.UtcNow</c>. For a California after-school program that is
/// wrong every afternoon: 4:30pm Pacific is 23:30 UTC in summer and 00:30 the *next day* in
/// winter, so a session opened after 4pm PST was filed under tomorrow's date and checked
/// against tomorrow's <c>MeetingDays</c> flag.
///
/// Use this for anything a human would call a date. Instants — CreatedAt, RevokedAt, token
/// expiry, health-check timestamps — are correctly UTC and should not go through here.
/// </summary>
public interface IOrgClock
{
    /// <summary>Today's date in the organisation's timezone, at midnight.</summary>
    DateTime Today { get; }

    /// <summary>The current local time in the organisation's timezone.</summary>
    DateTime Now { get; }

    /// <summary>The current instant in UTC. For timestamps.</summary>
    DateTime UtcNow { get; }
}
