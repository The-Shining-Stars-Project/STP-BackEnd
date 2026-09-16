using CRM.Application.DTOs.Events;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// Attendance for productions and events — one combined register per event, drawing Stars from
/// any programme or location. Deliberately separate from <see cref="IAttendanceService"/>:
/// event attendance is tracked apart from class attendance at the client's request, and the
/// two never share a record type.
///
/// ACCESS MODEL, which differs from the class path and has to. A class register belongs to one
/// programme, so one Require() covers it. An event register belongs to none, so scope is
/// applied per ENTRY: a teacher sees and marks only the Stars in their own programmes, while
/// an admin or coordinator sees the whole register. Structural changes — creating a register,
/// adding or removing Stars, setting sites or hours, submitting — are management-only, and
/// enforced at the controller with the ManagementWrite policy.
/// </summary>
public interface IEventAttendanceService
{
    Task<IReadOnlyList<EventSessionSummaryDto>> GetEventsAsync(Guid userId, DateTime from, DateTime to);

    /// <summary>Null when the event does not exist. Entries are filtered to the caller's scope.</summary>
    Task<EventRosterDto?> GetRosterAsync(Guid userId, Guid eventSessionId);

    /// <summary>Stars the caller may add — their own programmes' Stars, or all of them for an admin.</summary>
    Task<IReadOnlyList<EventCandidateDto>> GetCandidatesAsync(Guid userId, Guid eventSessionId);

    Task<EventSessionSummaryDto> CreateAsync(Guid userId, CreateEventSessionDto dto);

    /// <summary>Null when not found. Throws when the register is submitted.</summary>
    Task<EventSessionSummaryDto?> UpdateAsync(Guid userId, Guid eventSessionId, UpdateEventSessionDto dto);

    /// <summary>
    /// Idempotent: ids already on the register are skipped rather than duplicated, so a
    /// double-tapped "add stars" is harmless. Returns the refreshed roster.
    /// </summary>
    Task<EventRosterDto?> AddParticipantsAsync(Guid userId, Guid eventSessionId, AddEventParticipantsDto dto);

    /// <summary>
    /// Removes a Star from the register. Only while their record is still Unmarked and the
    /// register is Open — removing a marked Star would silently discard a real observation.
    /// </summary>
    Task<bool> RemoveParticipantAsync(Guid userId, Guid eventSessionId, Guid participantId);

    /// <summary>Marks one Star. False when not found; throws when out of scope or submitted.</summary>
    Task<bool> UpdateRecordAsync(Guid userId, Guid recordId, UpdateEventRecordDto dto);

    Task<EventSessionSummaryDto?> SubmitAsync(Guid userId, Guid eventSessionId);

    /// <summary>Puts a submitted register back to Open so marks can be corrected. Null if it doesn't exist.</summary>
    Task<EventSessionSummaryDto?> ReopenAsync(Guid userId, Guid eventSessionId);

    /// <summary>Removes an event and its attendance marks outright (an event created by mistake). False if it doesn't exist.</summary>
    Task<bool> DeleteAsync(Guid userId, Guid eventSessionId);
}
