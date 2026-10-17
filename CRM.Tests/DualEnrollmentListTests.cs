using CRM.Application.DTOs.Participants;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Dual enrolment in lists (Oct 2026: "Chris and Skyler only show on Pathways"). A star's
/// SECOND program counts wherever a list or page decides who is in a program, and the main
/// and second program can't be the same one. Real services over in-memory repositories,
/// scoped through the real <see cref="ProgramAccessService"/>.
/// </summary>
public class DualEnrollmentListTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ProgramAccessService _access;
    private static readonly DateTime Today = new(2026, 10, 16);

    private static readonly Guid PartTime = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Pathways = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid Other = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    private static readonly Guid PtTeacherUser = Guid.Parse("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid OtherTeacherUser = Guid.Parse("22222222-0000-0000-0000-00000000000c");
    private static readonly Guid AdminUser = Guid.Parse("33333333-0000-0000-0000-000000000001");

    /// <summary>Main program Pathways, second program Part-time.</summary>
    private static readonly Guid DualStar = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly Guid PathwaysOnlyStar = Guid.Parse("55555555-0000-0000-0000-000000000002");
    private static readonly Guid PartTimeOnlyStar = Guid.Parse("66666666-0000-0000-0000-000000000003");

    public DualEnrollmentListTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = PartTime, Name = "MJC: Part-Time", Slug = "mjc-pt", Track = ProgramTrack.PartTime },
            new CrmProgram { Id = Pathways, Name = "Pathways", Slug = "pathways", Track = ProgramTrack.Pathways },
            new CrmProgram { Id = Other, Name = "Manteca PT", Slug = "manteca-pt", Track = ProgramTrack.PartTime },
        ]);

        var ptStaff = new StaffMember { FullName = "Pam", Initials = "PO", Role = StaffRole.Teacher };
        var otherStaff = new StaffMember { FullName = "Olive", Initials = "OL", Role = StaffRole.Teacher };
        _uow.StaffRepo.Items.AddRange([ptStaff, otherStaff]);
        _uow.StaffProgramAssignments.AddRange([
            new StaffProgramAssignment { StaffMemberId = ptStaff.Id, ProgramId = PartTime },
            new StaffProgramAssignment { StaffMemberId = otherStaff.Id, ProgramId = Other },
        ]);

        _uow.UsersRepo.Items.AddRange([
            new User { Id = PtTeacherUser, Email = "pam@x.org", Role = UserRole.Staff, StaffMemberId = ptStaff.Id },
            new User { Id = OtherTeacherUser, Email = "olive@x.org", Role = UserRole.Staff, StaffMemberId = otherStaff.Id },
            new User { Id = AdminUser, Email = "admin@x.org", Role = UserRole.Admin },
        ]);

        _uow.ParticipantsRepo.Items.AddRange([
            new Participant { Id = DualStar, FullName = "Chris Dual", Initials = "CD", ProgramId = Pathways, SecondaryProgramId = PartTime },
            new Participant { Id = PathwaysOnlyStar, FullName = "Pat Pathways", Initials = "PP", ProgramId = Pathways },
            new Participant { Id = PartTimeOnlyStar, FullName = "Tia PartTime", Initials = "TP", ProgramId = PartTime },
        ]);

        _access = new ProgramAccessService(_uow);
    }

    private ParticipantService Participants() => new(_uow, new FakeStatsQueries(), _access, new FakeOrgClock(Today));
    private ProgramService Programs() => new(_uow, new FakeStatsQueries(), new FakeOrgClock(Today), _access);
    private AttendanceService Attendance() => new(_uow, _access, new FakeOrgClock(Today));

    private Participant Star(Guid id) => _uow.ParticipantsRepo.Items.Single(p => p.Id == id);

    // ── Stars list (also the Dashboard, Weekly Data and Enroll-star picker) ─────

    [Fact]
    public async Task Star_list_includes_a_star_whose_second_program_is_the_teachers()
    {
        var forPam = await Participants().GetAllAsync(PtTeacherUser);

        Assert.Equal(
            new[] { DualStar, PartTimeOnlyStar }.OrderBy(id => id),
            forPam.Select(p => p.Id).OrderBy(id => id));
        // Still described by their main program; the page labels the second one.
        var dual = forPam.Single(p => p.Id == DualStar);
        Assert.Equal(Pathways, dual.ProgramId);
        Assert.Equal(PartTime, dual.SecondaryProgramId);
    }

    [Fact]
    public async Task Star_list_leaves_out_a_dual_star_when_the_teacher_runs_neither_program()
    {
        var forOlive = await Participants().GetAllAsync(OtherTeacherUser);

        Assert.DoesNotContain(forOlive, p => p.Id == DualStar);
    }

    // ── Main and second program must differ ─────────────────────────────────────

    [Fact]
    public async Task Creating_a_star_with_the_same_main_and_second_program_is_refused()
    {
        var dto = new CreateParticipantDto
        {
            FullName = "Sam Same",
            Initials = "SS",
            ProgramId = PartTime,
            SecondaryProgramId = PartTime,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => Participants().CreateAsync(AdminUser, dto));
        Assert.DoesNotContain(_uow.ParticipantsRepo.Items, p => p.FullName == "Sam Same");
    }

    [Fact]
    public async Task Moving_the_main_program_onto_the_second_program_is_refused()
    {
        // What "Enroll star" used to send for a star already in this program as their second.
        var dto = new UpdateParticipantDto { ProgramId = PartTime };

        await Assert.ThrowsAsync<ArgumentException>(() => Participants().UpdateAsync(AdminUser, DualStar, dto));

        // Nothing was applied: Pathways is still their main program.
        Assert.Equal(Pathways, Star(DualStar).ProgramId);
        Assert.Equal(PartTime, Star(DualStar).SecondaryProgramId);
    }

    [Fact]
    public async Task Setting_the_second_program_to_the_main_program_is_refused()
    {
        var dto = new UpdateParticipantDto { SecondaryProgramId = Pathways, FullName = "Renamed" };

        await Assert.ThrowsAsync<ArgumentException>(() => Participants().UpdateAsync(AdminUser, PathwaysOnlyStar, dto));

        Assert.Null(Star(PathwaysOnlyStar).SecondaryProgramId);
        Assert.Equal("Pat Pathways", Star(PathwaysOnlyStar).FullName);
    }

    [Fact]
    public async Task Making_the_second_program_the_main_one_while_clearing_the_second_is_allowed()
    {
        var dto = new UpdateParticipantDto { ProgramId = PartTime, ClearSecondaryProgram = true };

        var updated = await Participants().UpdateAsync(AdminUser, DualStar, dto);

        Assert.NotNull(updated);
        Assert.Equal(PartTime, updated!.ProgramId);
        Assert.Null(updated.SecondaryProgramId);
    }

    [Fact]
    public async Task A_star_already_saved_with_the_same_program_twice_can_still_be_edited()
    {
        // Rows the old "Enroll star" left behind. The guard stops new ones; it must not lock
        // the existing ones out of an unrelated edit like a status change.
        var damaged = new Participant { FullName = "Old Row", Initials = "OR", ProgramId = PartTime, SecondaryProgramId = PartTime };
        _uow.ParticipantsRepo.Items.Add(damaged);

        var updated = await Participants().UpdateAsync(AdminUser, damaged.Id,
            new UpdateParticipantDto { Status = ParticipantStatus.Attention });

        Assert.Equal(ParticipantStatus.Attention, updated!.Status);
    }

    // ── Program page ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Program_page_lists_stars_whose_second_program_it_is()
    {
        var detail = await Programs().GetDetailAsync(PtTeacherUser, "mjc-pt");

        Assert.NotNull(detail);
        Assert.Equal(
            new[] { DualStar, PartTimeOnlyStar }.OrderBy(id => id),
            detail!.Participants.Select(p => p.Id).OrderBy(id => id));

        // The page tells a second-program star apart by ProgramId != this program.
        var dual = detail.Participants.Single(p => p.Id == DualStar);
        Assert.Equal(Pathways, dual.ProgramId);
        Assert.Equal("Pathways", dual.ProgramName);
        Assert.Equal(PartTime, dual.SecondaryProgramId);
    }

    [Fact]
    public async Task Program_page_enrolled_count_stays_main_program_only()
    {
        // Client decision pending (plan D4): no double counting yet.
        var detail = await Programs().GetDetailAsync(AdminUser, "mjc-pt");

        Assert.Equal(1, detail!.EnrolledCount);
    }

    // ── Today's attendance (dashboard widget) ───────────────────────────────────

    [Fact]
    public async Task Todays_roster_labels_a_dual_star_by_the_sessions_program()
    {
        var session = new Session { ProgramId = PartTime, Date = Today };
        await _uow.Sessions.AddAsync(session);
        _uow.AttendanceRepo.Items.Add(new AttendanceRecord { SessionId = session.Id, ParticipantId = DualStar });

        var entries = await Attendance().GetTodayRosterReadOnlyAsync(PtTeacherUser);

        var entry = Assert.Single(entries);
        Assert.Equal(DualStar, entry.ParticipantId);
        Assert.Equal(PartTime, entry.ProgramId);
        Assert.Equal("MJC: Part-Time", entry.ProgramName);
        Assert.Equal("mjc-pt", entry.ProgramSlug);
    }
}
