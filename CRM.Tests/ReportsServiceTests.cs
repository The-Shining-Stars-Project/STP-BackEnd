using CRM.Application.Services;
using CRM.Domain.Entities;
using TaskStatus = CRM.Domain.Enums.TaskStatus;

namespace CRM.Tests;

/// <summary>
/// Oct 2026 Reports fixes: the Staff Onboarding list and its totals cover current staff only,
/// with rows keyed by staff id, and Overdue tasks use the same read-time rule as the Tasks page.
/// </summary>
public class ReportsServiceTests
{
    private static readonly DateTime Today = new(2026, 10, 16);

    private readonly FakeUnitOfWork _uow = new();
    private readonly ReportsService _service;

    public ReportsServiceTests() =>
        _service = new ReportsService(_uow, new FakeStatsQueries(), new FakeOrgClock(Today));

    private StaffMember Hire(string name, int pct, DateTime? endDate = null)
    {
        var s = new StaffMember
        {
            Id = Guid.NewGuid(), FullName = name, Initials = name[..2].ToUpperInvariant(),
            OnboardingProgressPct = pct, EndDate = endDate,
        };
        _uow.StaffRepo.Items.Add(s);
        return s;
    }

    [Fact]
    public async Task Former_staff_are_left_out_of_the_onboarding_report_and_totals()
    {
        var ana = Hire("Ana Lee", 100);
        var scott = Hire("Scott Davis", 50);
        Hire("Bo Former", 100, endDate: new DateTime(2026, 9, 1));
        // The duplicate record from the client's list: same name, marked former.
        Hire("Scott Davis", 100, endDate: new DateTime(2026, 9, 30));

        var report = await _service.GetAsync();

        Assert.Equal([scott.Id, ana.Id], report.StaffOnboarding.Select(s => s.Id).ToArray()); // lowest pct first
        Assert.Equal(["Scott Davis", "Ana Lee"], report.StaffOnboarding.Select(s => s.Name).ToArray());
        Assert.Equal(2, report.Totals.Staff);
        Assert.Equal(1, report.Totals.FullyOnboardedStaff);
    }

    [Fact]
    public async Task Overdue_tasks_are_counted_from_the_due_date_not_the_stored_flag()
    {
        var projectId = Guid.NewGuid();
        var tasks = ((FakeRepository<ProjectTask>)_uow.Tasks).Items;
        tasks.AddRange([
            new ProjectTask { ProjectId = projectId, Name = "Past due, open", DueDate = Today.AddDays(-1) },
            new ProjectTask { ProjectId = projectId, Name = "Marked overdue", DueDate = Today.AddDays(3), Status = TaskStatus.Overdue },
            new ProjectTask { ProjectId = projectId, Name = "Past due, done", DueDate = Today.AddDays(-1), Status = TaskStatus.Done },
            new ProjectTask { ProjectId = projectId, Name = "Due today", DueDate = Today },
            new ProjectTask { ProjectId = projectId, Name = "Stale flag", DueDate = Today.AddDays(3), IsOverdue = true },
        ]);

        var report = await _service.GetAsync();

        Assert.Equal(2, report.Totals.OverdueTasks);
        Assert.Equal(4, report.Totals.OpenTasks);
    }
}
