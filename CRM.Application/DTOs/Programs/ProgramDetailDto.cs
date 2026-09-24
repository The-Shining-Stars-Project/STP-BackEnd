using CRM.Application.DTOs.Calendar;
using CRM.Application.DTOs.Participants;
using CRM.Application.DTOs.Staff;

using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Programs;

public class ProgramDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public string? SessionSchedule { get; set; }
    public string? DefaultLocation { get; set; }
    public ProgramTrack Track { get; set; }
    public int EnrolledCount { get; set; }
    public int? AttendancePct { get; set; }
    public List<ParticipantSummaryDto> Participants { get; set; } = new();
    public List<CalendarEventDto> UpcomingEvents { get; set; } = new();
    public List<StaffSummaryDto> Staff { get; set; } = new();
    public List<ProgramAlertDto> Alerts { get; set; } = new();
}

public class ProgramAlertDto
{
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>The participant this alert is about, when applicable — lets the UI link straight to their profile.</summary>
    public Guid? ParticipantId { get; set; }
}
