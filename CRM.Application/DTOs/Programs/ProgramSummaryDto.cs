using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Programs;

public class ProgramSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public string? SessionSchedule { get; set; }
    public string? DefaultLocation { get; set; }

    /// <summary>Structured meeting days (flags enum), e.g. "Monday, Wednesday, Friday".</summary>
    public MeetingDays MeetingDays { get; set; }
    public ProgramTrack Track { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public int EnrolledCount { get; set; }
    public int? AttendancePct { get; set; }
    public string? NextSessionDate { get; set; }
    public string? NextSessionMeta { get; set; }
    public int AlertCount { get; set; }
}
