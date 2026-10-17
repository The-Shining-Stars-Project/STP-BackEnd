using CRM.Application.DTOs.Roster;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Xunit;

namespace CRM.Tests;

/// <summary>
/// "Copy assignments from Q3": roster rows are per quarter and nothing carried over on Oct 1,
/// so every Assigned staff went blank (Oct 2026). Copy-forward fills only the gaps, from each
/// enrolment's most recent earlier term, and only in the caller's programs.
/// </summary>
public class RosterCopyForwardTests
{
    private static readonly Guid ProgramA = Guid.NewGuid();
    private static readonly Guid ProgramB = Guid.NewGuid();
    private static readonly Guid SiteMain = Guid.NewGuid();
    private static readonly Guid SiteAnnex = Guid.NewGuid();
    private static readonly Guid SiteRetired = Guid.NewGuid();
    private static readonly Guid GroupBright = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly RosterService _roster;
    private readonly Guid _adminUser;
    private readonly Guid _coordinatorAUser;
    private readonly StaffMember _teacher = new() { FullName = "Teacher One", Initials = "TO", Role = StaffRole.Teacher };
    private readonly StaffMember _formerTeacher = new() { FullName = "Gone Teacher", Initials = "GT", Role = StaffRole.Teacher, EndDate = new DateTime(2026, 9, 30) };
    private readonly Participant _starA = new() { FullName = "Star In A", Initials = "SA", ProgramId = ProgramA };
    private readonly Participant _starB = new() { FullName = "Star In B", Initials = "SB", ProgramId = ProgramB };

