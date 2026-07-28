using CRM.Application.DTOs.Roster;

namespace CRM.Application.Interfaces.Services;

public interface IRosterService
{
    /// <summary>
    /// Roster rows for a term (management view), limited to the caller's programs (#1).
    /// Optionally filtered to one site. Admins see every program.
    /// </summary>
    Task<IReadOnlyList<RosterEntryDto>> GetRosterAsync(Guid userId, int year, int quarter, Guid? siteId);

    /// <summary>The caller's in-scope participants for a term (their assigned programs; all for admins).</summary>
    Task<IReadOnlyList<RosterEntryDto>> GetMyStarsAsync(Guid userId, int year, int quarter);

    /// <summary>Creates or updates a participant's assignment for a term. Null if the participant doesn't exist.</summary>
    Task<RosterEntryDto?> UpsertAssignmentAsync(Guid userId, UpsertRosterAssignmentDto dto);
}
