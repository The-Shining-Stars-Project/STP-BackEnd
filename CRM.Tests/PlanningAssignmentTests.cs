using CRM.Application.DTOs.Planning;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Per-Star Planning defaults "Assigned staff" to the quarter's roster assignment, and says
/// where the name came from — teachers reported roster assignments "not populating" (Sep 2026).
/// </summary>
public class PlanningAssignmentTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly PlanningService _svc;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Participant _star;
    private readonly StaffMember _teacher = new() { FullName = "Teacher One", Initials = "TO" };
    private readonly StaffMember _other = new() { FullName = "Teacher Two", Initials = "TT" };

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
        var program = new CrmProgram { Name = "MJC", Slug = "mjc" };
        _uow.ProgramsRepo.Items.Add(program);
        _star = new Participant { FullName = "Star A", Initials = "SA", ProgramId = program.Id };
        _uow.ParticipantsRepo.Items.Add(_star);
        _uow.StaffRepo.Items.Add(_teacher);
        _uow.StaffRepo.Items.Add(_other);
        _svc = new PlanningService(_uow, new Admin(_uow));
    }

    [Fact]
    public async Task Roster_assignment_for_the_quarter_is_the_default()
    {
        _uow.RosterAssignmentsRepo.Items.Add(new RosterAssignment { ParticipantId = _star.Id, Year = 2026, Quarter = 3, AssignedStaffId = _teacher.Id });

        var plan = Assert.Single(await _svc.GetPerStarPlansAsync(_userId, "2026-09", null));

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("Roster", plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_plan_that_names_nobody_still_falls_back_to_the_roster()
    {
        _uow.RosterAssignmentsRepo.Items.Add(new RosterAssignment { ParticipantId = _star.Id, Year = 2026, Quarter = 3, AssignedStaffId = _teacher.Id });
        await _svc.UpsertPerStarPlanAsync(_userId, new UpsertPerStarPlanDto { ParticipantId = _star.Id, MonthKey = "2026-09", AssignedStaffId = null, PrimaryTier = ProgressLevel.Novice });

        var plan = Assert.Single(await _svc.GetPerStarPlansAsync(_userId, "2026-09", null));

        Assert.Equal(_teacher.Id, plan.AssignedStaffId);
        Assert.Equal("Roster", plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_plan_that_names_someone_wins_over_the_roster()
    {
        _uow.RosterAssignmentsRepo.Items.Add(new RosterAssignment { ParticipantId = _star.Id, Year = 2026, Quarter = 3, AssignedStaffId = _teacher.Id });
        await _svc.UpsertPerStarPlanAsync(_userId, new UpsertPerStarPlanDto { ParticipantId = _star.Id, MonthKey = "2026-09", AssignedStaffId = _other.Id, PrimaryTier = ProgressLevel.Novice });

        var plan = Assert.Single(await _svc.GetPerStarPlansAsync(_userId, "2026-09", null));

        Assert.Equal(_other.Id, plan.AssignedStaffId);
        Assert.Equal("Plan", plan.AssignedStaffSource);
    }

    [Fact]
    public async Task A_different_quarter_s_roster_does_not_apply()
    {
        _uow.RosterAssignmentsRepo.Items.Add(new RosterAssignment { ParticipantId = _star.Id, Year = 2026, Quarter = 2, AssignedStaffId = _teacher.Id });

        var plan = Assert.Single(await _svc.GetPerStarPlansAsync(_userId, "2026-09", null));

        Assert.Null(plan.AssignedStaffId);
        Assert.Null(plan.AssignedStaffSource);
    }
}
