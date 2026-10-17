using CRM.Application.DTOs.Planning;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Xunit;

namespace CRM.Tests;

/// <summary>
/// Per-Star Planning includes stars enrolled through their SECOND program, labelled as such
/// (Oct 2026: "Chris and Skyler only show on Pathways"). One plan per star per month, so a
/// dual-enrolled star is still one row, shown under the program the caller found them through.
/// </summary>
public class PlanningDualEnrollmentTests
{
    private static readonly Guid Pathways = Guid.NewGuid();
    private static readonly Guid PartTime = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly PlanningService _svc;
    private readonly Guid _adminUser;
    private readonly Guid _ptTeacherUser;
    private readonly StaffMember _pam = new() { FullName = "Pam PT", Initials = "PP", Role = StaffRole.Teacher };
    private readonly StaffMember _pathwaysTeacher = new() { FullName = "Pat Pathways", Initials = "PA", Role = StaffRole.Teacher };

    // Pathways primary, Part-time secondary — the Chris / Skyler shape.
    private readonly Participant _dual = new() { FullName = "Chris Quiwa", Initials = "CQ", ProgramId = Pathways, SecondaryProgramId = PartTime };
    private readonly Participant _pathwaysOnly = new() { FullName = "Pathways Only", Initials = "PO", ProgramId = Pathways };
    private readonly Participant _ptOnly = new() { FullName = "Part-time Only", Initials = "PT", ProgramId = PartTime };

    public PlanningDualEnrollmentTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = Pathways, Name = "Pathways: Manteca", Slug = "pathways:-manteca", Track = ProgramTrack.Pathways },
            new CrmProgram { Id = PartTime, Name = "Manteca: Part-Time", Slug = "manteca-pt", Track = ProgramTrack.PartTime },
        ]);
        _uow.StaffRepo.Items.AddRange([_pam, _pathwaysTeacher]);
        _uow.StaffProgramAssignments.Add(new StaffProgramAssignment { StaffMemberId = _pam.Id, ProgramId = PartTime });
        _uow.StaffProgramAssignments.Add(new StaffProgramAssignment { StaffMemberId = _pathwaysTeacher.Id, ProgramId = Pathways });

        var admin = new User { Email = "admin@x.org", FullName = "Admin", Role = UserRole.Admin };
        var pamUser = new User { Email = "pam@x.org", FullName = "Pam PT", Role = UserRole.Staff, StaffMemberId = _pam.Id };
        _uow.UsersRepo.Items.AddRange([admin, pamUser]);
        _adminUser = admin.Id;
        _ptTeacherUser = pamUser.Id;

        _uow.ParticipantsRepo.Items.AddRange([_dual, _pathwaysOnly, _ptOnly]);
        _svc = new PlanningService(_uow, new ProgramAccessService(_uow));
    }

    private void Roster(Participant star, Guid programId, StaffMember staff, int year = 2026, int quarter = 4) =>
        _uow.RosterAssignmentsRepo.Items.Add(new RosterAssignment
        {
            ParticipantId = star.Id, ProgramId = programId, Year = year, Quarter = quarter, AssignedStaffId = staff.Id,
        });

    private PerStarPlan Plan(Participant star, string monthKey, StaffMember? staff)
    {
        var plan = new PerStarPlan { ParticipantId = star.Id, MonthKey = monthKey, AssignedStaffId = staff?.Id };
        _uow.PerStarPlansRepo.Items.Add(plan);
        return plan;
    }

    private async Task<PerStarPlanDto> RowFor(Guid userId, Participant star, string monthKey = "2026-10", Guid? programId = null) =>
        Assert.Single(await _svc.GetPerStarPlansAsync(userId, monthKey, programId), r => r.ParticipantId == star.Id);

    [Fact]
    public async Task A_part_time_teacher_sees_a_pathways_primary_star_through_the_secondary_enrolment()
    {
        var rows = await _svc.GetPerStarPlansAsync(_ptTeacherUser, "2026-10", null);

        Assert.Equal(new[] { _dual.Id, _ptOnly.Id }.OrderBy(id => id), rows.Select(r => r.ParticipantId).OrderBy(id => id));
        var dual = Assert.Single(rows, r => r.ParticipantId == _dual.Id);
        Assert.Equal(PartTime, dual.ProgramId);
        Assert.Equal("Manteca: Part-Time", dual.ProgramName);
        Assert.Equal("manteca-pt", dual.ProgramSlug);
        Assert.True(dual.IsSecondaryEnrollment);
        Assert.False(Assert.Single(rows, r => r.ParticipantId == _ptOnly.Id).IsSecondaryEnrollment);
    }

    [Fact]
    public async Task Filtering_by_the_secondary_program_includes_the_dual_enrolled_star()
    {
        var rows = await _svc.GetPerStarPlansAsync(_adminUser, "2026-10", PartTime);

        Assert.DoesNotContain(rows, r => r.ParticipantId == _pathwaysOnly.Id);
        var dual = Assert.Single(rows, r => r.ParticipantId == _dual.Id);
        Assert.Equal(PartTime, dual.ProgramId);
        Assert.True(dual.IsSecondaryEnrollment);
        Assert.Contains(rows, r => r.ParticipantId == _ptOnly.Id);
    }

    [Fact]
    public async Task The_teacher_can_filter_by_their_program_and_still_see_the_star()
    {
        var rows = await _svc.GetPerStarPlansAsync(_ptTeacherUser, "2026-10", PartTime);

        Assert.Contains(rows, r => r.ParticipantId == _dual.Id && r.ProgramId == PartTime);
    }

    [Fact]
    public async Task Without_a_filter_a_dual_star_is_one_row_under_the_primary_when_the_caller_can_see_it()
    {
        var rows = await _svc.GetPerStarPlansAsync(_adminUser, "2026-10", null);

        Assert.Equal(3, rows.Count);
        var dual = Assert.Single(rows, r => r.ParticipantId == _dual.Id);
        Assert.Equal(Pathways, dual.ProgramId);
        Assert.False(dual.IsSecondaryEnrollment);
    }

    [Fact]
    public async Task The_roster_default_comes_from_the_enrolment_the_row_is_shown_under()
    {
        Roster(_dual, Pathways, _pathwaysTeacher);
        Roster(_dual, PartTime, _pam);

        var forPam = Assert.Single(await _svc.GetPerStarPlansAsync(_ptTeacherUser, "2026-10", null), r => r.ParticipantId == _dual.Id);
        var forAdmin = Assert.Single(await _svc.GetPerStarPlansAsync(_adminUser, "2026-10", null), r => r.ParticipantId == _dual.Id);

        Assert.Equal(_pam.Id, forPam.AssignedStaffId);
        Assert.Equal("Roster", forPam.AssignedStaffSource);
        Assert.Equal(_pathwaysTeacher.Id, forAdmin.AssignedStaffId);
    }

    [Fact]
    public async Task The_other_enrolment_s_roster_row_is_not_used()
    {
        Roster(_dual, PartTime, _pam);
        Roster(_dual, PartTime, _pam, quarter: 3);

        var underPathways = Assert.Single(await _svc.GetPerStarPlansAsync(_adminUser, "2026-10", Pathways), r => r.ParticipantId == _dual.Id);

        Assert.Null(underPathways.AssignedStaffId);
        Assert.Null(underPathways.AssignedStaffSource);
    }

    [Fact]
    public async Task A_save_by_the_part_time_teacher_comes_back_under_part_time()
    {
        var saved = await _svc.UpsertPerStarPlanAsync(_ptTeacherUser, new UpsertPerStarPlanDto
        {
            ParticipantId = _dual.Id, MonthKey = "2026-10", AssignedStaffId = _pam.Id, PrimaryTier = ProgressLevel.Novice,
        });

        Assert.Equal(PartTime, saved!.ProgramId);
        Assert.True(saved.IsSecondaryEnrollment);
        Assert.Equal("Plan", saved.AssignedStaffSource);
    }

    // ── Assigned staff for a dual-enrolled star's row (Oct 2026) ───────────────
    // The plan is one per star, but each enrolment has its own teacher, so a dual star's row
    // only uses a plan name that is assigned to the row's program.

    [Fact]
    public async Task A_dual_star_s_part_time_row_ignores_this_month_s_plan_naming_a_pathways_only_teacher()
    {
        Plan(_dual, "2026-10", _pathwaysTeacher);
        Roster(_dual, PartTime, _pam);

        var forPam = await RowFor(_ptTeacherUser, _dual);
        var underPathways = await RowFor(_adminUser, _dual);

        Assert.Equal(_pam.Id, forPam.AssignedStaffId);
        Assert.Equal("Roster", forPam.AssignedStaffSource);
        Assert.Equal(_pathwaysTeacher.Id, underPathways.AssignedStaffId);
        Assert.Equal("Plan", underPathways.AssignedStaffSource);
    }

    [Fact]
    public async Task A_dual_star_s_part_time_row_ignores_a_previous_plan_naming_a_pathways_only_teacher()
    {
        // September's plan was saved by the Pathways teacher; nothing is set for Q4 yet.
        Plan(_dual, "2026-09", _pathwaysTeacher);
        Roster(_dual, PartTime, _pam, quarter: 3);

        var forPam = await RowFor(_ptTeacherUser, _dual);
        var underPathways = await RowFor(_adminUser, _dual);

        Assert.Equal(_pam.Id, forPam.AssignedStaffId);
        Assert.Equal("PreviousRoster", forPam.AssignedStaffSource);
        Assert.Equal("Q3 2026", forPam.AssignedStaffSourceTerm);
        Assert.Equal(_pathwaysTeacher.Id, underPathways.AssignedStaffId);
        Assert.Equal("PreviousPlan", underPathways.AssignedStaffSource);
    }

    [Fact]
    public async Task A_single_program_star_s_plan_applies_whoever_it_names()
    {
        // Pam isn't assigned to Pathways, but a single-program star's plan is the star's own.
        Plan(_pathwaysOnly, "2026-10", _pam);
        Roster(_pathwaysOnly, Pathways, _pathwaysTeacher);

        var row = await RowFor(_adminUser, _pathwaysOnly);

        Assert.Equal(_pam.Id, row.AssignedStaffId);
        Assert.Equal("Plan", row.AssignedStaffSource);
    }

    [Fact]
    public async Task A_save_made_under_the_secondary_program_comes_back_resolved_for_it()
    {
        Roster(_dual, Pathways, _pathwaysTeacher);
        Roster(_dual, PartTime, _pam);

        var saved = await _svc.UpsertPerStarPlanAsync(_adminUser, new UpsertPerStarPlanDto
        {
            ParticipantId = _dual.Id, MonthKey = "2026-10", ProgramId = PartTime, AssignedStaffId = null, PrimaryTier = ProgressLevel.Novice,
        });

        Assert.Equal(PartTime, saved!.ProgramId);
        Assert.Equal("Manteca: Part-Time", saved.ProgramName);
        Assert.True(saved.IsSecondaryEnrollment);
        Assert.Equal(_pam.Id, saved.AssignedStaffId);
        Assert.Equal("Roster", saved.AssignedStaffSource);
        Assert.Equal("Q4 2026", saved.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task A_save_for_a_program_the_star_isn_t_enrolled_in_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _svc.UpsertPerStarPlanAsync(_adminUser, new UpsertPerStarPlanDto
        {
            ParticipantId = _pathwaysOnly.Id, MonthKey = "2026-10", ProgramId = PartTime, PrimaryTier = ProgressLevel.Novice,
        }));

        Assert.Contains("isn't enrolled", ex.Message);
        Assert.Empty(_uow.PerStarPlansRepo.Items);
    }

    [Fact]
    public async Task A_save_for_a_program_the_caller_can_t_access_is_forbidden()
    {
        // Pam can see Chris through Part-time, but may not save from a Pathways row.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.UpsertPerStarPlanAsync(_ptTeacherUser, new UpsertPerStarPlanDto
        {
            ParticipantId = _dual.Id, MonthKey = "2026-10", ProgramId = Pathways, PrimaryTier = ProgressLevel.Novice,
        }));

        Assert.Empty(_uow.PerStarPlansRepo.Items);
    }

    [Fact]
    public async Task A_tier_save_from_the_part_time_row_keeps_the_pathways_teacher_named_in_the_plan()
    {
        // Pam's row never showed Pat, so the "none" her page sends back must not wipe him.
        var plan = Plan(_dual, "2026-10", _pathwaysTeacher);

        var saved = await _svc.UpsertPerStarPlanAsync(_ptTeacherUser, new UpsertPerStarPlanDto
        {
            ParticipantId = _dual.Id, MonthKey = "2026-10", ProgramId = PartTime, AssignedStaffId = null, PrimaryTier = ProgressLevel.Intermediate,
        });

        Assert.Equal(_pathwaysTeacher.Id, plan.AssignedStaffId);
        Assert.Equal(ProgressLevel.Intermediate, plan.PrimaryTier);
        Assert.Null(saved!.AssignedStaffId);
        Assert.Equal("Plan", (await RowFor(_adminUser, _dual)).AssignedStaffSource);
    }

    [Fact]
    public async Task Clearing_the_plan_s_teacher_from_the_row_that_shows_it_still_clears_it()
    {
        var plan = Plan(_dual, "2026-10", _pathwaysTeacher);

        await _svc.UpsertPerStarPlanAsync(_adminUser, new UpsertPerStarPlanDto
        {
            ParticipantId = _dual.Id, MonthKey = "2026-10", ProgramId = Pathways, AssignedStaffId = null, PrimaryTier = ProgressLevel.Novice,
        });

        Assert.Null(plan.AssignedStaffId);
    }

    [Fact]
    public async Task Picking_a_teacher_not_assigned_to_the_row_s_program_is_refused_with_a_reason()
    {
        // The Part-time row could never show Pat, so storing him would look like nothing happened.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _svc.UpsertPerStarPlanAsync(_adminUser, new UpsertPerStarPlanDto
        {
            ParticipantId = _dual.Id, MonthKey = "2026-10", ProgramId = PartTime, AssignedStaffId = _pathwaysTeacher.Id, PrimaryTier = ProgressLevel.Novice,
        }));

        Assert.Contains("Pat Pathways", ex.Message);
        Assert.Contains("isn't assigned to", ex.Message);
        Assert.Empty(_uow.PerStarPlansRepo.Items);
    }

    [Fact]
    public async Task Rows_say_whether_the_star_is_dual_enrolled()
    {
        Assert.True((await RowFor(_adminUser, _dual)).IsDualEnrolled);
        Assert.False((await RowFor(_adminUser, _pathwaysOnly)).IsDualEnrolled);
    }

    [Fact]
    public async Task A_star_in_neither_of_the_teacher_s_programs_stays_hidden()
    {
        var rows = await _svc.GetPerStarPlansAsync(_ptTeacherUser, "2026-10", null);

        Assert.DoesNotContain(rows, r => r.ParticipantId == _pathwaysOnly.Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _svc.GetPerStarPlansAsync(_ptTeacherUser, "2026-10", Pathways));
    }
}
