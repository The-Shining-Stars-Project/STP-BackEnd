using CRM.Application.DTOs.Planning;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Per-Star Planning defaults "Assigned staff" to the quarter's roster assignment, and says
/// where the name came from — teachers reported roster assignments "not populating" (Sep 2026).
/// When the month and quarter are both empty (the Oct 1 rollover), the latest EARLIER plan or
/// roster row is shown instead, labelled with its term (Oct 2026).
/// </summary>
public class PlanningAssignmentTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly PlanningService _svc;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly CrmProgram _program = new() { Name = "MJC", Slug = "mjc" };
    private readonly Participant _star;
    private readonly StaffMember _teacher = new() { FullName = "Teacher One", Initials = "TO" };
    private readonly StaffMember _other = new() { FullName = "Teacher Two", Initials = "TT" };
    private readonly StaffMember _former = new() { FullName = "Gone Teacher", Initials = "GT", EndDate = new DateTime(2026, 9, 30) };

    private sealed class Admin : IProgramAccessService
    {
        private readonly FakeUnitOfWork _uow;
        public Admin(FakeUnitOfWork uow) => _uow = uow;
        public Task<ProgramAccess> ForUserAsync(Guid userId) => Task.FromResult(new ProgramAccess(true, new HashSet<Guid>()));
        public Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId) =>
            Task.FromResult(_uow.ParticipantsRepo.Items.FirstOrDefault(p => p.Id == participantId));
    }

    public PlanningAssignmentTests()
    {
        _uow.ProgramsRepo.Items.Add(_program);
        _star = new Participant { FullName = "Star A", Initials = "SA", ProgramId = _program.Id };
        _uow.ParticipantsRepo.Items.Add(_star);
        _uow.StaffRepo.Items.AddRange([_teacher, _other, _former]);
        _svc = new PlanningService(_uow, new Admin(_uow));
    }

    private void Roster(int year, int quarter, StaffMember? staff) =>
        _uow.RosterAssignmentsRepo.Items.Add(new RosterAssignment
        {
            ParticipantId = _star.Id, ProgramId = _program.Id, Year = year, Quarter = quarter, AssignedStaffId = staff?.Id,
        });

    private void Plan(string monthKey, StaffMember? staff) =>
        _uow.PerStarPlansRepo.Items.Add(new PerStarPlan { ParticipantId = _star.Id, MonthKey = monthKey, AssignedStaffId = staff?.Id });

    private async Task<PerStarPlanDto> Row(string monthKey) =>
        Assert.Single(await _svc.GetPerStarPlansAsync(_userId, monthKey, null));

    [Fact]
    public async Task Roster_assignment_for_the_quarter_is_the_default()
    {
        Roster(2026, 3, _teacher);

        var plan = await Row("2026-09");

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("Roster", plan.AssignedStaffSource);
        Assert.Equal("Q3 2026", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task A_plan_that_names_nobody_still_falls_back_to_the_roster()
    {
        Roster(2026, 3, _teacher);
        await _svc.UpsertPerStarPlanAsync(_userId, new UpsertPerStarPlanDto { ParticipantId = _star.Id, MonthKey = "2026-09", AssignedStaffId = null, PrimaryTier = ProgressLevel.Novice });

        var plan = await Row("2026-09");

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("Roster", plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_plan_that_names_someone_wins_over_the_roster()
    {
        Roster(2026, 3, _teacher);
        var saved = await _svc.UpsertPerStarPlanAsync(_userId, new UpsertPerStarPlanDto { ParticipantId = _star.Id, MonthKey = "2026-09", AssignedStaffId = _other.Id, PrimaryTier = ProgressLevel.Novice });

        var plan = await Row("2026-09");

        Assert.Equal(_other.Id, plan.AssignedStaffId);
        Assert.Equal("Plan", plan.AssignedStaffSource);
        Assert.Equal("2026-09", plan.AssignedStaffSourceTerm);
        Assert.Equal("Plan", saved!.AssignedStaffSource);
    }

    // ── Carry-over after a rollover (Oct 2026) ─────────────────────────────────

    [Fact]
    public async Task An_earlier_quarter_s_roster_applies_as_PreviousRoster_when_this_quarter_is_empty()
    {
        Roster(2026, 2, _teacher);

        var plan = await Row("2026-09");

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("Teacher One", plan.AssignedStaffName);
        Assert.Equal("PreviousRoster", plan.AssignedStaffSource);
        Assert.Equal("Q2 2026", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task The_most_recent_earlier_quarter_wins_including_across_a_year_boundary()
    {
        Roster(2025, 4, _teacher);
        Roster(2026, 1, _other);

        var plan = await Row("2026-05");

        Assert.Equal(_other.Id, plan.AssignedStaffId);
        Assert.Equal("Q1 2026", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task A_later_quarter_s_roster_never_applies()
    {
        Roster(2026, 4, _teacher);
        Roster(2027, 1, _other);

        var plan = await Row("2026-09");

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
        Assert.Null(plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task The_latest_earlier_month_s_plan_applies_as_PreviousPlan()
    {
        Plan("2026-07", _other);
        Plan("2026-09", _teacher);

        var plan = await Row("2026-10");

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("PreviousPlan", plan.AssignedStaffSource);
        Assert.Equal("2026-09", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task An_earlier_plan_that_names_nobody_is_passed_over()
    {
        Plan("2026-08", _teacher);
        Plan("2026-09", null);

        var plan = await Row("2026-10");

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("2026-08", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task This_quarter_s_roster_wins_over_an_earlier_plan()
    {
        Plan("2026-09", _other);
        Roster(2026, 4, _teacher);

        var plan = await Row("2026-10");

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("Roster", plan.AssignedStaffSource);
        Assert.Equal("Q4 2026", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task An_earlier_plan_wins_over_an_earlier_roster_from_the_same_quarter()
    {
        // July and Q3 are the same term, so the tie goes to the plan.
        Roster(2026, 3, _teacher);
        Plan("2026-07", _other);

        var plan = await Row("2026-10");

        Assert.Equal(_other.Id, plan.AssignedStaffId);
        Assert.Equal("PreviousPlan", plan.AssignedStaffSource);
        Assert.Equal("2026-07", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task A_newer_earlier_roster_wins_over_an_older_earlier_plan()
    {
        // An April plan must not beat the Q3 roster: the more recent term wins.
        Plan("2026-04", _other);
        Roster(2026, 3, _teacher);

        var plan = await Row("2026-10");

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("PreviousRoster", plan.AssignedStaffSource);
        Assert.Equal("Q3 2026", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task A_newer_earlier_plan_wins_over_an_older_earlier_roster()
    {
        Roster(2026, 2, _teacher);
        Plan("2026-08", _other);

        var plan = await Row("2026-10");

        Assert.Equal(_other.Id, plan.AssignedStaffId);
        Assert.Equal("PreviousPlan", plan.AssignedStaffSource);
        Assert.Equal("2026-08", plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task A_later_month_s_plan_never_applies()
    {
        Plan("2026-11", _teacher);

        var plan = await Row("2026-10");

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_former_latest_plan_owner_leaves_no_carry_over_rather_than_an_older_teacher()
    {
        // The star's most recent owner has left. Reaching back to August's teacher would credit
        // someone who hasn't had the star for months (the Alec-style miscount).
        Plan("2026-08", _teacher);
        Plan("2026-09", _former);

        var plan = await Row("2026-10");

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
        Assert.Null(plan.AssignedStaffSourceTerm);
    }

    [Fact]
    public async Task A_former_latest_roster_owner_leaves_no_carry_over_rather_than_an_older_teacher()
    {
        Roster(2026, 1, _teacher);
        Roster(2026, 2, _former);

        var plan = await Row("2026-09");

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_former_newer_roster_owner_is_not_swapped_for_an_older_plan()
    {
        // April's plan named a current teacher; Q3's roster named someone who has since left.
        // The most recent hint is the former teacher, so there is no carry-over at all.
        Plan("2026-04", _teacher);
        Roster(2026, 3, _former);

        var plan = await Row("2026-10");

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_former_newer_plan_owner_is_not_swapped_for_an_older_roster()
    {
        Roster(2026, 1, _teacher);
        Plan("2026-09", _former);

        var plan = await Row("2026-10");

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
    }

    [Fact]
    public async Task After_copy_forward_leaves_a_former_teacher_s_star_unassigned_planning_agrees()
    {
        // Q2 was X, Q3 was Y, Y left Sep 30; copy-forward wrote an empty Q4 row.
        Roster(2026, 2, _teacher);
        Plan("2026-06", _teacher);
        Roster(2026, 3, _former);
        Plan("2026-09", _former);
        Roster(2026, 4, null);

        var plan = await Row("2026-10");

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_former_teacher_named_for_this_month_still_shows()
    {
        // Only carried-over names are filtered; what someone set for this term is shown as-is
        // (the page labels the name "(former)").
        Plan("2026-10", _former);

        var plan = await Row("2026-10");

        Assert.Equal(_former.Id, plan.AssignedStaffId);
        Assert.Equal("Plan", plan.AssignedStaffSource);
    }
}
