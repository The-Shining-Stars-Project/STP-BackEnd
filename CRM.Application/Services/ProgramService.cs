using CRM.Application.DTOs.Calendar;
using CRM.Application.DTOs.Participants;
using CRM.Application.DTOs.Programs;
using CRM.Application.DTOs.Staff;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;

namespace CRM.Application.Services;

public class ProgramService : IProgramService
{
    private readonly IUnitOfWork _uow;
    private readonly IStatsQueries _stats;
    private readonly IOrgClock _clock;

    private readonly IProgramAccessService _access;

    public ProgramService(IUnitOfWork uow, IStatsQueries stats, IOrgClock clock, IProgramAccessService access)
    {
        _access = access;
        _uow = uow;
        _stats = stats;
        _clock = clock;
    }

    public async Task<IReadOnlyList<ProgramSummaryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var programs = await _uow.Programs.GetAllAsync(ct);
        var participants = await _uow.Participants.GetAllAsync(ct);

        // Attendance % from SQL aggregates (#8/#11); next session per program is a single
        // grouped query instead of loading every session ever created.
        var pctMap = AttendanceStats.PercentByParticipant(await _stats.GetParticipantAttendanceAsync(ct));
        // From the start of today, not the current instant (#7): sessions are stored as
        // dates, so passing "now" hid today's own session from "next session" the moment
        // the clock passed midnight UTC.
        var nextByProgram = await _stats.GetNextSessionByProgramAsync(_clock.Today, ct);

