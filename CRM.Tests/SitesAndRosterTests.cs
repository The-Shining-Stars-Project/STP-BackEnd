using CRM.Application.DTOs.Roster;
using CRM.Application.DTOs.Taxonomy;
using CRM.Application.Services;
using CRM.Domain.Entities;
using Xunit;

namespace CRM.Tests;

/// <summary>Sites managed from Settings, and a Star attending more than one of them in a term.</summary>
public class SitesAndRosterTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProgramId = Guid.NewGuid();
    private static readonly Guid StarId = Guid.NewGuid();
    private static readonly Guid Mjc = Guid.NewGuid();
    private static readonly Guid Manteca = Guid.NewGuid();
    private static readonly Guid Closed = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly SiteService _sites;
    private readonly RosterService _roster;

    public SitesAndRosterTests()
    {
        _sites = new SiteService(_uow);
        _roster = new RosterService(_uow, new FakeAllowAllAccess(_uow));
        _uow.Programs.AddAsync(new CrmProgram { Id = ProgramId, Name = "MJC", Slug = "mjc" }).GetAwaiter().GetResult();
        _uow.Participants.AddAsync(new Participant { Id = StarId, FullName = "Ada Lovelace", Initials = "AL", ProgramId = ProgramId }).GetAwaiter().GetResult();
        _uow.Sites.AddAsync(new Site { Id = Mjc, Name = "MJC Modesto", Slug = "mjc-modesto", SortOrder = 1 }).GetAwaiter().GetResult();
        _uow.Sites.AddAsync(new Site { Id = Manteca, Name = "Manteca", Slug = "manteca", SortOrder = 2 }).GetAwaiter().GetResult();
        _uow.Sites.AddAsync(new Site { Id = Closed, Name = "Old Annex", Slug = "old-annex", SortOrder = 3, IsActive = false }).GetAwaiter().GetResult();
    }

    // ── Sites ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_slugifies_and_appends_to_the_order()
    {
        var created = await _sites.CreateAsync(new CreateSiteDto { Name = "  Turlock   Library " });
        Assert.Equal("Turlock Library", created.Name);
        Assert.Equal("turlock-library", created.Slug);
        Assert.Equal(4, created.SortOrder);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Duplicate_active_name_is_refused_and_a_retired_twin_is_revived()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sites.CreateAsync(new CreateSiteDto { Name = "manteca" }));

        var revived = await _sites.CreateAsync(new CreateSiteDto { Name = "Old Annex" });
        Assert.Equal(Closed, revived.Id);
        Assert.True(revived.IsActive);
        Assert.Equal(3, (await _uow.Sites.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Slug_collisions_get_a_suffix_and_renames_keep_the_slug()
    {
        var twin = await _sites.CreateAsync(new CreateSiteDto { Name = "Manteca!" });
        Assert.Equal("manteca-2", twin.Slug);

        var renamed = await _sites.UpdateAsync(Manteca, new UpdateSiteDto { Name = "Manteca Public Library" });
        Assert.Equal("Manteca Public Library", renamed!.Name);
        Assert.Equal("manteca", renamed.Slug);
    }

    [Fact]
    public async Task Retiring_hides_from_dropdowns_but_keeps_the_row()
    {
        var retired = await _sites.UpdateAsync(Mjc, new UpdateSiteDto { IsActive = false });
        Assert.False(retired!.IsActive);
        Assert.Contains(await _sites.GetAllAsync(), s => s.Id == Mjc);
        Assert.Null(await _sites.UpdateAsync(Guid.NewGuid(), new UpdateSiteDto { IsActive = true }));
    }

    // ── Roster: one Star, two sites ───────────────────────────────────────────────

    private Task<RosterEntryDto?> Upsert(params Guid[] siteIds) =>
        _roster.UpsertAssignmentAsync(UserId, new UpsertRosterAssignmentDto
        {
            ParticipantId = StarId, Year = 2026, Quarter = 4, SiteIds = siteIds.ToList(),
        });

    [Fact]
    public async Task A_star_can_attend_two_sites_and_the_first_is_primary()
    {
        var entry = await Upsert(Manteca, Mjc);

        Assert.Equal(new[] { Manteca, Mjc }, entry!.SiteIds);
        Assert.Equal(new[] { "Manteca", "MJC Modesto" }, entry.SiteNames);
        Assert.Equal(Manteca, entry.SiteId);
        Assert.Equal("Manteca", entry.SiteName);
    }

    [Fact]
    public async Task Site_filter_matches_any_of_the_stars_sites()
    {
        await Upsert(Manteca, Mjc);

        Assert.Single(await _roster.GetRosterAsync(UserId, 2026, 4, Mjc));
        Assert.Single(await _roster.GetRosterAsync(UserId, 2026, 4, Manteca));
        Assert.Empty(await _roster.GetRosterAsync(UserId, 2026, 4, Closed));
    }

    [Fact]
    public async Task Updating_replaces_the_site_list_and_an_empty_list_clears_it()
    {
        await Upsert(Manteca, Mjc);
        var narrowed = await Upsert(Mjc);
        Assert.Equal(new[] { Mjc }, narrowed!.SiteIds);
        Assert.Equal(Mjc, narrowed.SiteId);

        var cleared = await Upsert();
        Assert.Empty(cleared!.SiteIds);
        Assert.Null(cleared.SiteId);
        Assert.Single(_uow.RosterAssignmentsRepo.Items); // still one row per star per term
    }

    [Fact]
    public async Task Retired_sites_are_dropped_from_a_save()
    {
        var entry = await Upsert(Closed, Mjc);
        Assert.Equal(new[] { Mjc }, entry!.SiteIds);
    }

    [Fact]
    public async Task The_single_site_form_still_works()
    {
        var entry = await _roster.UpsertAssignmentAsync(UserId, new UpsertRosterAssignmentDto
        {
            ParticipantId = StarId, Year = 2026, Quarter = 4, SiteId = Manteca,
        });
        Assert.Equal(new[] { Manteca }, entry!.SiteIds);
        Assert.Equal(Manteca, entry.SiteId);
    }
}

/// <summary>A dual-enrolled Star has one roster row per program, each with its own placement.</summary>
public class DualEnrollmentRosterTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Pathways = Guid.NewGuid();
    private static readonly Guid PartTime = Guid.NewGuid();
    private static readonly Guid StarId = Guid.NewGuid();
    private static readonly Guid TeacherA = Guid.NewGuid();
    private static readonly Guid TeacherB = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly RosterService _roster;

    public DualEnrollmentRosterTests()
    {
        _roster = new RosterService(_uow, new FakeAllowAllAccess(_uow));
        _uow.Programs.AddAsync(new CrmProgram { Id = Pathways, Name = "Pathways: Manteca", Slug = "pathways:-manteca" }).GetAwaiter().GetResult();
        _uow.Programs.AddAsync(new CrmProgram { Id = PartTime, Name = "Manteca: Part-Time", Slug = "manteca:-part-time" }).GetAwaiter().GetResult();
        _uow.Participants.AddAsync(new Participant { Id = StarId, FullName = "C Quiwa", Initials = "CQ", ProgramId = Pathways, SecondaryProgramId = PartTime }).GetAwaiter().GetResult();
        _uow.Staff.AddAsync(new StaffMember { Id = TeacherA, FullName = "Teacher A", Initials = "TA" }).GetAwaiter().GetResult();
        _uow.Staff.AddAsync(new StaffMember { Id = TeacherB, FullName = "Teacher B", Initials = "TB" }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Roster_lists_the_star_under_both_programs()
    {
        var rows = await _roster.GetRosterAsync(UserId, 2026, 4, null);

        Assert.Equal(2, rows.Count);
        var pathways = Assert.Single(rows, r => r.ProgramId == Pathways);
        var pt = Assert.Single(rows, r => r.ProgramId == PartTime);
        Assert.False(pathways.IsSecondaryEnrollment);
        Assert.True(pt.IsSecondaryEnrollment);
    }

    [Fact]
    public async Task Each_enrollment_keeps_its_own_placement()
    {
        await _roster.UpsertAssignmentAsync(UserId, new UpsertRosterAssignmentDto { ParticipantId = StarId, ProgramId = Pathways, Year = 2026, Quarter = 4, AssignedStaffId = TeacherA });
        await _roster.UpsertAssignmentAsync(UserId, new UpsertRosterAssignmentDto { ParticipantId = StarId, ProgramId = PartTime, Year = 2026, Quarter = 4, AssignedStaffId = TeacherB });

        var rows = await _roster.GetRosterAsync(UserId, 2026, 4, null);
        Assert.Equal(TeacherA, rows.Single(r => r.ProgramId == Pathways).AssignedStaffId);
        Assert.Equal(TeacherB, rows.Single(r => r.ProgramId == PartTime).AssignedStaffId);
        Assert.Equal(2, _uow.RosterAssignmentsRepo.Items.Count);
    }

    [Fact]
    public async Task Omitting_the_program_means_the_primary()
    {
        var entry = await _roster.UpsertAssignmentAsync(UserId, new UpsertRosterAssignmentDto { ParticipantId = StarId, Year = 2026, Quarter = 4, AssignedStaffId = TeacherA });
        Assert.Equal(Pathways, entry!.ProgramId);
        Assert.False(entry.IsSecondaryEnrollment);
    }

    [Fact]
    public async Task A_program_the_star_is_not_in_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _roster.UpsertAssignmentAsync(UserId,
            new UpsertRosterAssignmentDto { ParticipantId = StarId, ProgramId = Guid.NewGuid(), Year = 2026, Quarter = 4 }));
    }
}
