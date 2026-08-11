using CRM.Application.DTOs.Attendance;

namespace CRM.Application.Interfaces.Services;

public interface IAttendanceService
{
    /// <summary>
    /// Existing session roster by id. Returns null if not found; throws
    /// <see cref="UnauthorizedAccessException"/> if the caller is not assigned to the
    /// session's program.
    /// </summary>
    Task<AttendanceSessionDto?> GetSessionAsync(Guid userId, Guid sessionId);

    /// <summary>
    /// Updates an attendance record's status. Returns false if not found; throws
    /// <see cref="UnauthorizedAccessException"/> if the caller is not assigned to the
    /// record's program, or <see cref="InvalidOperationException"/> if the session is submitted.
    /// </summary>
    Task<bool> UpdateRecordAsync(Guid userId, Guid recordId, UpdateAttendanceDto dto);

    /// <summary>
    /// The session cards for a given date, scoped to the user's programs (all programs
    /// for an Admin). Includes programs scheduled to meet that day plus any program
    /// with a session already started that day (ad-hoc). Does not create anything.
    /// </summary>
    Task<IReadOnlyList<ScheduledSessionDto>> GetScheduledForUserAsync(Guid userId, DateTime date);

    /// <summary>
    /// Gets — or lazily creates — the session for a single program on a date, returning its
    /// roster of active participants. Returns null if the program does not exist; throws
    /// <see cref="UnauthorizedAccessException"/> if the user is not assigned to the program.
    /// </summary>
    Task<SessionRosterDto?> GetOrCreateSessionAsync(Guid userId, Guid programId, DateTime date);

    /// <summary>
    /// Read-only variant (#23): the roster for a program's session on a date, creating
    /// nothing. Returns null if the program or session does not exist; throws
    /// <see cref="UnauthorizedAccessException"/> if the user is not assigned to the program.
    /// </summary>
    Task<SessionRosterDto?> GetProgramSessionReadOnlyAsync(Guid userId, Guid programId, DateTime date);

    /// <summary>
    /// Finalizes a session: marks it Submitted and locks its records. Returns false if the
    /// session is not found; throws <see cref="UnauthorizedAccessException"/> if the user is
    /// not assigned to the session's program.
    /// </summary>
    Task<bool> SubmitSessionAsync(Guid userId, Guid sessionId);
    Task<bool> SetSessionHoursAsync(Guid userId, Guid sessionId, decimal? hours);

    /// <summary>
    /// Today's roster from existing sessions/records only — no lazy creation, no writes —
    /// scoped to the caller's programs (#1). Returns an empty list if no session exists for
    /// today. Intended for read-only views like the dashboard.
    /// </summary>
    Task<IReadOnlyList<AttendanceRosterEntryDto>> GetTodayRosterReadOnlyAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Adds a note to an attendance record. Returns null if the record is not found; throws
    /// <see cref="UnauthorizedAccessException"/> if the caller is not assigned to the
    /// record's program.
    /// </summary>
    Task<AttendanceNoteDto?> AddNoteAsync(Guid userId, Guid recordId, CreateAttendanceNoteDto dto);
}