        var ptsByProgram = participants
            .GroupBy(p => p.ProgramId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return programs.Select(p =>
        {
            var pts = ptsByProgram.GetValueOrDefault(p.Id, new());
            var next = nextByProgram.GetValueOrDefault(p.Id);
            return new ProgramSummaryDto
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                ColorHex = p.ColorHex,
                SessionSchedule = p.SessionSchedule,
                DefaultLocation = p.DefaultLocation,
                MeetingDays = p.MeetingDays,
                StartTime = p.StartTime,
                EndTime = p.EndTime,
                EnrolledCount = pts.Count(x => x.Status == Domain.Enums.ParticipantStatus.Active),
                AttendancePct = pts.Any() ? (int)Math.Round(pts.Average(x => (double)pctMap.GetValueOrDefault(x.Id, 0))) : null,
                NextSessionDate = next?.Date.ToString("MMM d"),
                NextSessionMeta = next?.Room is string r ? $"{next.Date:dddd} · {r}" : null,
                AlertCount = pts.Count(x => x.Status == Domain.Enums.ParticipantStatus.Attention),
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<ProgramSummaryDto>> GetForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct);
        var user = await _uow.Users.GetByIdAsync(userId);
        if (user is null) return new List<ProgramSummaryDto>();
        if (user.Role == Domain.Enums.UserRole.Admin) return all;
        if (user.StaffMemberId is not { } staffId) return new List<ProgramSummaryDto>();

        var assignments = await _uow.GetStaffProgramAssignmentsAsync();
        var allowed = assignments.Where(a => a.StaffMemberId == staffId).Select(a => a.ProgramId).ToHashSet();
        return all.Where(p => allowed.Contains(p.Id)).ToList();
    }

    public async Task<ProgramSummaryDto?> GetBySlugAsync(string slug)
    {
        var all = await GetAllAsync();
        return all.FirstOrDefault(p => p.Slug == slug);
    }

    public async Task<ProgramDetailDto?> GetDetailAsync(Guid userId, string slug)
    {
        var programs = await _uow.Programs.GetAllAsync();
        var program = programs.FirstOrDefault(p => p.Slug == slug);
        if (program is null) return null;

        // Scoped like every other star list (#1). This was the one page that showed a
        // teacher every program's children — and the only page an unassigned teacher saw
        // stars on at all, which is how the gap was noticed.
        (await _access.ForUserAsync(userId)).Require(program.Id);

        // Filter in SQL (#11), not after loading the whole table.
        var participants = (await _uow.Participants.ListAsync(p => p.ProgramId == program.Id)).ToList();

        // Real per-participant attendance % (#8), scoped to this program's participants.
        var participantIds = participants.Select(p => p.Id).ToHashSet();
        var pctMap = AttendanceStats.PercentByParticipant(
            await _uow.Attendance.ListAsync(r => participantIds.Contains(r.ParticipantId)));

        var events = (await _uow.CalendarEvents.GetAllAsync())
            .Where(e => e.ProgramId == program.Id && e.IsUpcoming)
            .OrderBy(e => e.Date)
            .Take(5)
            .ToList();

        // Staff list: resolve via StaffProgramAssignments so program detail actually
        // returns its staff (was a dead query + always-empty list).
        var assignments = await _uow.GetStaffProgramAssignmentsAsync();
        var staffIds = assignments
            .Where(a => a.ProgramId == program.Id)
            .Select(a => a.StaffMemberId)
            .ToHashSet();
        var staff = staffIds.Count == 0
            ? new List<StaffMember>()
            : (await _uow.Staff.ListAsync(s => staffIds.Contains(s.Id))).ToList();

        var summary = await GetBySlugAsync(slug);

        return new ProgramDetailDto
        {
            Id = program.Id,
            Name = program.Name,
            Slug = program.Slug,
            ColorHex = program.ColorHex,
            SessionSchedule = program.SessionSchedule,
            DefaultLocation = program.DefaultLocation,
            EnrolledCount = summary?.EnrolledCount ?? 0,
            AttendancePct = summary?.AttendancePct,
            Participants = participants.Select(p => new ParticipantSummaryDto
            {
                Id = p.Id,
                FullName = p.FullName,
                Initials = p.Initials,
                Status = p.Status,
                ProgramId = p.ProgramId,
                ProgramName = program.Name,
                AttendancePct = pctMap.GetValueOrDefault(p.Id, 0),
                StartDate = p.StartDate.ToString("yyyy-MM-dd"),
                HasDocAlerts = DocumentAlerts.For(p),
            }).ToList(),
            UpcomingEvents = events.Select(e => new CalendarEventDto
            {
                Id = e.Id,
                Title = e.Title,
                Location = e.Location,
                Meta = e.Meta,
                Date = e.Date.ToString("yyyy-MM-dd"),
                TimeRange = e.TimeRange,
                ProgramId = e.ProgramId,
                ProgramName = program.Name,
                IsUpcoming = e.IsUpcoming,
            }).ToList(),
            Staff = staff
                .OrderBy(s => s.FullName)
                .Select(s => new StaffSummaryDto
                {
                    Id = s.Id,
                    FullName = s.FullName,
                    Initials = s.Initials,
                    Role = s.Role,
                    StartDate = s.StartDate.ToString("yyyy-MM-dd"),
                    OnboardingProgressPct = s.OnboardingProgressPct,
                    ProgramNames = new List<string> { program.Name },
                })
                .ToList(),
            Alerts = participants
                .Where(p => p.Status == Domain.Enums.ParticipantStatus.Attention)
                .Select(p => new ProgramAlertDto
                {
                    Severity = "Warning",
                    Message = $"{p.FullName} needs attention",
                    ParticipantId = p.Id,
                }).ToList(),
        };
    }

    public async Task<ProgramSummaryDto> CreateAsync(CreateProgramDto dto)
    {
        var program = new CrmProgram
        {
            Name = dto.Name,
            Slug = dto.Name.ToLower()
                        .Replace(" ", "-")
                        .Replace("'", "")
                        .Replace(".", ""),
            ColorHex = dto.ColorHex,
            SessionSchedule = dto.SessionSchedule,
            DefaultLocation = dto.DefaultLocation,
            MeetingDays = dto.MeetingDays,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
        };

        await _uow.Programs.AddAsync(program);
        await _uow.SaveChangesAsync();

        return new ProgramSummaryDto
        {
            Id = program.Id,
            Name = program.Name,
            Slug = program.Slug,
            ColorHex = program.ColorHex,
            SessionSchedule = program.SessionSchedule,
            DefaultLocation = program.DefaultLocation,
            MeetingDays = program.MeetingDays,
            StartTime = program.StartTime,
            EndTime = program.EndTime,
            EnrolledCount = 0,
            AlertCount = 0,
        };
    }

    public async Task<ProgramSummaryDto?> UpdateAsync(Guid id, UpdateProgramDto dto)
    {
        var program = await _uow.Programs.GetByIdAsync(id);
        if (program is null) return null;

        // Slug stays fixed (it backs routes/links); everything else is replaceable.
        program.Name = dto.Name.Trim();
        program.ColorHex = dto.ColorHex;
        program.SessionSchedule = dto.SessionSchedule;
        program.DefaultLocation = dto.DefaultLocation;
        program.MeetingDays = dto.MeetingDays;
        program.StartTime = dto.StartTime;
        program.EndTime = dto.EndTime;

        await _uow.Programs.UpdateAsync(program);
        await _uow.SaveChangesAsync();

        // Reuse the full summary computation (enrolled/attendance/next session).
        return await GetBySlugAsync(program.Slug);
    }

    public async Task<bool> AssignStaffAsync(Guid programId, Guid staffMemberId)
    {
        var program = await _uow.Programs.GetByIdAsync(programId);
        var staff = await _uow.Staff.GetByIdAsync(staffMemberId);
        if (program is null || staff is null) return false;

        var assignments = await _uow.GetStaffProgramAssignmentsAsync();
        if (assignments.Any(a => a.ProgramId == programId && a.StaffMemberId == staffMemberId))
            return true; // already assigned — idempotent

        await _uow.AddStaffProgramAssignmentAsync(new StaffProgramAssignment
        {
            ProgramId = programId,
            StaffMemberId = staffMemberId,
        });
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnassignStaffAsync(Guid programId, Guid staffMemberId)
    {
        var program = await _uow.Programs.GetByIdAsync(programId);
        var staff = await _uow.Staff.GetByIdAsync(staffMemberId);
        if (program is null || staff is null) return false;

        await _uow.RemoveStaffProgramAssignmentAsync(staffMemberId, programId);
        await _uow.SaveChangesAsync();
        return true;
    }
}
