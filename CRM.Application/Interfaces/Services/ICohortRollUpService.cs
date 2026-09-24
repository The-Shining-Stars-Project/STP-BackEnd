using CRM.Application.DTOs.Progress;
using CRM.Domain.Enums;

namespace CRM.Application.Interfaces.Services;

public interface ICohortRollUpService
{
    /// <summary>
    /// Per-skill level counts for a month, optionally scoped to one program. Levels are derived
    /// from each star's weekly scores; a confirmed month-end level overrides the derived one.
    /// </summary>
    Task<CohortRollUpDto> GetRollUpAsync(Guid userId, string monthKey, Guid? programId);

    /// <summary>
    /// The Stars behind one cell of the roll-up — which children are at <paramref name="level"/>
    /// on <paramref name="subSkillId"/> this month. Same score-derived rule as the counts, so
    /// the list always reconciles with the number that was clicked.
    /// </summary>
    Task<IReadOnlyList<CohortStarDto>> GetStarsAtLevelAsync(
        Guid userId, string monthKey, Guid subSkillId, ProgressLevel level, Guid? programId);
}
