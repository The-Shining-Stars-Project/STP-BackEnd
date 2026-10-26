using CRM.Application.DTOs.Tasks;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Xunit;
using TaskStatus = CRM.Domain.Enums.TaskStatus;

namespace CRM.Tests;

/// <summary>
/// Oct 26 2026 (Rachel): tasks can carry a job-title role, a role task with nobody assigned is a
/// "grab bag" anyone can take, and the login that marks a task Done is recorded so she can count
/// completions per person per role each month for annual evaluations.
/// </summary>
public class TaskCompletionTests
{
    private static readonly DateTime Today = new(2026, 10, 16); // FakeOrgClock.Now = 17:00 that day

    private readonly FakeUnitOfWork _uow = new();
    private readonly TaskService _service;
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly User _rachel;
    private readonly User _alec;
    private readonly StaffMember _rachelStaff;

    public TaskCompletionTests()
    {
        _service = new TaskService(_uow, new FakeOrgClock(Today));
        ((FakeRepository<Project>)_uow.Projects).Items.Add(new Project { Id = _projectId, Title = "Flux tasks" });

        _rachelStaff = new StaffMember { Id = Guid.NewGuid(), FullName = "Rachel Magana", Initials = "RM" };
        _uow.Staff.AddAsync(_rachelStaff).GetAwaiter().GetResult();
        _rachel = new User { Id = Guid.NewGuid(), FullName = "Rachel Magana", Email = "rm@example.org", StaffMemberId = _rachelStaff.Id };
        _alec = new User { Id = Guid.NewGuid(), FullName = "Alec Van Lewen", Email = "av@example.org" };
        _uow.UsersRepo.Items.Add(_rachel);
        _uow.UsersRepo.Items.Add(_alec);
    }

    private List<ProjectTask> Tasks => ((FakeRepository<ProjectTask>)_uow.Tasks).Items;

    private ProjectTask Seed(TaskRole? role = TaskRole.TeachingArtist, TaskStatus status = TaskStatus.Upcoming)
    {
        var t = new ProjectTask { Id = Guid.NewGuid(), ProjectId = _projectId, Name = "Restock props", Role = role, Status = status };
        Tasks.Add(t);
        return t;
    }

    private Task<ProjectTaskDto?> MarkDone(User by, ProjectTask t) =>
        _service.UpdateTaskAsync(by.Id, t.Id, new UpdateTaskDto { TaskStatus = TaskStatus.Done });

