namespace CRM.Application.DTOs.Progress;

/// <summary>
/// One Star behind a roll-up count. Returned on demand rather than inlined into every row:
/// with ~48 active skills and a full cohort, embedding names in the roll-up would multiply
/// its payload by two orders of magnitude for a detail most readers never open.
/// </summary>
public class CohortStarDto
{
    public Guid ParticipantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
}

/// <summary>One sub-skill's level distribution across the cohort for a month.</summary>
public class CohortRollUpRowDto
{
    public Guid SubSkillId { get; set; }
    public string SubSkillName { get; set; } = string.Empty;
    public int SectionNumber { get; set; }
    public string ObjectiveAreaName { get; set; } = string.Empty;
    public string ObjectiveAreaColorHex { get; set; } = string.Empty;

    public int NoviceCount { get; set; }
    public int IntermediateCount { get; set; }
    public int ExpertCount { get; set; }
    public int NotApplicableCount { get; set; }

    /// <summary>Novice + Intermediate + Expert (excludes N/A) — the number of Stars with a real level.</summary>
    public int ScoredCount { get; set; }
    /// <summary>"Novice" / "Intermediate" / "Expert", or "—" when nothing is scored.</summary>
    public string MostCommonLevel { get; set; } = "—";
}

/// <summary>
/// Where the cohort lives this month — per-skill level counts aggregated from CONFIRMED
/// month-end snapshots only. Computed live (no stored table). Optionally scoped to one program.
/// </summary>
public class CohortRollUpDto
{
    public string MonthKey { get; set; } = string.Empty;
    public Guid? ProgramId { get; set; }
    public string? ProgramName { get; set; }
    /// <summary>Distinct Stars with at least one confirmed level this month (within scope).</summary>
    public int ParticipantCount { get; set; }
    public List<CohortRollUpRowDto> Rows { get; set; } = new();
}
