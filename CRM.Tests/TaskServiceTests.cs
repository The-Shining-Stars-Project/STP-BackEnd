using CRM.Application.DTOs.Tasks;
using CRM.Application.Services;
using CRM.Domain.Entities;
using TaskStatus = CRM.Domain.Enums.TaskStatus;

namespace CRM.Tests;

/// <summary>
/// Oct 2026 Tasks fixes. Adding a task validates its name and project and keeps the status
/// chosen in the modal (it used to be dropped). Overdue is computed from the due date when
/// read: the stored flag was only ever set by the demo seeder, so production showed none.
/// </summary>
public class TaskServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 16);

    private readonly FakeUnitOfWork _uow = new();
    private readonly TaskService _service;
    private readonly Guid _projectId = Guid.NewGuid();

    public TaskServiceTests()
    {
        Projects.Add(new Project { Id = _projectId, Title = "ASCAP Reporting" });
        _service = new TaskService(_uow, new FakeOrgClock(Today));
    }

    private List<Project> Projects => ((FakeRepository<Project>)_uow.Projects).Items;
    private List<ProjectTask> Tasks => ((FakeRepository<ProjectTask>)_uow.Tasks).Items;

    private ProjectTask SeedTask(DateTime? due, TaskStatus status = TaskStatus.Upcoming, bool storedFlag = false)
    {
        var task = new ProjectTask
        {
            Id = Guid.NewGuid(), ProjectId = _projectId, Name = "Task", DueDate = due,
            Status = status, IsOverdue = storedFlag,
        };
        Tasks.Add(task);
        return task;
    }

    private async Task<ProjectTaskDto> ReadAsync(Guid taskId) =>
        (await _service.GetProjectsAsync()).SelectMany(p => p.Tasks).Single(t => t.Id == taskId);

    // ── Adding a task ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_task_needs_a_name(string name)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = name }));

        Assert.Equal("Task name is required.", ex.Message);
        Assert.Empty(Tasks);
    }

    [Fact]
    public async Task The_name_is_saved_trimmed()
    {
        var dto = await _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = "  Book the venue  " });

        Assert.Equal("Book the venue", dto.Name);
        Assert.Equal("Book the venue", Tasks.Single().Name);
    }

    [Fact]
    public async Task A_missing_project_is_not_found_rather_than_a_database_error()
    {
        // KeyNotFoundException maps to 404 in GlobalExceptionHandler; before, the bad id
        // reached the database as a foreign-key violation.
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.AddTaskAsync(Guid.NewGuid(), new CreateTaskDto { Name = "Book the venue" }));

        Assert.Empty(Tasks);
    }

    [Theory]
    [InlineData(TaskStatus.Upcoming)]
    [InlineData(TaskStatus.InProgress)]
    [InlineData(TaskStatus.Blocked)]
    public async Task The_status_chosen_in_the_modal_is_saved(TaskStatus status)
    {
        var dto = await _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = "Book the venue", Status = status });

        Assert.Equal(status, dto.TaskStatus);
        Assert.Equal(status, Tasks.Single().Status);
    }

    [Fact]
    public async Task No_status_means_upcoming()
    {
        var dto = await _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = "Book the venue" });

        Assert.Equal(TaskStatus.Upcoming, dto.TaskStatus);
    }

    [Theory]
    [InlineData(TaskStatus.Done)]
    [InlineData(TaskStatus.Overdue)]
    [InlineData((TaskStatus)99)]
    public async Task A_new_task_cannot_start_done_overdue_or_unknown(TaskStatus status)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = "Book the venue", Status = status }));

        Assert.Empty(Tasks);
    }

    // ── Overdue, computed when read ─────────────────────────────────────────────

    [Fact]
    public async Task An_open_task_past_its_due_date_is_overdue()
    {
        var task = SeedTask(Today.AddDays(-1));

        Assert.True((await ReadAsync(task.Id)).IsOverdue);
    }

    [Fact]
    public async Task A_done_task_past_its_due_date_is_not_overdue()
    {
        var task = SeedTask(Today.AddDays(-1), TaskStatus.Done);

        Assert.False((await ReadAsync(task.Id)).IsOverdue);
    }

    [Fact]
    public async Task A_task_due_today_is_not_overdue_yet()
    {
        // A due date that carries a time of day still compares as a date.
        var task = SeedTask(Today.AddHours(9));

        Assert.False((await ReadAsync(task.Id)).IsOverdue);
    }

    [Fact]
    public async Task A_task_with_no_due_date_is_never_overdue()
    {
        var task = SeedTask(null);

        Assert.False((await ReadAsync(task.Id)).IsOverdue);
    }

    [Fact]
    public async Task A_task_explicitly_marked_overdue_counts()
    {
        var task = SeedTask(Today.AddDays(5), TaskStatus.Overdue);

        Assert.True((await ReadAsync(task.Id)).IsOverdue);
    }

    [Fact]
    public async Task The_stored_flag_is_no_longer_read()
    {
        var task = SeedTask(Today.AddDays(5), storedFlag: true);

        Assert.False((await ReadAsync(task.Id)).IsOverdue);
    }

    [Fact]
    public async Task Add_and_update_report_overdue_with_the_same_rule()
    {
        var added = await _service.AddTaskAsync(_projectId,
            new CreateTaskDto { Name = "File the report", DueDate = Today.AddDays(-2) });
        Assert.True(added.IsOverdue);

        var done = await _service.UpdateTaskAsync(added.Id, new UpdateTaskDto { TaskStatus = TaskStatus.Done });
        Assert.False(done!.IsOverdue);
    }

    // ── Edit and delete (Oct 2026) ──────────────────────────────────────────────

    [Fact]
    public async Task Draft_is_an_accepted_starting_status_and_round_trips()
    {
        var created = await _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = "Idea: costume swap", Status = TaskStatus.Draft });
        Assert.Equal(TaskStatus.Draft, created.TaskStatus);
        Assert.Equal(TaskStatus.Draft, (await ReadAsync(created.Id)).TaskStatus);
    }

    [Fact]
    public async Task Clearing_the_assignee_and_due_date_needs_the_explicit_flags()
    {
        var staffId = Guid.NewGuid();
        var task = SeedTask(new DateTime(2026, 11, 1));
        task.AssignedToId = staffId;

        // Null means "leave alone" — the modal sends only what changed.
        await _service.UpdateTaskAsync(task.Id, new UpdateTaskDto { Name = "Renamed" });
        Assert.Equal(staffId, task.AssignedToId);
        Assert.NotNull(task.DueDate);

        await _service.UpdateTaskAsync(task.Id, new UpdateTaskDto { ClearAssignee = true, ClearDueDate = true });
        Assert.Null(task.AssignedToId);
        Assert.Null(task.DueDate);
    }

    [Fact]
    public async Task A_blank_name_on_update_is_refused()
    {
        var task = SeedTask(null);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateTaskAsync(task.Id, new UpdateTaskDto { Name = "   " }));
        Assert.Equal("Task", task.Name);
    }

    [Fact]
    public async Task Deleting_a_task_removes_only_that_task()
    {
        var gone = SeedTask(null);
        var kept = SeedTask(null);

        Assert.True(await _service.DeleteTaskAsync(gone.Id));
        Assert.False(await _service.DeleteTaskAsync(gone.Id));

        Assert.Single(Tasks);
        Assert.Equal(kept.Id, Tasks[0].Id);
    }

    [Fact]
    public async Task Updating_a_project_changes_only_what_was_sent()
    {
        var project = Projects.Single();
        project.DueDate = new DateTime(2026, 12, 1);
        SeedTask(null);

        var dto = await _service.UpdateProjectAsync(project.Id, new UpdateProjectDto { Status = "Done", Scope = "  " });

        Assert.Equal("done", dto!.Status);
        Assert.Null(dto.Scope);
        Assert.Equal("ASCAP Reporting", dto.Title);
        Assert.Equal("2026-12-01", dto.DueDate);
        Assert.Single(dto.Tasks);

        await _service.UpdateProjectAsync(project.Id, new UpdateProjectDto { ClearDueDate = true });
        Assert.Null(project.DueDate);
        Assert.Null(await _service.UpdateProjectAsync(Guid.NewGuid(), new UpdateProjectDto { Title = "x" }));
    }

    [Fact]
    public async Task Deleting_a_project_takes_its_tasks_with_it_and_leaves_other_projects_alone()
    {
        var other = new Project { Id = Guid.NewGuid(), Title = "Winter Showcase" };
        Projects.Add(other);
        SeedTask(null);
        SeedTask(null);
        Tasks.Add(new ProjectTask { Id = Guid.NewGuid(), ProjectId = other.Id, Name = "Keep" });

        Assert.True(await _service.DeleteProjectAsync(_projectId));

        Assert.Single(Projects);
        Assert.Equal("Keep", Assert.Single(Tasks).Name);
        Assert.False(await _service.DeleteProjectAsync(_projectId));
    }
}
