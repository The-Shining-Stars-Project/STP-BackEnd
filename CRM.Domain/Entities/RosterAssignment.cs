using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// Places a Star into a Site + StarGroup for a given quarter, and names the staff
/// member who owns their planning that term. Term-scoped (one row per participant per
/// Year+Quarter) so each quarter is a fresh, historically-preserved snapshot. This is
/// the first real Participant↔Staff link. <see cref="AssignedStaffId"/> is attribution
/// (who owns the Star for planning), NOT a visibility gate — teacher access is governed
/// by the program-level <see cref="StaffProgramAssignment"/> chain, matching attendance.
/// </summary>
public class RosterAssignment : BaseEntity
{
    public Guid ParticipantId { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? StarGroupId { get; set; }
    public Guid? AssignedStaffId { get; set; }

    /// <summary>Quarter number 1–4.</summary>
    public int Quarter { get; set; }
    public int Year { get; set; }

    /// <summary>The spreadsheet's "1:6" note as structured data (some Stars are not counted toward the ratio).</summary>
    public bool CountedInRatio { get; set; } = true;
    public string? Notes { get; set; }

    public Participant? Participant { get; set; }
    /// <summary>The primary site (first listed). Every site is in <see cref="Sites"/>.</summary>
    public Site? Site { get; set; }
    public ICollection<RosterAssignmentSite> Sites { get; set; } = new List<RosterAssignmentSite>();
    public StarGroup? StarGroup { get; set; }
    public StaffMember? AssignedStaff { get; set; }
}
