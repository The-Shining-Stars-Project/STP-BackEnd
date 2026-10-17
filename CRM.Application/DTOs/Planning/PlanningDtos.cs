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
    /// <summary>
    /// The program this row is shown under: the filtered program when one was requested,
    /// otherwise the primary if the caller can see it, else the secondary.
    /// </summary>
    public Guid ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    /// <summary>True when <see cref="ProgramId"/> is the star's second program ("also enrolled in").</summary>
    public bool IsSecondaryEnrollment { get; set; }
    /// <summary>
    /// True when the star is in two programs. Their row's Assigned staff must then be someone
    /// assigned to the row's program; the picker filters to those and the save refuses others.
    /// </summary>
    public bool IsDualEnrolled { get; set; }
    public string MonthKey { get; set; } = string.Empty;

    public Guid? PlanId { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public string? AssignedStaffName { get; set; }
    /// <summary>
    /// Where <see cref="AssignedStaffId"/> came from: "Plan" (this month's plan), "Roster" (this
    /// quarter's roster), "PreviousPlan" / "PreviousRoster" (carried over from the most recent
    /// earlier month's plan / quarter's roster), or null when nobody is assigned.
    /// </summary>
    public string? AssignedStaffSource { get; set; }
    /// <summary>
    /// The term the assignment came from: "yyyy-MM" for Plan / PreviousPlan, "Qn yyyy"
    /// (e.g. "Q3 2026") for Roster / PreviousRoster. Null when <see cref="AssignedStaffSource"/> is.
    /// </summary>
    public string? AssignedStaffSourceTerm { get; set; }
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
    /// <summary>
    /// The program the row was shown under (one of the star's enrolments, and one the caller can
    /// access). The response is labelled and its assigned-staff default resolved for this program.
    /// Omitted = the primary if the caller can see it, else the secondary.
    /// </summary>
    public Guid? ProgramId { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public ProgressLevel PrimaryTier { get; set; }
    public Guid? PriorityObjectiveAreaId { get; set; }
    public Guid? PrioritySubSkillId { get; set; }
    public string? MonthlyGoal { get; set; }
    public string? HowIllSupport { get; set; }
    public string? Notes { get; set; }
}
