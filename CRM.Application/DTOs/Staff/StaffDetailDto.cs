namespace CRM.Application.DTOs.Staff;

public class StaffDetailDto : StaffSummaryDto
{
    public List<OnboardingItemDto> OnboardingItems { get; set; } = new();
}

public class OnboardingItemDto
{
    public Guid Id { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public string? CompletedDate { get; set; }
    public string? ExpiryDate { get; set; }

    // The paperwork behind the item, described but never embedded: bytes come from
    // GET /api/staff/{id}/onboarding/{itemId}/file.
    public bool HasFile { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
}
