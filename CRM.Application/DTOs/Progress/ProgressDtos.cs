using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Progress;

public class WeeklyDataEntryDto
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid SubSkillId { get; set; }
    public Guid? SessionId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public int WeekNumber { get; set; }
    public string WeekDate { get; set; } = string.Empty;
    public DataScore Score { get; set; }
    public Guid? RecordedByStaffMemberId { get; set; }

    /// <summary>
    /// The month-end snapshot for this skill, refreshed by the same save. Returned so the
    /// tracker can update its Month-end column without a follow-up GET — a refetch would
    /// write a progress.star.view audit row for every score keystroke.
    /// </summary>
    public MonthlyProgressSnapshotDto? Snapshot { get; set; }
}

/// <summary>
/// One grid's worth of edits saved in a single request (Sep 2026: per-cell autosave let
/// out-of-order responses "reset" cells, and a set score could never be cleared).
/// </summary>
public class SaveWeeklyScoresDto
{
    public string MonthKey { get; set; } = string.Empty;
    public string? WeekDate { get; set; }
    public List<WeeklyScoreChangeDto> Changes { get; set; } = new();
}

public class WeeklyScoreChangeDto
{
    public Guid ParticipantId { get; set; }
    public Guid SubSkillId { get; set; }
    public int WeekNumber { get; set; }
    /// <summary>Null clears the cell — the entry is deleted and the month-end level re-derived.</summary>
    public DataScore? Score { get; set; }
}

/// <summary>What a bulk save left behind: the surviving entries for every touched (star, skill) and their refreshed month-end snapshots.</summary>
public class SaveWeeklyScoresResultDto
{
    public List<WeeklyDataEntryDto> Entries { get; set; } = new();
    public List<MonthlyProgressSnapshotDto> Snapshots { get; set; } = new();
}

public class RecordWeeklyScoreDto
{
    public Guid ParticipantId { get; set; }
    public Guid SubSkillId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public int WeekNumber { get; set; }
    public string? WeekDate { get; set; }  // ISO date; defaults to today if omitted
    public DataScore Score { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? RecordedByStaffMemberId { get; set; }
}

public class MonthlyProgressSnapshotDto
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid SubSkillId { get; set; }
    public string SubSkillName { get; set; } = string.Empty;
    public int SectionNumber { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public ProgressLevel Level { get; set; }
    public ProgressLevel SuggestedLevel { get; set; }
    public int SummedScore { get; set; }
    public int ScoredWeekCount { get; set; }
    public bool IsConfirmed { get; set; }
    public Guid? ConfirmedByStaffMemberId { get; set; }
}

public class ConfirmMonthEndDto
{
    public Guid SubSkillId { get; set; }
    public ProgressLevel Level { get; set; }
    public Guid? ConfirmedByStaffMemberId { get; set; }
}

public class WeeklyFocusSkillDto
{
    public Guid ProgramId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public int WeekNumber { get; set; }
    public Guid SubSkillId { get; set; }
    public string SubSkillName { get; set; } = string.Empty;
    public int SectionNumber { get; set; }
}

public class SetFocusSkillsDto
{
    public Guid ProgramId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public int WeekNumber { get; set; }
    public List<Guid> SubSkillIds { get; set; } = new();
}

/// <summary>A Star's full month: weekly entries, month-end snapshots, Section-6 notes, and the monthly summary.</summary>
public class StarMonthDto
{
    public Guid ParticipantId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public List<WeeklyDataEntryDto> Entries { get; set; } = new();
    public List<MonthlyProgressSnapshotDto> Snapshots { get; set; } = new();
    public List<WeeklyNoteSelectionDto> NoteSelections { get; set; } = new();
    public MonthlySummaryDto? MonthlySummary { get; set; }

    /// <summary>
    /// The Star's overall level for the month — the average of EVERY weekly score they were
    /// given, pooled across all skills, not the average of the per-skill levels. The client
    /// asked for "the average overall monthly level across all their scores", and pooling the
    /// raw scores is the reading that matches: averaging levels would weight a skill scored
    /// once the same as one scored four times.
    ///
    /// A suggestion for MonthlySummary.PrimaryLevel, never a substitute for it — the same
    /// suggest-then-confirm split the per-skill levels use.
    /// </summary>
    public ProgressLevel SuggestedPrimaryLevel { get; set; }

    /// <summary>How many weekly scores fed the suggestion. Zero means there is nothing to suggest.</summary>
    public int SuggestedPrimaryScoredCount { get; set; }
}

// ── Goal bank + Section-6 notes + monthly summary ─────────────────────────────

public class GoalBankEntryDto
{
    public Guid Id { get; set; }
    public GoalBankKind Kind { get; set; }
    public int SectionNumber { get; set; }
    public ProgressLevel Level { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool HasGrowingEdge { get; set; }
}

public class WeeklyNoteSelectionDto
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public int WeekNumber { get; set; }
    public GoalBankKind Kind { get; set; }
    public Guid? GoalBankEntryId { get; set; }
    public string? CustomText { get; set; }
    /// <summary>The bank entry's text (if a bank entry was chosen), else the custom text — the display value.</summary>
    public string? DisplayText { get; set; }
}

public class UpsertNoteSelectionDto
{
    public int WeekNumber { get; set; }
    public GoalBankKind Kind { get; set; }
    public Guid? GoalBankEntryId { get; set; }
    public string? CustomText { get; set; }
}

public class MonthlySummaryDto
{
    public Guid ParticipantId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public ProgressLevel PrimaryLevel { get; set; }
    public string? ProgressNarrative { get; set; }
    public bool GoalsCarryOver { get; set; }
    public string? NextMonthUpdate { get; set; }
    public bool HasSummary { get; set; }
}

public class UpsertMonthlySummaryDto
{
    public ProgressLevel PrimaryLevel { get; set; }
    public string? ProgressNarrative { get; set; }
    public bool GoalsCarryOver { get; set; } = true;
    public string? NextMonthUpdate { get; set; }
}
