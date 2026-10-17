using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// The program page's "Upcoming events" (Oct 2026: past events kept showing) and
/// <c>/api/programs/mine</c>, which must give a login exactly the programs it can open.
/// </summary>
public class ProgramUpcomingAndMineTests
{
    private readonly FakeUnitOfWork _uow = new();
    private static readonly DateTime Today = new(2026, 10, 16);

    private static readonly Guid ProgramA = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid ProgramB = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b2");

    public ProgramUpcomingAndMineTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = ProgramA, Name = "Program A", Slug = "program-a" },
            new CrmProgram { Id = ProgramB, Name = "Program B", Slug = "program-b" },
        ]);
    }

    private ProgramService Programs() =>
        new(_uow, new FakeStatsQueries(), new FakeOrgClock(Today), new ProgramAccessService(_uow));

    private async Task<Guid> Login(UserRole role = UserRole.Staff, Guid? program = null, DateTime? staffEndDate = null)
    {
        Guid? staffId = null;
        if (program is { } pid)
        {
            var staff = new StaffMember { FullName = "T", Initials = "T", Role = StaffRole.Teacher, EndDate = staffEndDate };
            await _uow.Staff.AddAsync(staff);
            _uow.StaffProgramAssignments.Add(new StaffProgramAssignment { StaffMemberId = staff.Id, ProgramId = pid });
            staffId = staff.Id;
        }
        var user = new User { Email = $"{Guid.NewGuid():N}@x.org", Role = role, StaffMemberId = staffId };
        await _uow.Users.AddAsync(user);
        return user.Id;
    }

    private async Task<CalendarEvent> Event(string title, DateTime date, bool storedIsUpcoming, Guid? program = null)
    {
        var e = new CalendarEvent { Title = title, Date = date, IsUpcoming = storedIsUpcoming, ProgramId = program ?? ProgramA };
        await _uow.CalendarEvents.AddAsync(e);
        return e;
    }

    // ── Upcoming events (1.2) ───────────────────────────────────────────────────

    [Fact]
    public async Task Upcoming_events_start_today_on_the_org_clock_not_the_stored_flag()
    {
        var admin = await Login(UserRole.Admin);
        // The stored flag was set when the event was created and never recomputed.
        await Event("Yesterday's showcase", Today.AddDays(-1), storedIsUpcoming: true);
        var today = await Event("Today's rehearsal", Today.AddHours(15), storedIsUpcoming: false);

        var detail = await Programs().GetDetailAsync(admin, "program-a");

        var shown = Assert.Single(detail!.UpcomingEvents);
        Assert.Equal(today.Id, shown.Id);
        Assert.True(shown.IsUpcoming);
    }

    [Fact]
    public async Task Upcoming_events_are_this_programs_soonest_five()
    {
        var admin = await Login(UserRole.Admin);
        for (var i = 6; i >= 1; i--) await Event($"Day +{i}", Today.AddDays(i), storedIsUpcoming: true);
        await Event("Other program", Today, storedIsUpcoming: true, program: ProgramB);

        var detail = await Programs().GetDetailAsync(admin, "program-a");

        Assert.Equal(
            new[] { "Day +1", "Day +2", "Day +3", "Day +4", "Day +5" },
            detail!.UpcomingEvents.Select(e => e.Title));
    }

    // ── /api/programs/mine (1.11) ───────────────────────────────────────────────

    [Fact]
    public async Task My_programs_are_empty_for_a_login_linked_to_a_former_staff_record()
    {
        var former = await Login(program: ProgramA, staffEndDate: new DateTime(2026, 9, 30));

        Assert.Empty(await Programs().GetForUserAsync(former));
    }

    [Fact]
    public async Task My_programs_are_exactly_an_active_teachers_assigned_programs()
    {
        var teacher = await Login(program: ProgramB);

        var mine = await Programs().GetForUserAsync(teacher);

        Assert.Equal(new[] { ProgramB }, mine.Select(p => p.Id));
    }

    [Fact]
    public async Task My_programs_are_every_program_for_an_admin()
    {
        var admin = await Login(UserRole.Admin);

        var mine = await Programs().GetForUserAsync(admin);

        Assert.Equal(2, mine.Count);
    }

    [Fact]
    public async Task My_programs_are_empty_for_a_deactivated_login()
    {
        var teacher = await Login(program: ProgramA);
        _uow.UsersRepo.Items.Single(u => u.Id == teacher).IsActive = false;

        Assert.Empty(await Programs().GetForUserAsync(teacher));
    }
}