    // ── Role ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_role_is_saved_on_create_changed_on_update_and_cleared_explicitly()
    {
        var created = await _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = "Sweep stage", Role = TaskRole.SeniorTeachingArtist });
        Assert.Equal(TaskRole.SeniorTeachingArtist, created.Role);

        var changed = await _service.UpdateTaskAsync(_rachel.Id, created.Id, new UpdateTaskDto { Role = TaskRole.AssistantTeachingArtist });
        Assert.Equal(TaskRole.AssistantTeachingArtist, changed!.Role);

        var untouched = await _service.UpdateTaskAsync(_rachel.Id, created.Id, new UpdateTaskDto { Name = "Sweep the stage" });
        Assert.Equal(TaskRole.AssistantTeachingArtist, untouched!.Role); // null = leave alone

        var cleared = await _service.UpdateTaskAsync(_rachel.Id, created.Id, new UpdateTaskDto { ClearRole = true });
        Assert.Null(cleared!.Role);
    }

    // ── Who completed it ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Marking_done_records_the_login_and_the_org_local_time()
    {
        var t = Seed();

        var dto = await MarkDone(_alec, t);

        Assert.Equal(_alec.Id, t.CompletedByUserId);
        Assert.Equal(Today.AddHours(17), t.CompletedAt);
        Assert.False(t.CompletionEstimated);
        Assert.Equal("Alec Van Lewen", dto!.CompletedByName);
        Assert.Equal("2026-10-16T17:00", dto.CompletedAt);
    }

    [Fact]
    public async Task Re_saving_a_done_task_keeps_the_original_completer()
    {
        var t = Seed();
        await MarkDone(_alec, t);

        // Rachel fixes a typo, and even re-sends Done: Alec still gets the credit.
        await _service.UpdateTaskAsync(_rachel.Id, t.Id, new UpdateTaskDto { Name = "Restock props (Act 2)", TaskStatus = TaskStatus.Done });

        Assert.Equal(_alec.Id, t.CompletedByUserId);
    }

    [Fact]
    public async Task Reopening_clears_the_completer_and_finishing_again_credits_the_new_person()
    {
        var t = Seed();
        await MarkDone(_alec, t);

        await _service.UpdateTaskAsync(_alec.Id, t.Id, new UpdateTaskDto { TaskStatus = TaskStatus.Upcoming });
        Assert.Null(t.CompletedByUserId);
        Assert.Null(t.CompletedAt);

        await MarkDone(_rachel, t);
        Assert.Equal(_rachel.Id, t.CompletedByUserId);
    }

    [Fact]
    public async Task A_completer_whose_account_was_deleted_is_still_listed_as_Former_user()
    {
        var t = Seed();
        await MarkDone(_alec, t);
        _uow.UsersRepo.Items.Remove(_alec);

        var read = (await _service.GetProjectsAsync()).SelectMany(p => p.Tasks).Single();

        Assert.Equal(_alec.Id, read.CompletedByUserId);
        Assert.Equal("Former user", read.CompletedByName);
    }

    // ── Grab bag: Take it ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Take_assigns_an_open_task_to_the_callers_staff_record_and_is_idempotent()
    {
        var t = Seed();

        var dto = await _service.TakeTaskAsync(_rachel.Id, t.Id);
        Assert.Equal(_rachelStaff.Id, dto!.AssignedToId);
        Assert.Equal("Rachel Magana", dto.AssignedToName);

        Assert.Equal(_rachelStaff.Id, (await _service.TakeTaskAsync(_rachel.Id, t.Id))!.AssignedToId);
        Assert.Null(await _service.TakeTaskAsync(_rachel.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Take_refuses_when_someone_else_has_it_or_the_login_has_no_current_staff_record()
    {
        var taken = Seed();
        taken.AssignedToId = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.TakeTaskAsync(_rachel.Id, taken.Id));
        Assert.Contains("already took", ex.Message);

        var open = Seed();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.TakeTaskAsync(_alec.Id, open.Id)); // unlinked login

        _rachelStaff.EndDate = Today.AddDays(-1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.TakeTaskAsync(_rachel.Id, open.Id)); // former staff
        Assert.Null(open.AssignedToId);
    }

    // ── The evaluation report ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_report_counts_each_persons_completions_per_role_for_the_month()
    {
        foreach (var role in new TaskRole?[] { TaskRole.SeniorTeachingArtist, TaskRole.SeniorTeachingArtist, TaskRole.SeniorTeachingArtist,
                                               TaskRole.TeachingArtist, TaskRole.TeachingArtist, null })
            await MarkDone(_rachel, Seed(role));
        await MarkDone(_alec, Seed(TaskRole.TeachingArtist));
        Seed(TaskRole.TeachingArtist); // still open: not counted

        var report = await _service.GetCompletionsAsync(2026, 10);

        var rachel = report.People[0];
        Assert.Equal("Rachel Magana", rachel.Name);
        Assert.Equal(6, rachel.Total);
        Assert.Equal(3, rachel.ByRole["SeniorTeachingArtist"]);
        Assert.Equal(2, rachel.ByRole["TeachingArtist"]);
        Assert.Equal(1, rachel.ByRole["None"]);
        Assert.Equal(1, report.People[1].Total);
        Assert.Equal(7, report.Items.Count);
        Assert.All(report.Items, i => Assert.Equal("Flux tasks", i.ProjectTitle));
    }

    [Fact]
    public async Task The_report_uses_org_local_months_and_flags_estimated_rows()
    {
        // 23:30 on Oct 31 local is already Nov 1 in UTC; it must still count for October.
        Tasks.Add(new ProjectTask { ProjectId = _projectId, Name = "Late", Status = TaskStatus.Done, Role = TaskRole.Coordinator,
                                    CompletedByUserId = _rachel.Id, CompletedAt = new DateTime(2026, 10, 31, 23, 30, 0), CompletionEstimated = true });
        Tasks.Add(new ProjectTask { ProjectId = _projectId, Name = "November", Status = TaskStatus.Done,
                                    CompletedByUserId = _rachel.Id, CompletedAt = new DateTime(2026, 11, 1, 0, 5, 0) });

        var october = await _service.GetCompletionsAsync(2026, 10);

        var rachel = Assert.Single(october.People);
        Assert.Equal(1, rachel.Total);
        Assert.Equal(1, rachel.Estimated);
        Assert.True(Assert.Single(october.Items).Estimated);
        Assert.Single((await _service.GetCompletionsAsync(2026, 11)).Items);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetCompletionsAsync(2026, 13));
    }
}
