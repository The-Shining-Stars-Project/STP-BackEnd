using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Attendance;

public class AttendanceSessionDto
{
    public Guid SessionId { get; set; }
    public Guid ProgramId { get; set; }
    public string Date { get; set; } = string.Empty;
    public string? Room { get; set; }
    public string? TimeRange { get; set; }
    public List<AttendanceRecordDto> Records { get; set; } = new();
}

public class AttendanceRecordDto
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public string ParticipantName { get; set; } = string.Empty;
    public string ParticipantInitials { get; set; } = string.Empty;
    public AttendanceStatus Status { get; set; }
    public string? Group { get; set; }
    public List<AttendanceNoteDto> Notes { get; set; } = new();
}

public class AttendanceNoteDto
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string NoteType { get; set; } = string.Empty;
}

public class UpdateAttendanceDto
{
    public AttendanceStatus Status { get; set; }
}

/// <summary>A single participant row in today's cross-program attendance roster.</summary>
public class AttendanceRosterEntryDto
{
    public Guid RecordId { get; set; }
    public Guid ParticipantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public Guid ProgramId { get; set; }
    public string ProgramSlug { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public AttendanceStatus Status { get; set; }
    public List<AttendanceNoteDto> Notes { get; set; } = new();
}

public class CreateAttendanceNoteDto
{
    public string Content { get; set; } = string.Empty;
    public string NoteType { get; set; } = "observation";
}

/// <summary>Body for POST /api/attendance/session — opens (get-or-creates) a program's session for a date.</summary>
public class OpenSessionDto
{
    public Guid ProgramId { get; set; }
    /// <summary>Defaults to today (UTC) when omitted.</summary>
    public DateTime? Date { get; set; }
}

/// <summary>
/// A card on the attendance landing page: one program meeting on a given date,
/// scoped to the current user's programs. <see cref="SessionId"/> is null until
/// the session is started.
/// </summary>
public class ScheduledSessionDto
{
    public Guid? SessionId { get; set; }
    public Guid ProgramId { get; set; }
    public string ProgramSlug { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string? TimeRange { get; set; }
    public string? Room { get; set; }

    /// <summary>"not-started", "in-progress", or "submitted".</summary>
    public string Status { get; set; } = "not-started";
    public int MarkedCount { get; set; }
    public int TotalCount { get; set; }

    /// <summary>True when this session exists on a date the program isn't normally scheduled.</summary>
    public bool IsAdHoc { get; set; }
}

/// <summary>A single session's roster plus its meta — the working view for taking attendance.</summary>
public class SessionRosterDto
{
    public Guid SessionId { get; set; }
    public Guid ProgramId { get; set; }
    public string ProgramSlug { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string ColorHex { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string? TimeRange { get; set; }
    public string? Room { get; set; }

    /// <summary>"open" or "submitted".</summary>
    public string Status { get; set; } = "open";
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Total hours the session ran — recorded for Pathways reporting.</summary>
    public decimal? HoursLogged { get; set; }

    public List<AttendanceRosterEntryDto> Entries { get; set; } = new();
}

/// <summary>Body for PUT /api/attendance/session/{id}/hours.</summary>
public class SetSessionHoursDto
{
    /// <summary>Total hours for the session; null clears the value.</summary>
    public decimal? Hours { get; set; }
}
