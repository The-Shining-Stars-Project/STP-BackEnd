using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Planning;

/// <summary>One Star's monthly plan row (participant info + their possibly-unset plan for the month).</summary>
public class PerStarPlanDto
{
    public Guid ParticipantId { get; set; }
    public string ParticipantName { get; set; } = string.Empty;
    /// <summary>The star's current status, so the page can hide former stars by default.</summary>
    public CRM.Domain.Enums.ParticipantStatus Status { get; set; }
    public string ParticipantInitials { get; set; } = string.Empty;
    public Guid ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    public string MonthKey { get; set; } = string.Empty;

    public Guid? PlanId { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public string? AssignedStaffName { get; set; }
    /// <summary>"Plan" when the plan names someone, "Roster" when it is the quarter's roster default, null when nobody.</summary>
    public string? AssignedStaffSource { get; set; }
    public ProgressLevel PrimaryTier { get; set; }
    public Guid? PriorityObjectiveAreaId { get; set; }
    public string? PriorityObjectiveAreaName { get; set; }
    public Guid? PrioritySubSkillId { get; set; }
    public string? PrioritySubSkillName { get; set; }
    public string? MonthlyGoal { get; set; }
    public string? HowIllSupport { get; set; }
    public string? Notes { get; set; }
}

public class UpsertPerStarPlanDto
{
    public Guid ParticipantId { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public Guid? AssignedStaffId { get; set; }
    public ProgressLevel PrimaryTier { get; set; }
    public Guid? PriorityObjectiveAreaId { get; set; }
    public Guid? PrioritySubSkillId { get; set; }
    public string? MonthlyGoal { get; set; }
    public string? HowIllSupport { get; set; }
    public string? Notes { get; set; }
}
