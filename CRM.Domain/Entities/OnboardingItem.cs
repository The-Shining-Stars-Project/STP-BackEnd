using CRM.Domain.Common;

namespace CRM.Domain.Entities;

public class OnboardingItem : BaseEntity
{
    public Guid StaffMemberId { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Copied from the template item; expiry = completion + this many months.</summary>
    public int? RenewalMonths { get; set; }

    /// <summary>Not required for this person (e.g. fingerprinting for someone never at a school site). Excluded from progress and alerts.</summary>
    public bool IsNotApplicable { get; set; }

    /// <summary>
    /// The paperwork behind the checkbox — a signed offer letter, the I-9, the TB result.
    /// Any item can carry one file; the "Documents" section of the template is where it is
    /// expected. Null when nothing has been uploaded; the four columns move together.
    /// </summary>
    public string? BlobName { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }

    public StaffMember StaffMember { get; set; } = null!;
}
