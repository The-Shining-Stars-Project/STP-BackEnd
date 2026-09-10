using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Participants;

public class ParticipantDetailDto : ParticipantSummaryDto
{
    public List<DocumentRecordDto> Documents { get; set; } = new();
    public List<AttendanceRecordDto> RecentAttendance { get; set; } = new();
}

public class DocumentRecordDto
{
    public Guid Id { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string? ExpiryDate { get; set; }
    public bool IsComplete { get; set; }

    // The attached file, described but never embedded: the bytes come from
    // GET /api/participants/{id}/documents/{docId}/file. The blob name stays server-side.
    public bool HasFile { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
}

public class AttendanceRecordDto
{
    public Guid Id { get; set; }
    public string SessionDate { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
