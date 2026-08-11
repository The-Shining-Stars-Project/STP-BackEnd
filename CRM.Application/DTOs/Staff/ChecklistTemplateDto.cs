using System.ComponentModel.DataAnnotations;

namespace CRM.Application.DTOs.Staff;

public class ChecklistTemplateItemDto
{
    public string Section { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public class UpdateChecklistTemplateDto
{
    [Required]
    public List<ChecklistTemplateItemDto> Items { get; set; } = new();
}

public class SetOnboardingItemDto
{
    public bool IsCompleted { get; set; }

    /// <summary>Due/renewal date for expiring items (CPR, TB, Mandated Reporter…).</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>True clears the stored expiry (null alone means "unchanged").</summary>
    public bool ClearExpiry { get; set; }
}
