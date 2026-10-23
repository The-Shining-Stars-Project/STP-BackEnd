namespace CRM.Application.Interfaces;

/// <summary>
/// Attendance rules the client may want to tune without a deploy. Bound from the
/// <c>Attendance</c> configuration section in Program.cs and registered as a singleton.
/// </summary>
public sealed class AttendanceOptions
{
    /// <summary>
    /// How many days after a class session a teacher may still reopen it to fix marks
    /// (client decision, Oct 2026: 7). Managers are never limited. 0 makes reopen
    /// management-only again.
    /// </summary>
    public int TeacherReopenDays { get; set; } = 7;
}
