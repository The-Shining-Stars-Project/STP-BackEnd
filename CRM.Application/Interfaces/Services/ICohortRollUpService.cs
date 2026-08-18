using CRM.Application.DTOs.Progress;
using CRM.Domain.Enums;

namespace CRM.Application.Interfaces.Services;

public interface ICohortRollUpService
{
    /// <summary>Per-skill level counts from confirmed month-end snapshots, optionally scoped to one program.</summary>
    Task<CohortRollUpDto> GetRollUpAsync(string monthKey, Guid? programId);

    /// <summary>
    /// The Stars behind one cell of the roll-up — which children are at <paramref name="level"/>
    /// on <paramref name="subSkillId"/> this month. Same confirmed-only rule as the counts, so
    /// the list always reconciles with the number that was clicked.
    /// </summary>
    Task<IReadOnlyList<CohortStarDto>> GetStarsAtLevelAsync(
        string monthKey, Guid subSkillId, ProgressLevel level, Guid? programId);
}
