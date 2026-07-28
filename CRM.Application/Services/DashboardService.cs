using CRM.Application.DTOs.Dashboard;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;

namespace CRM.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly IParticipantService _participants;
    private readonly IAttendanceService _attendance;
    private readonly ITaskService _tasks;
    private readonly IStaffService _staff;
    private readonly IProgramService _programs;
    private readonly ICalendarService _calendar;
    private readonly IOrgClock _clock;

    public DashboardService(
        IParticipantService participants,
        IAttendanceService attendance,
        ITaskService tasks,
        IStaffService staff,
        IProgramService programs,
        ICalendarService calendar,
        IOrgClock clock)
    {
        _clock = clock;
        _participants = participants;
        _attendance = attendance;
        _tasks = tasks;
        _staff = staff;
        _programs = programs;
        _calendar = calendar;
    }

    public async Task<DashboardDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        // Runs sequentially on a single DbContext (EF contexts aren't concurrent), but
        // collapses seven HTTP round-trips into one and uses the read-only roster so the
        // dashboard never triggers the attendance lazy-create write path.
        //
        // The participant list and today's roster are program-scoped (#1): composing
        // several endpoints into one payload must not widen what any of them return.
        // "This month" is a local-calendar question (#7): on the 1st, UTC and Pacific
        // disagree about which month it is for the first several hours of the day.
        var now = _clock.Today;
        var next = now.AddMonths(1);

        var participants = await _participants.GetAllAsync(userId, ct);
        var roster = await _attendance.GetTodayRosterReadOnlyAsync(userId, ct);
        var projects = await _tasks.GetProjectsAsync(ct);
        var staff = await _staff.GetAllAsync(ct);
        var programs = await _programs.GetAllAsync(ct);
        var thisMonth = await _calendar.GetEventsAsync(now.Month, now.Year, ct);
        var nextMonth = await _calendar.GetEventsAsync(next.Month, next.Year, ct);

        return new DashboardDto
        {
            Participants = participants.ToList(),
            TodayRoster = roster.ToList(),
            Projects = projects.ToList(),
            Staff = staff.ToList(),
            Programs = programs.ToList(),
            Events = thisMonth.Concat(nextMonth).ToList(),
        };
    }
}
