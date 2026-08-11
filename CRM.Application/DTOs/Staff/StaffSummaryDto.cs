using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Staff;

public class StaffSummaryDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public StaffRole Role { get; set; }
    public string StartDate { get; set; } = string.Empty;
    /// <summary>yyyy-MM-dd; non-null marks the member as former.</summary>
    public string? EndDate { get; set; }
    public bool IsFormer { get; set; }
    public int OnboardingProgressPct { get; set; }
    public List<string> ProgramNames { get; set; } = new();
}
