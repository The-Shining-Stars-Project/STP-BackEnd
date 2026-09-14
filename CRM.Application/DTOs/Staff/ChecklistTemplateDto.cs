using System.ComponentModel.DataAnnotations;

namespace CRM.Application.DTOs.Staff;

public class ChecklistTemplateItemDto
{
    public string Section { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    /// <summary>Renewal interval in months for items that expire (TB 48, CPR 24, harassment 24); null for one-time items.</summary>
    [Range(1, 240)]
    public int? RenewalMonths { get; set; }
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

    /// <summary>When the item was done. With a renewal interval on the item, the expiry is stamped from this date.</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Marks the item not required for this person (null = unchanged).</summary>
    public bool? IsNotApplicable { get; set; }
}
