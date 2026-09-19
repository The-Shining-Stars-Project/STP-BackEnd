namespace CRM.Application.DTOs.Reports;

/// <summary>Aggregated, read-only reporting snapshot across the whole org.</summary>
public class ReportsDto
{
    public ReportTotalsDto Totals { get; set; } = new();
    public List<ProgramReportDto> Programs { get; set; } = new();
    public List<StaffOnboardingReportDto> StaffOnboarding { get; set; } = new();
    public AttendanceSummaryDto Attendance { get; set; } = new();
    public List<StarAttendanceReportDto> StarAttendance { get; set; } = new();
}

/// <summary>Per-star attendance tally — presents, absences, and rate across all marked records.</summary>
public class StarAttendanceReportDto
{
    public Guid ParticipantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;
    public string ProgramSlug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Present { get; set; }
    public int Absent { get; set; }
    public int PresentRatePct { get; set; }
}

public class ReportTotalsDto
{
    public int TotalParticipants { get; set; }
    public int ActiveParticipants { get; set; }
    public int Prospective { get; set; }
    public int Attention { get; set; }
    public int Former { get; set; }
    public int Programs { get; set; }
    public int Staff { get; set; }
    public int FullyOnboardedStaff { get; set; }
    public int AvgAttendancePct { get; set; }
    public int OpenTasks { get; set; }
    public int OverdueTasks { get; set; }
}

public class ProgramReportDto
{
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Enrolled { get; set; }
    public int AttendancePct { get; set; }
    public int Sessions { get; set; }
}

public class StaffOnboardingReportDto
{
    public string Name { get; set; } = string.Empty;
    public int Pct { get; set; }
}

/// <summary>Marked-attendance totals across every recorded session.</summary>
public class AttendanceSummaryDto
{
    public int Sessions { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Unmarked { get; set; }
    public int Rescheduled { get; set; }
    public int NotScheduled { get; set; }
    /// <summary>Present / (Present + Absent), as a whole percent.</summary>
    public int PresentRatePct { get; set; }
}
