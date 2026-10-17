using CRM.Application.DTOs.Planning;

namespace CRM.Application.Interfaces.Services;

public interface IPlanningService
{
    /// <summary>
    /// Per-Star plan rows for a month, limited to stars the caller can see through either
    /// enrolment (#1). Optionally narrowed to the stars enrolled in one program (primary or
    /// secondary). Each star appears once.
    /// </summary>
    Task<IReadOnlyList<PerStarPlanDto>> GetPerStarPlansAsync(Guid userId, string monthKey, Guid? programId);

    /// <summary>
    /// Creates or updates a participant's plan for a month. Null if the participant doesn't exist;
    /// throws <see cref="UnauthorizedAccessException"/> if they are outside the caller's programs.
    /// </summary>
    /// <remarks>
    /// Authoring a plan is a teacher's job, not a coordinator's, so there is no role gate above
    /// this — the program scope is the only thing standing between a teacher and another
    /// program's children. Keep it.
    /// </remarks>
    Task<PerStarPlanDto?> UpsertPerStarPlanAsync(Guid userId, UpsertPerStarPlanDto dto);
}
