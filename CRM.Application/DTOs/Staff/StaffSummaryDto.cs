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
    public string? TShirtSize { get; set; }
    public int OnboardingProgressPct { get; set; }
    public List<string> ProgramNames { get; set; } = new();
    public List<Guid> ProgramIds { get; set; } = new();
    /// <summary>Training/paperwork expiring within 60 days or already expired. Admin-only (emptied for others).</summary>
    public List<TrainingAlertDto> TrainingAlerts { get; set; } = new();
}

public class TrainingAlertDto
{
    public Guid ItemId { get; set; }
    public string Label { get; set; } = string.Empty;
    /// <summary>yyyy-MM-dd.</summary>
    public string ExpiryDate { get; set; } = string.Empty;
    /// <summary>Negative when already expired.</summary>
    public int DaysUntil { get; set; }
}