    public RosterCopyForwardTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = ProgramA, Name = "Program A", Slug = "program-a" },
            new CrmProgram { Id = ProgramB, Name = "Program B", Slug = "program-b" },
        ]);
        _uow.SitesRepo.Items.AddRange([
            new Site { Id = SiteMain, Name = "Main", Slug = "main", SortOrder = 1 },
            new Site { Id = SiteAnnex, Name = "Annex", Slug = "annex", SortOrder = 2 },
            new Site { Id = SiteRetired, Name = "Old Hall", Slug = "old-hall", SortOrder = 3, IsActive = false },
        ]);
        _uow.StarGroups.AddAsync(new StarGroup { Id = GroupBright, Name = "Bright Stars", Slug = "bright-stars" }).GetAwaiter().GetResult();

        var coordinatorA = new StaffMember { FullName = "Coordinator A", Initials = "CA", Role = StaffRole.Coordinator };
        _uow.StaffRepo.Items.AddRange([_teacher, _formerTeacher, coordinatorA]);
        _uow.StaffProgramAssignments.Add(new StaffProgramAssignment { StaffMemberId = coordinatorA.Id, ProgramId = ProgramA });

        var admin = new User { Email = "admin@x.org", FullName = "Admin", Role = UserRole.Admin };
        var coordinatorUser = new User { Email = "coord@x.org", FullName = "Coordinator A", Role = UserRole.Staff, StaffMemberId = coordinatorA.Id };
        _uow.UsersRepo.Items.AddRange([admin, coordinatorUser]);
        _adminUser = admin.Id;
        _coordinatorAUser = coordinatorUser.Id;

        _uow.ParticipantsRepo.Items.AddRange([_starA, _starB]);
        _roster = new RosterService(_uow, new ProgramAccessService(_uow));
    }

    private RosterAssignment Row(Participant star, Guid programId, int year, int quarter,
        StaffMember? staff = null, Guid? group = null, bool countedInRatio = true, string? notes = null, Guid[]? sites = null)
    {
        sites ??= [];
        var row = new RosterAssignment
        {
            ParticipantId = star.Id, ProgramId = programId, Year = year, Quarter = quarter,
            SiteId = sites.Length > 0 ? sites[0] : null, StarGroupId = group, AssignedStaffId = staff?.Id,
            CountedInRatio = countedInRatio, Notes = notes,
        };
        _uow.RosterAssignmentsRepo.Items.Add(row);
        foreach (var site in sites)
            _uow.RosterAssignmentSites.Add(new RosterAssignmentSite { RosterAssignmentId = row.Id, SiteId = site });
        return row;
    }

    private Task<CopyRosterForwardResultDto> CopyInto(int year, int quarter, Guid? user = null) =>
        _roster.CopyForwardAsync(user ?? _adminUser, new CopyRosterForwardDto { Year = year, Quarter = quarter });

    private RosterAssignment? Target(Participant star, Guid programId, int year = 2026, int quarter = 4) =>
        _uow.RosterAssignmentsRepo.Items.SingleOrDefault(r =>
            r.ParticipantId == star.Id && r.ProgramId == programId && r.Year == year && r.Quarter == quarter);

    [Fact]
    public async Task Copies_the_latest_earlier_term_with_sites_group_staff_ratio_and_notes()
    {
        Row(_starA, ProgramA, 2026, 2, staff: null, notes: "older");
        Row(_starA, ProgramA, 2026, 3, _teacher, GroupBright, countedInRatio: false, notes: "Needs a quiet corner", sites: [SiteAnnex, SiteMain]);

        var result = await CopyInto(2026, 4);

        Assert.Equal(1, result.Copied);
        var copy = Target(_starA, ProgramA)!;
        Assert.Equal(_teacher.Id, copy.AssignedStaffId);
        Assert.Equal(GroupBright, copy.StarGroupId);
        Assert.False(copy.CountedInRatio);
        Assert.Equal("Needs a quiet corner", copy.Notes);
        Assert.Equal(SiteAnnex, copy.SiteId);

        // The roster view reads the copy back with every site, primary first.
        var entry = Assert.Single(await _roster.GetRosterAsync(_adminUser, 2026, 4, null), e => e.ParticipantId == _starA.Id);
        Assert.Equal(new[] { SiteAnnex, SiteMain }, entry.SiteIds);
        Assert.Equal("Teacher One", entry.AssignedStaffName);
        Assert.Equal("Bright Stars", entry.StarGroupName);
    }

    [Fact]
    public async Task Copies_across_a_year_boundary()
    {
        Row(_starA, ProgramA, 2026, 4, _teacher);

        Assert.Equal(1, (await CopyInto(2027, 1)).Copied);
        Assert.Equal(_teacher.Id, Target(_starA, ProgramA, 2027, 1)!.AssignedStaffId);
    }

    [Fact]
    public async Task An_enrolment_that_already_has_a_row_in_the_term_is_left_alone()
    {
        Row(_starA, ProgramA, 2026, 3, _teacher, notes: "Q3 notes", sites: [SiteMain]);
        var existing = Row(_starA, ProgramA, 2026, 4, staff: null, notes: "set by hand");
        Row(_starB, ProgramB, 2026, 3, _teacher);

        var result = await CopyInto(2026, 4);

        Assert.Equal(1, result.Copied); // star B only
        Assert.NotNull(Target(_starB, ProgramB));
        Assert.Same(existing, Target(_starA, ProgramA));
        Assert.Null(existing.AssignedStaffId);
        Assert.Equal("set by hand", existing.Notes);
        Assert.DoesNotContain(_uow.RosterAssignmentSites, s => s.RosterAssignmentId == existing.Id);
    }

    [Fact]
    public async Task A_former_teacher_is_not_carried_into_the_new_term()
    {
        Row(_starA, ProgramA, 2026, 3, _formerTeacher, GroupBright, notes: "kept", sites: [SiteMain]);

        Assert.Equal(1, (await CopyInto(2026, 4)).Copied);

        var copy = Target(_starA, ProgramA)!;
        Assert.Null(copy.AssignedStaffId);
        Assert.Equal(GroupBright, copy.StarGroupId);
        Assert.Equal(SiteMain, copy.SiteId);
    }

    [Fact]
    public async Task A_retired_site_is_not_carried_into_the_new_term()
    {
        Row(_starA, ProgramA, 2026, 3, _teacher, sites: [SiteRetired, SiteMain]);

        await CopyInto(2026, 4);

        var copy = Target(_starA, ProgramA)!;
        Assert.Equal(SiteMain, copy.SiteId);
        Assert.Equal(new[] { SiteMain }, _uow.RosterAssignmentSites.Where(s => s.RosterAssignmentId == copy.Id).Select(s => s.SiteId));
    }

    [Fact]
    public async Task Programs_outside_the_caller_s_scope_are_untouched()
    {
        Row(_starA, ProgramA, 2026, 3, _teacher);
        Row(_starB, ProgramB, 2026, 3, _teacher);

        var result = await CopyInto(2026, 4, _coordinatorAUser);

        Assert.Equal(1, result.Copied);
        Assert.NotNull(Target(_starA, ProgramA));
        Assert.Null(Target(_starB, ProgramB));
    }

    [Fact]
    public async Task A_later_term_is_never_a_source()
    {
        Row(_starA, ProgramA, 2026, 4, _teacher);

        var result = await CopyInto(2026, 3);

        Assert.Equal(0, result.Copied);
        Assert.Null(Target(_starA, ProgramA, 2026, 3));
    }

    [Fact]
    public async Task An_enrolment_with_no_earlier_row_gets_nothing()
    {
        Assert.Equal(0, (await CopyInto(2026, 4)).Copied);
        Assert.Empty(_uow.RosterAssignmentsRepo.Items);
    }

    [Fact]
    public async Task Running_it_twice_copies_nothing_the_second_time()
    {
        Row(_starA, ProgramA, 2026, 3, _teacher, sites: [SiteMain]);
        Row(_starB, ProgramB, 2026, 2, _teacher);

        Assert.Equal(2, (await CopyInto(2026, 4)).Copied);
        Assert.Equal(0, (await CopyInto(2026, 4)).Copied);
        Assert.Equal(4, _uow.RosterAssignmentsRepo.Items.Count);
    }

    [Fact]
    public async Task Each_enrolment_of_a_dual_enrolled_star_copies_its_own_row()
    {
        var dual = new Participant { FullName = "Dual Star", Initials = "DS", ProgramId = ProgramA, SecondaryProgramId = ProgramB };
        _uow.ParticipantsRepo.Items.Add(dual);
        var other = new StaffMember { FullName = "Teacher Two", Initials = "TT" };
        _uow.StaffRepo.Items.Add(other);
        Row(dual, ProgramA, 2026, 3, _teacher);
        Row(dual, ProgramB, 2026, 3, other);

        Assert.Equal(2, (await CopyInto(2026, 4)).Copied);
        Assert.Equal(_teacher.Id, Target(dual, ProgramA)!.AssignedStaffId);
        Assert.Equal(other.Id, Target(dual, ProgramB)!.AssignedStaffId);
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 5)]
    [InlineData(2019, 1)]
    [InlineData(2101, 1)]
    public async Task An_invalid_term_is_refused(int year, int quarter)
    {
        Row(_starA, ProgramA, 2026, 3, _teacher);

        await Assert.ThrowsAsync<ArgumentException>(() => CopyInto(year, quarter));
        Assert.Single(_uow.RosterAssignmentsRepo.Items);
    }
}
