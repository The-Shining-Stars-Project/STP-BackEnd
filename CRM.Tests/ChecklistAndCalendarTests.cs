using CRM.Application.DTOs.Calendar;
using CRM.Application.DTOs.Staff;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>
/// Sep 14 2026 meeting items: checklist edits reach current staff, renewable items stamp
/// their expiry from the completion date, N/A items drop out of progress, and calendar
/// events can be edited, deleted and tied to several sites.
/// </summary>
public class ChecklistAndCalendarTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly StaffService _staff;
    private readonly CalendarService _calendar;
    private readonly FakeOrgClock _clock = new();

    public ChecklistAndCalendarTests()
    {
        _staff = new StaffService(_uow, _clock, new FakeFileStorage(), NullLogger<StaffService>.Instance);
        _calendar = new CalendarService(_uow, _clock);
    }

    private async Task<Guid> HireAsync(string name)
    {
        var s = await _staff.CreateAsync(new CreateStaffDto { FullName = name, Initials = name[..2].ToUpperInvariant(), Role = StaffRole.Teacher });
        return s.Id;
    }

    private async Task SetTemplateAsync(params (string Section, string Label, int? Months)[] items) =>
        await _staff.UpdateChecklistTemplateAsync(new UpdateChecklistTemplateDto
        {
            Items = items.Select(i => new ChecklistTemplateItemDto { Section = i.Section, Label = i.Label, RenewalMonths = i.Months }).ToList(),
        });

    // ── Template sync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Editing_the_template_adds_new_items_to_current_staff()
    {
        await SetTemplateAsync(("Training", "CPR", 24));
        var id = await HireAsync("Ana Lee");

        await SetTemplateAsync(("Training", "CPR", 24), ("Documents", "Fingerprinting", null));

        var detail = await _staff.GetByIdAsync(id);
        Assert.Equal(["CPR", "Fingerprinting"], detail!.OnboardingItems.OrderBy(o => o.Label).Select(o => o.Label).ToArray());
    }

    [Fact]
    public async Task Removing_a_template_item_drops_it_unless_it_is_done_or_has_a_file()
    {
        await SetTemplateAsync(("Training", "CPR", 24), ("Documents", "Old form", null), ("Documents", "Signed policy", null));
        var id = await HireAsync("Ana Lee");
        var before = await _staff.GetByIdAsync(id);
        var policy = before!.OnboardingItems.Single(o => o.Label == "Signed policy");
        await _staff.SetOnboardingItemAsync(id, policy.Id, new SetOnboardingItemDto { IsCompleted = true });

        await SetTemplateAsync(("Training", "CPR", 24));

        var after = await _staff.GetByIdAsync(id);
        var labels = after!.OnboardingItems.Select(o => o.Label).OrderBy(l => l).ToArray();
        Assert.Equal(["CPR", "Signed policy"], labels); // "Old form" (incomplete) is gone; the completed one stays
    }

    [Fact]
    public async Task Template_renewal_changes_reach_existing_items()
    {
        await SetTemplateAsync(("Training", "TB", null));
        var id = await HireAsync("Ana Lee");

        await SetTemplateAsync(("Training", "TB", 48));

        var item = (await _staff.GetByIdAsync(id))!.OnboardingItems.Single();
        Assert.Equal(48, item.RenewalMonths);
    }

    // ── Renewals + N/A ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Completing_a_renewable_item_stamps_its_expiry_from_the_completion_date()
    {
        await SetTemplateAsync(("Training", "TB", 48));
        var id = await HireAsync("Ana Lee");
        var item = (await _staff.GetByIdAsync(id))!.OnboardingItems.Single();

        var detail = await _staff.SetOnboardingItemAsync(id, item.Id,
            new SetOnboardingItemDto { IsCompleted = true, CompletedDate = new DateTime(2026, 9, 14) });

        var done = detail!.OnboardingItems.Single();
        Assert.Equal("2026-09-14", done.CompletedDate);
        Assert.Equal("2030-09-14", done.ExpiryDate);

        var undone = await _staff.SetOnboardingItemAsync(id, item.Id, new SetOnboardingItemDto { IsCompleted = false });
        Assert.Null(undone!.OnboardingItems.Single().ExpiryDate);
    }

    [Fact]
    public async Task Not_applicable_items_are_left_out_of_progress()
    {
        await SetTemplateAsync(("Documents", "Fingerprinting", null), ("Documents", "I-9", null));
        var id = await HireAsync("Ana Lee");
        var items = (await _staff.GetByIdAsync(id))!.OnboardingItems;
        var fp = items.Single(o => o.Label == "Fingerprinting");
        var i9 = items.Single(o => o.Label == "I-9");

        await _staff.SetOnboardingItemAsync(id, fp.Id, new SetOnboardingItemDto { IsCompleted = false, IsNotApplicable = true });
        var detail = await _staff.SetOnboardingItemAsync(id, i9.Id, new SetOnboardingItemDto { IsCompleted = true });

        Assert.Equal(100, detail!.OnboardingProgressPct);
        Assert.True(detail.OnboardingItems.Single(o => o.Label == "Fingerprinting").IsNotApplicable);
    }

    [Fact]
    public async Task Expiring_training_surfaces_as_an_alert_on_the_staff_list()
    {
        await SetTemplateAsync(("Training", "CPR", 24));
        var id = await HireAsync("Ana Lee");
        var item = (await _staff.GetByIdAsync(id))!.OnboardingItems.Single();
        // Completed 23 months ago → due in about a month → inside the 60-day window.
        await _staff.SetOnboardingItemAsync(id, item.Id,
            new SetOnboardingItemDto { IsCompleted = true, CompletedDate = _clock.Today.AddMonths(-23) });

        var summary = (await _staff.GetAllAsync()).Single(s => s.Id == id);
        var alert = Assert.Single(summary.TrainingAlerts);
        Assert.Equal("CPR", alert.Label);
        Assert.InRange(alert.DaysUntil, 25, 35);
    }

    // ── Calendar ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Events_carry_sites_and_can_be_edited_and_deleted()
    {
        var mjc = new Site { Id = Guid.NewGuid(), Name = "MJC", Slug = "mjc", SortOrder = 1, IsActive = true };
        var manteca = new Site { Id = Guid.NewGuid(), Name = "Manteca", Slug = "manteca", SortOrder = 2, IsActive = true };
        _uow.SitesRepo.Items.AddRange([mjc, manteca]);

        var created = await _calendar.CreateEventAsync(new CreateCalendarEventDto
        {
            Title = "Showcase", Date = "2026-10-03", SiteIds = [manteca.Id, mjc.Id], Meta = "Zoom: https://zoom.us/j/123",
        });
        Assert.Equal(["MJC", "Manteca"], created.SiteNames); // site sort order, not the order sent

        var updated = await _calendar.UpdateEventAsync(created.Id, new UpdateCalendarEventDto
        {
            Title = "Showcase (moved)", Date = "2026-10-10", SiteIds = [manteca.Id],
        });
        Assert.Equal("Showcase (moved)", updated!.Title);
        Assert.Equal(["Manteca"], updated.SiteNames);
        Assert.Equal("2026-10-10", updated.Date);

        Assert.True(await _calendar.DeleteEventAsync(created.Id));
        Assert.Null(await _calendar.UpdateEventAsync(created.Id, new UpdateCalendarEventDto { Title = "x", Date = "2026-10-10" }));
        Assert.Empty(await _calendar.GetEventsAsync(10, 2026));
    }
}
