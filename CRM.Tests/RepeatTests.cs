using CRM.Application.DTOs.Calendar;
using CRM.Application.DTOs.Tasks;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Xunit;
using TaskStatus = CRM.Domain.Enums.TaskStatus;

namespace CRM.Tests;

/// <summary>
/// Repeat on calendar events (Oct 2026): a series is stored once and expanded per month, and an
/// edit or delete can cover one occurrence, the rest of the series, or all of it.
/// </summary>
public class RepeatingEventTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly CalendarService _calendar;

    public RepeatingEventTests() => _calendar = new CalendarService(_uow, new FakeOrgClock(new DateTime(2026, 10, 1)));

    private List<CalendarEvent> Events => ((FakeRepository<CalendarEvent>)_uow.CalendarEvents).Items;
    private List<CalendarEventSkip> Skips => ((FakeRepository<CalendarEventSkip>)_uow.CalendarEventSkips).Items;

    // Rehearsal every Monday from Oct 5, 2026.
    private Task<CalendarEventDto> Weekly(string title = "Rehearsal", string? until = null) =>
        _calendar.CreateEventAsync(new CreateCalendarEventDto
        {
            Title = title, Date = "2026-10-05", RepeatFrequency = RepeatFrequency.Weekly, RepeatUntil = until,
        });

    private async Task<List<string>> Days(int month, string title = "Rehearsal") =>
        (await _calendar.GetEventsAsync(month, 2026)).Where(e => e.Title == title).Select(e => e.Date).ToList();

    private static UpdateCalendarEventDto Edit(string title, string date, RepeatFrequency? f = null) =>
        new() { Title = title, Date = date, RepeatFrequency = f };

    [Fact]
    public async Task A_weekly_series_is_stored_once_and_shows_on_every_matching_day()
    {
        var created = await Weekly();

        Assert.Single(Events);
        Assert.Equal(new[] { "2026-10-05", "2026-10-12", "2026-10-19", "2026-10-26" }, await Days(10));
        Assert.Equal(5, (await Days(11)).Count); // Nov 2, 9, 16, 23, 30
        Assert.Equal("2026-10-05", created.SeriesStart);
        var oct19 = (await _calendar.GetEventsAsync(10, 2026)).Single(e => e.Date == "2026-10-19");
        Assert.Equal(created.Id, oct19.Id);
        Assert.Equal("2026-10-05", oct19.SeriesStart);
    }

    [Fact]
    public async Task One_off_events_are_unchanged()
    {
        await _calendar.CreateEventAsync(new CreateCalendarEventDto { Title = "Showcase", Date = "2026-12-12" });

        Assert.Equal(new[] { "2026-12-12" }, await Days(12, "Showcase"));
        Assert.Empty(await Days(11, "Showcase"));
    }

    [Fact]
    public async Task Deleting_this_occurrence_skips_only_that_day()
    {
        var s = await Weekly();

        Assert.True(await _calendar.DeleteEventAsync(s.Id, EventEditScope.This, new DateOnly(2026, 10, 12)));
        Assert.True(await _calendar.DeleteEventAsync(s.Id, EventEditScope.This, new DateOnly(2026, 10, 12))); // idempotent

        Assert.Equal(new[] { "2026-10-05", "2026-10-19", "2026-10-26" }, await Days(10));
        Assert.Single(Skips);
        Assert.Single(Events);
    }

    [Fact]
    public async Task Deleting_this_and_following_ends_the_series_the_day_before()
    {
        var s = await Weekly();

        await _calendar.DeleteEventAsync(s.Id, EventEditScope.Following, new DateOnly(2026, 10, 19));

        Assert.Equal(new[] { "2026-10-05", "2026-10-12" }, await Days(10));
        Assert.Empty(await Days(11));
        Assert.Equal(new DateTime(2026, 10, 18), Events.Single().RepeatUntil);
    }

    [Fact]
    public async Task Following_from_the_first_occurrence_or_all_deletes_the_whole_series()
    {
        var a = await Weekly("A");
        var b = await Weekly("B");

        await _calendar.DeleteEventAsync(a.Id, EventEditScope.Following, new DateOnly(2026, 10, 5));
        await _calendar.DeleteEventAsync(b.Id);

        Assert.Empty(Events);
    }

    [Fact]
    public async Task Editing_this_occurrence_lifts_it_out_as_a_one_off()
    {
        var s = await Weekly();

        var moved = await _calendar.UpdateEventAsync(s.Id, Edit("Rehearsal (Tue)", "2026-10-13"), EventEditScope.This, new DateOnly(2026, 10, 12));

        Assert.NotEqual(s.Id, moved!.Id);
        Assert.Equal(RepeatFrequency.None, moved.RepeatFrequency);
        Assert.Equal(new[] { "2026-10-05", "2026-10-19", "2026-10-26" }, await Days(10));
        Assert.Equal(new[] { "2026-10-13" }, await Days(10, "Rehearsal (Tue)"));
    }

    [Fact]
    public async Task Editing_this_and_following_splits_the_series_there()
    {
        var s = await Weekly();

        var rest = await _calendar.UpdateEventAsync(s.Id, Edit("Rehearsal (new room)", "2026-10-19", RepeatFrequency.Weekly), EventEditScope.Following, new DateOnly(2026, 10, 19));

        Assert.Equal(2, Events.Count);
        Assert.Equal(new[] { "2026-10-05", "2026-10-12" }, await Days(10));
        Assert.Equal(new[] { "2026-10-19", "2026-10-26" }, await Days(10, "Rehearsal (new room)"));
        Assert.Equal("2026-10-19", rest!.SeriesStart);
    }

    [Fact]
    public async Task Editing_all_from_a_later_occurrence_keeps_the_start_unless_the_date_moves()
    {
        var s = await Weekly();

        // Rename from the Oct 19 occurrence: the series still starts Oct 5.
        await _calendar.UpdateEventAsync(s.Id, Edit("Rehearsal!", "2026-10-19", RepeatFrequency.Weekly), EventEditScope.All, new DateOnly(2026, 10, 19));
        Assert.Equal(new DateTime(2026, 10, 5), Events.Single().Date);

        // Moving that occurrence to Tuesday moves every occurrence by a day.
        await _calendar.UpdateEventAsync(s.Id, Edit("Rehearsal!", "2026-10-20", RepeatFrequency.Weekly), EventEditScope.All, new DateOnly(2026, 10, 19));
        Assert.Equal(new[] { "2026-10-06", "2026-10-13", "2026-10-20", "2026-10-27" }, await Days(10, "Rehearsal!"));
    }

    [Fact]
    public async Task A_stale_or_missing_occurrence_is_refused_and_bad_rules_are_400s()
    {
        var s = await Weekly();

        await Assert.ThrowsAsync<ArgumentException>(() => _calendar.DeleteEventAsync(s.Id, EventEditScope.This, new DateOnly(2026, 10, 13))); // a Tuesday
        await Assert.ThrowsAsync<ArgumentException>(() => _calendar.DeleteEventAsync(s.Id, EventEditScope.This));
        await Assert.ThrowsAsync<ArgumentException>(() => _calendar.CreateEventAsync(new CreateCalendarEventDto
        {
            Title = "X", Date = "2026-10-05", RepeatFrequency = RepeatFrequency.Weekly, RepeatUntil = "2026-10-01",
        }));
        Assert.Single(Events);
    }

    [Fact]
    public async Task An_until_date_stops_the_series_in_the_month_view()
    {
        await Weekly(until: "2026-10-19");

        Assert.Equal(new[] { "2026-10-05", "2026-10-12", "2026-10-19" }, await Days(10));
        Assert.Empty(await Days(11));
    }
}

