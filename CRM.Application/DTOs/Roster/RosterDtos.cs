namespace CRM.Application.DTOs.Roster;

/// <summary>
/// One participant's roster row for a term: who they are, their program, and their
/// (possibly not-yet-set) Site / StarGroup / assigned-staff placement. Participants
/// with no assignment for the term still appear, with the assignment fields null.
/// </summary>
public class RosterEntryDto
{
    public Guid ParticipantId { get; set; }
    public string ParticipantName { get; set; } = string.Empty;
    /// <summary>The star's current status, so the page can hide former stars by default.</summary>
    public CRM.Domain.Enums.ParticipantStatus Status { get; set; }
    public string ParticipantInitials { get; set; } = string.Empty;
    /// <summary>The enrolment this row is for. A dual-enrolled Star has one row per program.</summary>
    public Guid ProgramId { get; set; }
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    /// <summary>True when this row is the Star's secondary enrolment ("also enrolled in").</summary>
    public bool IsSecondaryEnrollment { get; set; }

    public Guid? AssignmentId { get; set; }
    /// <summary>Primary (first-listed) site — kept for everything that shows one site.</summary>
    public Guid? SiteId { get; set; }
    public string? SiteName { get; set; }
    /// <summary>Every site the Star attends this term, primary first.</summary>
    public List<Guid> SiteIds { get; set; } = new();
    public List<string> SiteNames { get; set; } = new();
    public Guid? StarGroupId { get; set; }
    public string? StarGroupName { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public string? AssignedStaffName { get; set; }
    public bool CountedInRatio { get; set; } = true;
    public string? Notes { get; set; }

    public int Quarter { get; set; }
    public int Year { get; set; }
}

public class UpsertRosterAssignmentDto
{
    public Guid ParticipantId { get; set; }
    /// <summary>Which enrolment to place. Omitted = the Star's primary program (older clients).</summary>
    public Guid? ProgramId { get; set; }
    public int Quarter { get; set; }
    public int Year { get; set; }
    /// <summary>Single-site form, still accepted; ignored when <see cref="SiteIds"/> is given.</summary>
    public Guid? SiteId { get; set; }
    /// <summary>Every site the Star attends this term, primary first. Empty list clears them.</summary>
    public List<Guid>? SiteIds { get; set; }
    public Guid? StarGroupId { get; set; }
    public Guid? AssignedStaffId { get; set; }
    public bool CountedInRatio { get; set; } = true;
    public string? Notes { get; set; }
}
