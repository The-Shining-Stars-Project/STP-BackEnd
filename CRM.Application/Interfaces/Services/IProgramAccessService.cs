using CRM.Domain.Entities;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// Resolves which programs a signed-in user may read or write. Extracted from
/// AttendanceService (#5) so every participant-scoped surface enforces one rule instead
/// of each service inventing its own — or, as was the case, skipping the check entirely.
/// </summary>
public interface IProgramAccessService
{
    /// <summary>
    /// The caller's program scope: every program for an Admin, otherwise the programs
    /// their linked staff record is assigned to. Users with no linked staff record get an
    /// empty scope (fail closed).
    /// </summary>
    Task<ProgramAccess> ForUserAsync(Guid userId);

    /// <summary>
    /// Loads a participant and asserts the caller may touch their program.
    /// Returns null when the participant doesn't exist so callers can 404;
    /// throws <see cref="UnauthorizedAccessException"/> (→ 403) when it exists but is out of scope.
    /// </summary>
    Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId);
}

/// <summary>
/// A resolved program scope. Admins satisfy every check without enumerating the program
/// table, so <see cref="AssignedProgramIds"/> is empty for them — always ask via
/// <see cref="CanAccess"/> rather than reading the set directly.
/// </summary>
public sealed class ProgramAccess
{
    /// <summary>Scope for a user who may see nothing (unknown, inactive, or no staff link).</summary>
    public static readonly ProgramAccess None = new(false, new HashSet<Guid>());

    public ProgramAccess(bool isAdmin, IReadOnlySet<Guid> assignedProgramIds)
    {
        IsAdmin = isAdmin;
        AssignedProgramIds = assignedProgramIds;
    }

    public bool IsAdmin { get; }

    /// <summary>The programs a non-admin is assigned to. Empty for admins — see the class remarks.</summary>
    public IReadOnlySet<Guid> AssignedProgramIds { get; }

    /// <summary>True when the caller may read or write data belonging to this program.</summary>
    public bool CanAccess(Guid programId) => IsAdmin || AssignedProgramIds.Contains(programId);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> (→ 403) unless the caller may touch this program.</summary>
    public void Require(Guid programId)
    {
        if (!CanAccess(programId))
            throw new UnauthorizedAccessException("You are not assigned to this program.");
    }
}
