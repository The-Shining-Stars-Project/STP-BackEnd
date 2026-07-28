using CRM.Application.DTOs.Participants;
using CRM.Application.DTOs.Planning;
using CRM.Application.DTOs.Progress;
using CRM.Application.DTOs.Roster;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// The regression suite for #1 — a teacher assigned to one program must not be able to read
/// or write another program's children, by any route. Every test drives the real services
/// against a real (SQLite in-memory) database, so it fails if the scoping is removed from
/// either the service or the shared <see cref="IProgramAccessService"/>.
/// </summary>
public class ProgramScopingTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly ProgramAccessService _access;

    // Two programs, a teacher on each, an admin, and one child enrolled in each program.
    private static readonly Guid ProgramA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ProgramB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid StaffA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid StaffB = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid TeacherAUser = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid TeacherBUser = Guid.Parse("44444444-0000-0000-0000-000000000002");
    private static readonly Guid AdminUser = Guid.Parse("55555555-0000-0000-0000-000000000003");
    private static readonly Guid UnlinkedUser = Guid.Parse("66666666-0000-0000-0000-000000000004");
    private static readonly Guid ChildInA = Guid.Parse("77777777-0000-0000-0000-000000000001");
    private static readonly Guid ChildInB = Guid.Parse("88888888-0000-0000-0000-000000000002");

    public ProgramScopingTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = ProgramA, Name = "Program A", Slug = "program-a", ColorHex = "#111111" },
            new CrmProgram { Id = ProgramB, Name = "Program B", Slug = "program-b", ColorHex = "#222222" },
        ]);

        _uow.StaffRepo.Items.AddRange([
            new StaffMember { Id = StaffA, FullName = "Teacher A", Initials = "TA", Role = StaffRole.Teacher },
            new StaffMember { Id = StaffB, FullName = "Teacher B", Initials = "TB", Role = StaffRole.Teacher },
        ]);

        _uow.StaffProgramAssignments.AddRange([
            new StaffProgramAssignment { StaffMemberId = StaffA, ProgramId = ProgramA },
            new StaffProgramAssignment { StaffMemberId = StaffB, ProgramId = ProgramB },
        ]);

        _uow.UsersRepo.Items.AddRange([
            new User { Id = TeacherAUser, Email = "a@x.org", FullName = "Teacher A", Role = UserRole.Staff, StaffMemberId = StaffA },
            new User { Id = TeacherBUser, Email = "b@x.org", FullName = "Teacher B", Role = UserRole.Staff, StaffMemberId = StaffB },
            new User { Id = AdminUser, Email = "admin@x.org", FullName = "Admin", Role = UserRole.Admin },
            // A staff account with no linked staff record — must resolve to "no programs",
            // not "unfiltered".
            new User { Id = UnlinkedUser, Email = "nobody@x.org", FullName = "Unlinked", Role = UserRole.Staff },
        ]);

        _uow.ParticipantsRepo.Items.AddRange([
            new Participant { Id = ChildInA, FullName = "Child In A", Initials = "CA", ProgramId = ProgramA },
            new Participant { Id = ChildInB, FullName = "Child In B", Initials = "CB", ProgramId = ProgramB },
        ]);

        _access = new ProgramAccessService(_uow);
    }

    private ParticipantService Participants() => new(_uow, new FakeStatsQueries(), _access);
    private ProgressTrackingService Progress() => new(_uow, _access);
    private ArtsProfileService ArtsProfile() => new(_uow, _access);
    private RosterService Roster() => new(_uow, _access);
    private PlanningService Planning() => new(_uow, _access);

    // ── The scope primitive ─────────────────────────────────────────────────────

    [Fact]
    public async Task Teacher_scope_contains_only_their_assigned_program()
    {
        var scope = await _access.ForUserAsync(TeacherAUser);

        Assert.False(scope.IsAdmin);
        Assert.True(scope.CanAccess(ProgramA));
        Assert.False(scope.CanAccess(ProgramB));
    }

    [Fact]
    public async Task Admin_scope_covers_every_program()
    {
        var scope = await _access.ForUserAsync(AdminUser);

        Assert.True(scope.IsAdmin);
        Assert.True(scope.CanAccess(ProgramA));
        Assert.True(scope.CanAccess(ProgramB));
    }

    [Fact]
    public async Task User_with_no_staff_link_has_an_empty_scope()
    {
        var scope = await _access.ForUserAsync(UnlinkedUser);

        Assert.False(scope.CanAccess(ProgramA));
        Assert.False(scope.CanAccess(ProgramB));
    }

    [Fact]
    public async Task Unknown_user_id_has_an_empty_scope()
    {
        var scope = await _access.ForUserAsync(Guid.NewGuid());

        Assert.False(scope.CanAccess(ProgramA));
        Assert.False(scope.CanAccess(ProgramB));
    }

    // ── Participants ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Participant_list_is_filtered_to_the_callers_programs()
    {
        var forTeacherA = await Participants().GetAllAsync(TeacherAUser);

        Assert.Equal(new[] { ChildInA }, forTeacherA.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task Participant_list_shows_every_child_to_an_admin()
    {
        var forAdmin = await Participants().GetAllAsync(AdminUser);

        Assert.Equal(2, forAdmin.Count);
    }

    [Fact]
    public async Task Participant_list_is_empty_for_an_unlinked_account()
    {
        var forUnlinked = await Participants().GetAllAsync(UnlinkedUser);

        Assert.Empty(forUnlinked);
    }

    [Fact]
    public async Task Reading_another_programs_child_by_id_is_forbidden()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Participants().GetByIdAsync(TeacherAUser, ChildInB));
    }

    [Fact]
    public async Task Reading_your_own_programs_child_is_allowed()
    {
        var child = await Participants().GetByIdAsync(TeacherAUser, ChildInA);

        Assert.NotNull(child);
        Assert.Equal(ChildInA, child!.Id);
    }

    [Fact]
    public async Task A_missing_participant_is_not_found_rather_than_forbidden()
    {
        var child = await Participants().GetByIdAsync(AdminUser, Guid.NewGuid());

        Assert.Null(child);
    }

    [Fact]
    public async Task Enrolling_into_another_programs_roster_is_forbidden()
    {
        var dto = new CreateParticipantDto { FullName = "New Child", Initials = "NC", ProgramId = ProgramB };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Participants().CreateAsync(TeacherAUser, dto));
    }

    [Fact]
    public async Task Editing_another_programs_child_is_forbidden()
    {
        var dto = new UpdateParticipantDto { FullName = "Renamed" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Participants().UpdateAsync(TeacherAUser, ChildInB, dto));
    }

    [Fact]
    public async Task Moving_your_own_child_into_a_program_you_dont_run_is_forbidden()
    {
        var dto = new UpdateParticipantDto { ProgramId = ProgramB };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Participants().UpdateAsync(TeacherAUser, ChildInA, dto));
    }

    [Fact]
    public async Task Deleting_another_programs_child_is_forbidden()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Participants().DeleteAsync(TeacherAUser, ChildInB));
    }

    // ── Progress tracking — the write paths that were fully open ────────────────

    [Fact]
    public async Task Reading_another_programs_progress_record_is_forbidden()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().GetStarMonthAsync(TeacherAUser, ChildInB, "2026-07"));
    }

    [Fact]
    public async Task Reading_your_own_programs_progress_record_is_allowed()
    {
        var month = await Progress().GetStarMonthAsync(TeacherAUser, ChildInA, "2026-07");

        Assert.NotNull(month);
        Assert.Equal(ChildInA, month!.ParticipantId);
    }

    [Fact]
    public async Task Scoring_another_programs_child_is_forbidden()
    {
        var dto = new RecordWeeklyScoreDto
        {
            ParticipantId = ChildInB,
            SubSkillId = Guid.NewGuid(),
            MonthKey = "2026-07",
            WeekNumber = 1,
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().RecordWeeklyScoreAsync(TeacherAUser, dto));
    }

    [Fact]
    public async Task Confirming_another_programs_month_end_level_is_forbidden()
    {
        var dto = new ConfirmMonthEndDto { SubSkillId = Guid.NewGuid(), Level = ProgressLevel.Intermediate };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().ConfirmMonthEndAsync(TeacherAUser, ChildInB, "2026-07", dto));
    }

    [Fact]
    public async Task Computing_another_programs_month_end_is_forbidden()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().ComputeMonthEndAsync(TeacherAUser, ChildInB, "2026-07"));
    }

    [Fact]
    public async Task Writing_a_note_on_another_programs_child_is_forbidden()
    {
        var dto = new UpsertNoteSelectionDto { WeekNumber = 1, Kind = GoalBankKind.Strength, CustomText = "x" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().UpsertNoteSelectionAsync(TeacherAUser, ChildInB, "2026-07", dto));
    }

    [Fact]
    public async Task Writing_a_monthly_summary_for_another_programs_child_is_forbidden()
    {
        var dto = new UpsertMonthlySummaryDto { ProgressNarrative = "x" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().UpsertMonthlySummaryAsync(TeacherAUser, ChildInB, "2026-07", dto));
    }

    [Fact]
    public async Task Reading_another_programs_focus_skills_is_forbidden()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().GetFocusSkillsAsync(TeacherAUser, ProgramB, "2026-07"));
    }

    [Fact]
    public async Task Setting_another_programs_focus_skills_is_forbidden()
    {
        var dto = new SetFocusSkillsDto { ProgramId = ProgramB, MonthKey = "2026-07", WeekNumber = 1 };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Progress().SetFocusSkillsAsync(TeacherAUser, dto));
    }

    // ── Student Frame, roster, planning ─────────────────────────────────────────

    [Fact]
    public async Task Reading_another_programs_student_frame_is_forbidden()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ArtsProfile().GetAsync(TeacherAUser, ChildInB));
    }

    [Fact]
    public async Task Writing_another_programs_student_frame_is_forbidden()
    {
        var dto = new UpsertArtsProfileDto { IppSummary = "x" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ArtsProfile().UpsertAsync(TeacherAUser, ChildInB, dto));
    }

    [Fact]
    public async Task Management_roster_is_filtered_to_the_callers_programs()
    {
        var forTeacherA = await Roster().GetRosterAsync(TeacherAUser, 2026, 3, null);

        Assert.Equal(new[] { ChildInA }, forTeacherA.Select(e => e.ParticipantId).ToArray());
    }

    [Fact]
    public async Task Management_roster_shows_every_child_to_an_admin()
    {
        var forAdmin = await Roster().GetRosterAsync(AdminUser, 2026, 3, null);

        Assert.Equal(2, forAdmin.Count);
    }

    [Fact]
    public async Task Assigning_another_programs_child_to_a_roster_slot_is_forbidden()
    {
        var dto = new UpsertRosterAssignmentDto { ParticipantId = ChildInB, Year = 2026, Quarter = 3 };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Roster().UpsertAssignmentAsync(TeacherAUser, dto));
    }

    [Fact]
    public async Task Per_star_plans_are_filtered_to_the_callers_programs()
    {
        var forTeacherA = await Planning().GetPerStarPlansAsync(TeacherAUser, "2026-07", null);

        Assert.Equal(new[] { ChildInA }, forTeacherA.Select(e => e.ParticipantId).ToArray());
    }

    [Fact]
    public async Task Requesting_another_programs_plans_by_id_is_forbidden()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Planning().GetPerStarPlansAsync(TeacherAUser, "2026-07", ProgramB));
    }

    [Fact]
    public async Task Writing_a_plan_for_another_programs_child_is_forbidden()
    {
        var dto = new UpsertPerStarPlanDto { ParticipantId = ChildInB, MonthKey = "2026-07" };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => Planning().UpsertPerStarPlanAsync(TeacherAUser, dto));
    }
}
