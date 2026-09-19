namespace CRM.Application.Interfaces;

/// <summary>
/// Per-participant attendance tally, aggregated in SQL. One row per participant who has
/// at least one marked (Present/Absent) record — never one row per attendance record.
/// </summary>
public record ParticipantAttendanceAgg(Guid ParticipantId, Guid ProgramId, int PresentCount, int AbsentCount);

/// <summary>Whole-ledger attendance counts, aggregated in SQL.</summary>
public record AttendanceStatusTotals(int Present, int Absent, int Unmarked, int SessionCount, int Rescheduled = 0, int NotScheduled = 0);

/// <summary>A program's next upcoming session (date + room only).</summary>
public record NextSessionStub(Guid ProgramId, DateTime Date, string? Room);

/// <summary>
/// Purpose-built aggregate queries for the read-heavy endpoints (#11). The generic
/// repository can only load whole tables and join in memory; these run the aggregation
/// in the database and return results bounded by the number of participants/programs,
/// not the (ever-growing) number of attendance records or sessions.
/// Interface lives in Application; the EF implementation lives in Persistence.
/// </summary>
public interface IStatsQueries
{
    /// <summary>Present/Absent counts per participant across all marked records.</summary>
    Task<IReadOnlyList<ParticipantAttendanceAgg>> GetParticipantAttendanceAsync(CancellationToken ct = default);

    /// <summary>Ledger-wide Present/Absent/Unmarked counts and the total session count.</summary>
    Task<AttendanceStatusTotals> GetAttendanceStatusTotalsAsync(CancellationToken ct = default);

    /// <summary>Session count per program.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetSessionCountByProgramAsync(CancellationToken ct = default);

    /// <summary>Each program's next session on/after <paramref name="fromUtc"/>.</summary>
    Task<IReadOnlyDictionary<Guid, NextSessionStub>> GetNextSessionByProgramAsync(DateTime fromUtc, CancellationToken ct = default);
}