/// <summary>
/// Repeat on tasks (Oct 2026): the next task is created when the current one is marked Done —
/// no timers — due on the next occurrence, and annual projects can be copied to next year.
/// </summary>
public class RepeatingTaskTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly FakeUnitOfWork _uow = new();
    private readonly TaskService _service;
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly StaffMember _jo = new() { Id = Guid.NewGuid(), FullName = "JoAnna Pimental", Initials = "JP" };

    public RepeatingTaskTests()
    {
        _service = new TaskService(_uow, new FakeOrgClock(new DateTime(2026, 10, 26)));
        Projects.Add(new Project { Id = _projectId, Title = "ASCAP Reporting 2026", DueDate = new DateTime(2026, 12, 15), Status = "inprogress" });
        _uow.Staff.AddAsync(_jo).GetAwaiter().GetResult();
    }

    private List<Project> Projects => ((FakeRepository<Project>)_uow.Projects).Items;
    private List<ProjectTask> Tasks => ((FakeRepository<ProjectTask>)_uow.Tasks).Items;

    private Task<ProjectTaskDto> Add(DateTime? due, RepeatFrequency f, int interval = 1, DateTime? until = null) =>
        _service.AddTaskAsync(_projectId, new CreateTaskDto
        {
            Name = "Submit attendance report", DueDate = due, RepeatFrequency = f, RepeatInterval = interval, RepeatUntil = until,
            AssignedToId = _jo.Id, Role = TaskRole.Coordinator,
        });

    private Task<ProjectTaskDto?> Done(Guid id) =>
        _service.UpdateTaskAsync(UserId, id, new UpdateTaskDto { TaskStatus = TaskStatus.Done });

    [Fact]
    public async Task Marking_a_repeating_task_done_creates_the_next_one()
    {
        var first = await Add(new DateTime(2026, 10, 30), RepeatFrequency.Weekly);

        await Done(first.Id);

        var next = Assert.Single(Tasks, t => t.Id != first.Id);
        Assert.Equal(new DateTime(2026, 11, 6), next.DueDate);
        Assert.Equal(TaskStatus.Upcoming, next.Status);
        Assert.Equal(first.Id, next.RecurrenceParentId);
        Assert.Equal(_jo.Id, next.AssignedToId);
        Assert.Equal(TaskRole.Coordinator, next.Role);
        Assert.Null(next.CompletedByUserId);
    }

    [Fact]
    public async Task Unchecking_and_rechecking_does_not_create_a_second_copy()
    {
        var first = await Add(new DateTime(2026, 10, 30), RepeatFrequency.Weekly);

        await Done(first.Id);
        await _service.UpdateTaskAsync(UserId, first.Id, new UpdateTaskDto { TaskStatus = TaskStatus.Upcoming });
        await Done(first.Id);

        Assert.Equal(2, Tasks.Count);
    }

    [Fact]
    public async Task Monthly_on_the_31st_follows_the_anchor_through_short_months()
    {
        var jan = await Add(new DateTime(2027, 1, 31), RepeatFrequency.Monthly);

        await Done(jan.Id);
        var feb = Tasks.Single(t => t.RecurrenceParentId == jan.Id);
        await Done(feb.Id);
        var mar = Tasks.Single(t => t.RecurrenceParentId == feb.Id);

        Assert.Equal(new DateTime(2027, 2, 28), feb.DueDate);
        Assert.Equal(new DateTime(2027, 3, 31), mar.DueDate); // not stuck on the 28th
    }

    [Fact]
    public async Task Saving_the_form_without_changing_the_rule_keeps_the_anchor()
    {
        var jan = await Add(new DateTime(2027, 1, 31), RepeatFrequency.Monthly);
        await Done(jan.Id);
        var feb = Tasks.Single(t => t.RecurrenceParentId == jan.Id);

        // The edit modal re-sends the same rule with a rename; that must not re-anchor on Feb 28.
        await _service.UpdateTaskAsync(UserId, feb.Id, new UpdateTaskDto { Name = "Renamed", RepeatFrequency = RepeatFrequency.Monthly, RepeatInterval = 1 });
        await Done(feb.Id);
        Assert.Equal(new DateTime(2027, 3, 31), Tasks.Single(t => t.RecurrenceParentId == feb.Id).DueDate);

        // Actually moving the due date does re-anchor: the schedule follows the new date.
        var mar = Tasks.Single(t => t.RecurrenceParentId == feb.Id);
        await _service.UpdateTaskAsync(UserId, mar.Id, new UpdateTaskDto { DueDate = new DateTime(2027, 3, 15), RepeatFrequency = RepeatFrequency.Monthly, RepeatInterval = 1 });
        await Done(mar.Id);
        Assert.Equal(new DateTime(2027, 4, 15), Tasks.Single(t => t.RecurrenceParentId == mar.Id).DueDate);
    }

    [Fact]
    public async Task The_series_stops_at_its_end_date_and_former_staff_are_not_carried_over()
    {
        var last = await Add(new DateTime(2026, 11, 6), RepeatFrequency.Weekly, until: new DateTime(2026, 11, 10));
        await Done(last.Id);
        Assert.Single(Tasks); // next would be Nov 13, past the end

        var other = await Add(new DateTime(2026, 11, 6), RepeatFrequency.Weekly);
        _jo.EndDate = new DateTime(2026, 10, 31);
        await Done(other.Id);
        Assert.Null(Tasks.Single(t => t.RecurrenceParentId == other.Id).AssignedToId);
    }

    [Fact]
    public async Task A_repeating_task_needs_a_due_date_and_None_stops_it()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Add(null, RepeatFrequency.Weekly));

        var t = await Add(new DateTime(2026, 10, 30), RepeatFrequency.Weekly);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateTaskAsync(UserId, t.Id, new UpdateTaskDto { ClearDueDate = true }));

        await _service.UpdateTaskAsync(UserId, t.Id, new UpdateTaskDto { RepeatFrequency = RepeatFrequency.None });
        await Done(t.Id);
        Assert.Single(Tasks);
    }

    [Fact]
    public async Task Copy_to_next_year_bumps_dates_and_the_title_and_skips_superseded_repeats()
    {
        var weekly = await Add(new DateTime(2026, 10, 30), RepeatFrequency.Weekly);
        await Done(weekly.Id); // spawns Nov 6; the Oct 30 one is now superseded
        var oneOff = await _service.AddTaskAsync(_projectId, new CreateTaskDto { Name = "File ASCAP form", DueDate = new DateTime(2026, 12, 1) });
        await Done(oneOff.Id);

        var copy = await _service.CopyProjectToNextYearAsync(_projectId);

        Assert.Equal("ASCAP Reporting 2027", copy!.Title);
        Assert.Equal("2027-12-15", copy.DueDate);
        Assert.Equal("planning", copy.Status);
        Assert.Equal(2, copy.Tasks.Count);
        Assert.Contains(copy.Tasks, t => t.Name == "Submit attendance report" && t.DueDate == "2027-11-06" && t.RepeatFrequency == RepeatFrequency.Weekly);
        Assert.Contains(copy.Tasks, t => t.Name == "File ASCAP form" && t.DueDate == "2027-12-01");
        Assert.All(copy.Tasks, t => { Assert.Equal(TaskStatus.Upcoming, t.TaskStatus); Assert.Null(t.CompletedByUserId); });
        Assert.Null(await _service.CopyProjectToNextYearAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Copy_appends_the_year_when_the_title_has_none()
    {
        Projects[0].Title = "Winter Showcase";

        Assert.Equal("Winter Showcase (2027)", (await _service.CopyProjectToNextYearAsync(_projectId))!.Title);
    }
}
