using System.ComponentModel.DataAnnotations;

namespace CRM.Application.DTOs.Participants;

public class CreateDocumentRecordDto
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string DocumentType { get; set; } = string.Empty;

    public DateTime? ExpiryDate { get; set; }

    public bool IsComplete { get; set; }
}

public class UpdateDocumentRecordDto
{
    [StringLength(100, MinimumLength = 1)]
    public string? DocumentType { get; set; }

    public DateTime? ExpiryDate { get; set; }

    /// <summary>True clears the stored expiry (null alone means "unchanged").</summary>
    public bool ClearExpiry { get; set; }

    public bool? IsComplete { get; set; }
}
