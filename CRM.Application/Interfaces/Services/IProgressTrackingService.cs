using CRM.Application.DTOs.Progress;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// Assessment data for individual children. Every method takes the calling user and is
/// scoped to their programs (#1) — the <c>userId</c> here is an authorization input, not
/// only the audit stamp it used to be.
/// </summary>
public interface IProgressTrackingService
{
    /// <summary>The focus skills set for a program across a month (all weeks).</summary>
    Task<IReadOnlyList<WeeklyFocusSkillDto>> GetFocusSkillsAsync(Guid currentUserId, Guid programId, string monthKey);

    /// <summary>Replaces the focus skills for one program-week; returns the new set.</summary>
    Task<IReadOnlyList<WeeklyFocusSkillDto>> SetFocusSkillsAsync(Guid currentUserId, SetFocusSkillsDto dto);

    /// <summary>Records (upserts) one weekly Data score for a Star on a sub-skill; stamps the recorder from the caller.</summary>
    Task<WeeklyDataEntryDto?> RecordWeeklyScoreAsync(Guid currentUserId, RecordWeeklyScoreDto dto);

    /// <summary>
    /// Every weekly entry for a program's stars (primary or secondary enrollment) in one month —
    /// the Weekly Data grid's bulk read, one call per program instead of one per star.
    /// Throws <see cref="UnauthorizedAccessException"/> if the program is out of scope.
    /// </summary>
    Task<IReadOnlyList<WeeklyDataEntryDto>> GetProgramMonthAsync(Guid currentUserId, Guid programId, string monthKey);

    /// <summary>A Star's full month: weekly entries + month-end snapshots. Null if the participant doesn't exist.</summary>
    Task<StarMonthDto?> GetStarMonthAsync(Guid currentUserId, Guid participantId, string monthKey);

    /// <summary>(Re)derives suggested month-end levels for every active sub-skill; preserves confirmed levels. Null if the participant doesn't exist.</summary>
    Task<IReadOnlyList<MonthlyProgressSnapshotDto>?> ComputeMonthEndAsync(Guid currentUserId, Guid participantId, string monthKey);

    /// <summary>Confirms (or overrides) a Star's month-end level for one sub-skill; stamps the confirmer from the caller. Null if the participant doesn't exist.</summary>
    Task<MonthlyProgressSnapshotDto?> ConfirmMonthEndAsync(Guid currentUserId, Guid participantId, string monthKey, ConfirmMonthEndDto dto);

    /// <summary>Records (upserts) a Section-6 note for a Star in one week. Null if the participant doesn't exist.</summary>
    Task<WeeklyNoteSelectionDto?> UpsertNoteSelectionAsync(Guid currentUserId, Guid participantId, string monthKey, UpsertNoteSelectionDto dto);

    /// <summary>Creates or updates a Star's monthly summary. Null if the participant doesn't exist.</summary>
    Task<MonthlySummaryDto?> UpsertMonthlySummaryAsync(Guid currentUserId, Guid participantId, string monthKey, UpsertMonthlySummaryDto dto);
}
