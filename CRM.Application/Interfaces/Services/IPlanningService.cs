using CRM.Application.DTOs.Planning;

namespace CRM.Application.Interfaces.Services;

public interface IPlanningService
{
    /// <summary>
    /// Per-Star plan rows for a month, limited to the caller's programs (#1).
    /// Optionally narrowed further to a single program.
    /// </summary>
    Task<IReadOnlyList<PerStarPlanDto>> GetPerStarPlansAsync(Guid userId, string monthKey, Guid? programId);

    /// <summary>Creates or updates a participant's plan for a month. Null if the participant doesn't exist.</summary>
    Task<PerStarPlanDto?> UpsertPerStarPlanAsync(Guid userId, UpsertPerStarPlanDto dto);
}
