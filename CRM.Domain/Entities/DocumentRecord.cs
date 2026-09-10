using CRM.Domain.Common;

namespace CRM.Domain.Entities;

/// <summary>
/// One piece of a star's paperwork — an intake packet, a POS authorization, an IPP, a
/// photo release. The record can exist before the file does (the intake checklist is
/// tracked whether or not the scan has been uploaded yet), so the file columns are nullable
/// and always null together.
/// </summary>
public class DocumentRecord : BaseEntity
{
    public Guid ParticipantId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public bool IsComplete { get; set; }

    /// <summary>Storage key of the attached file; null when nothing has been uploaded.</summary>
    public string? BlobName { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }

    public Participant Participant { get; set; } = null!;
}
