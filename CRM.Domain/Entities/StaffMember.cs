using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class StaffMember : BaseEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public StaffRole Role { get; set; } = StaffRole.Teacher;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    /// <summary>Set when the staff member leaves — a non-null end date marks them as former.</summary>
    public DateTime? EndDate { get; set; }

    public int OnboardingProgressPct { get; set; }

    public ICollection<StaffProgramAssignment> ProgramAssignments { get; set; } = new List<StaffProgramAssignment>();
    public ICollection<ProjectTask> AssignedTasks { get; set; } = new List<ProjectTask>();
    public ICollection<OnboardingItem> OnboardingItems { get; set; } = new List<OnboardingItem>();
}
