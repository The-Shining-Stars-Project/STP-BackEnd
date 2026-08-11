using CRM.Application.DTOs.Reports;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

public class ReportsService : IReportsService
{
    private readonly IUnitOfWork _uow;
    private readonly IStatsQueries _stats;

    public ReportsService(IUnitOfWork uow, IStatsQueries stats)
    {
        _uow = uow;
        _stats = stats;
    }

    public async Task<ReportsDto> GetAsync(CancellationToken ct = default)
    {
        // Bounded tables (participants, programs, staff, tasks) load once; the unbounded
        // ones (attendance records, sessions) arrive as SQL aggregates (#11) — this
        // endpoint no longer scales with the size of the attendance ledger.
        var participants = await _uow.Participants.GetAllAsync(ct);
        var programs = await _uow.Programs.GetAllAsync(ct);
        var staff = await _uow.Staff.GetAllAsync(ct);
        var tasks = await _uow.Tasks.GetAllAsync(ct);

        var attendanceAgg = await _stats.GetParticipantAttendanceAsync(ct);
        var attendanceTotals = await _stats.GetAttendanceStatusTotalsAsync(ct);
        var sessionCountByProgram = await _stats.GetSessionCountByProgramAsync(ct);

        // Real per-participant attendance % (#8), from the SQL-side aggregates.
        var pctMap = AttendanceStats.PercentByParticipant(attendanceAgg);

        var ptsByProgram = participants
            .GroupBy(p => p.ProgramId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var programReports = programs
            .Select(p =>
            {
                var pts = ptsByProgram.GetValueOrDefault(p.Id, new());
                return new ProgramReportDto
                {
                    Slug = p.Slug,
                    Name = p.Name,
                    Enrolled = pts.Count(x => x.Status == ParticipantStatus.Active),
                    AttendancePct = pts.Count > 0 ? (int)Math.Round(pts.Average(x => (double)pctMap.GetValueOrDefault(x.Id, 0))) : 0,
                    Sessions = sessionCountByProgram.GetValueOrDefault(p.Id, 0),
                };
            })
            .OrderBy(p => p.Name)
            .ToList();

        var totals = new ReportTotalsDto
        {
            TotalParticipants = participants.Count,
            ActiveParticipants = participants.Count(p => p.Status == ParticipantStatus.Active),
            Prospective = participants.Count(p => p.Status == ParticipantStatus.Prospective),
            Attention = participants.Count(p => p.Status == ParticipantStatus.Attention),
            Former = participants.Count(p => p.Status == ParticipantStatus.Former),
            Programs = programs.Count,
            Staff = staff.Count,
            FullyOnboardedStaff = staff.Count(s => s.OnboardingProgressPct == 100),
            AvgAttendancePct = participants.Count > 0 ? (int)Math.Round(participants.Average(p => (double)pctMap.GetValueOrDefault(p.Id, 0))) : 0,
            OpenTasks = tasks.Count(t => t.Status != Domain.Enums.TaskStatus.Done),
            OverdueTasks = tasks.Count(t => t.IsOverdue || t.Status == Domain.Enums.TaskStatus.Overdue),
        };

        // Per-star attendance incl. absences — only statuses that track attendance.
        var aggByParticipant = attendanceAgg.ToDictionary(a => a.ParticipantId);
        var programById = programs.ToDictionary(p => p.Id);
        var starAttendance = participants
            .Where(p => p.Status is ParticipantStatus.Active or ParticipantStatus.Attention)
            .Select(p =>
            {
                aggByParticipant.TryGetValue(p.Id, out var agg);
                var present = agg?.PresentCount ?? 0;
                var absent = agg?.AbsentCount ?? 0;
                programById.TryGetValue(p.ProgramId, out var prog);
                return new StarAttendanceReportDto
                {
                    ParticipantId = p.Id,
                    Name = p.FullName,
                    ProgramName = prog?.Name ?? string.Empty,
                    ProgramSlug = prog?.Slug ?? string.Empty,
                    Status = p.Status.ToString(),
                    Present = present,
                    Absent = absent,
                    PresentRatePct = AttendanceStats.Percent(present, present + absent),
                };
            })
            .OrderByDescending(s => s.Absent)
            .ThenBy(s => s.Name)
            .ToList();

        var staffOnboarding = staff
            .OrderBy(s => s.OnboardingProgressPct)
            .ThenBy(s => s.FullName)
            .Select(s => new StaffOnboardingReportDto { Name = s.FullName, Pct = s.OnboardingProgressPct })
            .ToList();

        return new ReportsDto
        {
            Totals = totals,
            Programs = programReports,
            StaffOnboarding = staffOnboarding,
            StarAttendance = starAttendance,
            Attendance = new AttendanceSummaryDto
            {
                Sessions = attendanceTotals.SessionCount,
                Present = attendanceTotals.Present,
                Absent = attendanceTotals.Absent,
                Unmarked = attendanceTotals.Unmarked,
                PresentRatePct = AttendanceStats.Percent(
                    attendanceTotals.Present, attendanceTotals.Present + attendanceTotals.Absent),
            },
        };
    }
}
