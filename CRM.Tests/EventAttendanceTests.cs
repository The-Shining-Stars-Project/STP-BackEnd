using CRM.Application.DTOs.Events;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Event and production registers: one combined list per event, drawing Stars from any
/// programme, tracked separately from class attendance.
/// </summary>
public class EventAttendanceTests
{
    private static readonly Guid MjcId = Guid.Parse("11111111-1111-0000-0000-000000000001");
    private static readonly Guid PathwaysId = Guid.Parse("22222222-2222-0000-0000-000000000002");
    private static readonly Guid MantecaId = Guid.Parse("33333333-3333-0000-0000-000000000003");
    private static readonly Guid SiteA = Guid.Parse("44444444-4444-0000-0000-000000000004");
    private static readonly Guid SiteB = Guid.Parse("55555555-5555-0000-0000-000000000005");
    private static readonly Guid OtherSite = Guid.Parse("66666666-6666-0000-0000-000000000006");

    private static readonly Guid AdminUser = Guid.Parse("aaaa0000-0000-0000-0000-00000000000a");
    private static readonly Guid PathwaysTeacher = Guid.Parse("bbbb0000-0000-0000-0000-00000000000b");
    private static readonly Guid UnlinkedUser = Guid.Parse("cccc0000-0000-0000-0000-00000000000c");

    /// <summary>Admin sees everything; the teacher is scoped to Pathways; the third user to nothing.</summary>
    private sealed class Access : IProgramAccessService
    {
        private readonly FakeUnitOfWork _uow;
        public Access(FakeUnitOfWork uow) => _uow = uow;

        public Task<ProgramAccess> ForUserAsync(Guid userId) => Task.FromResult(
            userId == AdminUser ? new ProgramAccess(true, new HashSet<Guid>())
            : userId == PathwaysTeacher ? new ProgramAccess(false, new HashSet<Guid> { PathwaysId })
            : ProgramAccess.None);

        public async Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId)
        {
            var p = await _uow.Participants.GetByIdAsync(participantId);
            if (p is null) return null;
            (await ForUserAsync(userId)).Require(p.ProgramId);
            return p;
        }
    }

    private readonly FakeUnitOfWork _uow = new();
    private readonly EventAttendanceService _service;

    private readonly Guid _mjcStar, _pathwaysStar, _mantecaStar, _dualStar;

    public EventAttendanceTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = MjcId, Name = "MJC", Slug = "mjc" },
            new CrmProgram { Id = PathwaysId, Name = "Pathways", Slug = "pathways" },
            new CrmProgram { Id = MantecaId, Name = "Manteca PT", Slug = "manteca" },
        ]);
        _uow.SitesRepo.Items.AddRange([
            new Site { Id = SiteA, Name = "Main Stage", Slug = "main" },
            new Site { Id = SiteB, Name = "Studio", Slug = "studio" },
            new Site { Id = OtherSite, Name = "Not Participating", Slug = "other" },
        ]);

        _mjcStar = AddStar("Alpha One", MjcId);
        _pathwaysStar = AddStar("Bravo Two", PathwaysId);
        _mantecaStar = AddStar("Charlie Three", MantecaId);
        // Primary Manteca, secondary Pathways — the dual-enrolment case.
        _dualStar = AddStar("Delta Four", MantecaId, secondary: PathwaysId);

        _service = new EventAttendanceService(_uow, new Access(_uow), new FakeOrgClock());
    }

    private Guid AddStar(string name, Guid programId, Guid? secondary = null)
    {
        var id = Guid.NewGuid();
        _uow.ParticipantsRepo.Items.Add(new Participant
        {
            Id = id, FullName = name, Initials = name[..2].ToUpperInvariant(),
            ProgramId = programId, SecondaryProgramId = secondary, Status = ParticipantStatus.Active,
        });
        return id;
    }

    private Task<EventSessionSummaryDto> NewEvent(params Guid[] siteIds) =>
        _service.CreateAsync(AdminUser, new CreateEventSessionDto
        {
            Title = "Spring Showcase", Category = EventCategory.Production,
            Date = "2026-05-01", Venue = "Main Stage", SiteIds = siteIds.ToList(),
        });

    // ── the requirement ──────────────────────────────────────────────────────

    [Fact]
    public async Task One_register_holds_stars_from_every_program()
    {
        var ev = await NewEvent(SiteA, SiteB);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_mjcStar, _pathwaysStar, _mantecaStar] });

        var roster = await _service.GetRosterAsync(AdminUser, ev.Id);

        Assert.Equal(3, roster!.Entries.Count);
        // As a set: the point is that all three programmes are represented on one register,
        // not what order the names happen to collate in.
        Assert.Equal(
            new HashSet<string> { "MJC", "Manteca PT", "Pathways" },
            roster.Entries.Select(e => e.ProgramName).ToHashSet());
    }

    [Fact]
    public async Task A_dual_enrolled_star_appears_once_as_candidate_and_once_on_the_register()
    {
        var ev = await NewEvent(SiteA);

        var candidates = await _service.GetCandidatesAsync(AdminUser, ev.Id);
        Assert.Single(candidates.Where(c => c.ParticipantId == _dualStar));

        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_dualStar, _dualStar] });

        var roster = await _service.GetRosterAsync(AdminUser, ev.Id);
        Assert.Single(roster!.Entries.Where(e => e.ParticipantId == _dualStar));
    }

    [Fact]
    public async Task Adding_is_idempotent()
    {
        var ev = await NewEvent(SiteA);
        var dto = new AddEventParticipantsDto { ParticipantIds = [_mjcStar, _pathwaysStar] };

        await _service.AddParticipantsAsync(AdminUser, ev.Id, dto);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, dto); // double-tap

        var roster = await _service.GetRosterAsync(AdminUser, ev.Id);
        Assert.Equal(2, roster!.Entries.Count);
    }

    [Fact]
    public async Task Sites_are_recorded_on_the_event()
    {
        var ev = await NewEvent(SiteA, SiteB);
        Assert.Equal(new HashSet<string> { "Main Stage", "Studio" }, ev.Sites.Select(s => s.Name).ToHashSet());
    }

    // ── access ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_teacher_sees_only_their_own_stars_on_a_mixed_register()
    {
        var ev = await NewEvent(SiteA);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_mjcStar, _pathwaysStar, _mantecaStar] });

        var roster = await _service.GetRosterAsync(PathwaysTeacher, ev.Id);

        Assert.Single(roster!.Entries);
        Assert.Equal(_pathwaysStar, roster.Entries[0].ParticipantId);
        // Counts describe what the caller can see, not the whole event.
        Assert.Equal(1, roster.Event.TotalCount);
    }

    [Fact]
    public async Task A_teacher_may_mark_their_own_star_but_not_another_programs()
    {
        var ev = await NewEvent(SiteA);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_mjcStar, _pathwaysStar] });

        var all = await _service.GetRosterAsync(AdminUser, ev.Id);
        var mine = all!.Entries.Single(e => e.ParticipantId == _pathwaysStar);
        var theirs = all.Entries.Single(e => e.ParticipantId == _mjcStar);

        Assert.True(await _service.UpdateRecordAsync(PathwaysTeacher, mine.RecordId,
            new UpdateEventRecordDto { Status = AttendanceStatus.Present }));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.UpdateRecordAsync(PathwaysTeacher, theirs.RecordId,
                new UpdateEventRecordDto { Status = AttendanceStatus.Present }));
    }

    [Fact]
    public async Task A_dual_enrolled_star_is_markable_via_the_secondary_program()
    {
        var ev = await NewEvent(SiteA);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_dualStar] });

        var roster = await _service.GetRosterAsync(AdminUser, ev.Id);
        // Primary is Manteca, which this teacher cannot access; secondary is Pathways, which they can.
        Assert.True(await _service.UpdateRecordAsync(PathwaysTeacher, roster!.Entries[0].RecordId,
            new UpdateEventRecordDto { Status = AttendanceStatus.Present }));
    }

    [Fact]
    public async Task A_user_with_no_program_scope_sees_nothing()
    {
        var ev = await NewEvent(SiteA);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_mjcStar, _pathwaysStar] });

        var roster = await _service.GetRosterAsync(UnlinkedUser, ev.Id);
        Assert.Empty(roster!.Entries);
    }

    // ── locks and validation ─────────────────────────────────────────────────

    [Fact]
    public async Task A_submitted_register_cannot_be_marked()
    {
        var ev = await NewEvent(SiteA);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_pathwaysStar] });
        var roster = await _service.GetRosterAsync(AdminUser, ev.Id);
        await _service.SubmitAsync(AdminUser, ev.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateRecordAsync(AdminUser, roster!.Entries[0].RecordId,
                new UpdateEventRecordDto { Status = AttendanceStatus.Absent }));
    }

    [Fact]
    public async Task A_marked_star_cannot_be_removed_until_unmarked()
    {
        var ev = await NewEvent(SiteA);
        await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_pathwaysStar] });
        var roster = await _service.GetRosterAsync(AdminUser, ev.Id);
        await _service.UpdateRecordAsync(AdminUser, roster!.Entries[0].RecordId,
            new UpdateEventRecordDto { Status = AttendanceStatus.Present });

        // Removing would discard a real observation about whether a child was there.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RemoveParticipantAsync(AdminUser, ev.Id, _pathwaysStar));

        await _service.UpdateRecordAsync(AdminUser, roster.Entries[0].RecordId,
            new UpdateEventRecordDto { Status = AttendanceStatus.Unmarked });
        Assert.True(await _service.RemoveParticipantAsync(AdminUser, ev.Id, _pathwaysStar));
    }

    [Fact]
    public async Task A_site_not_taking_part_is_rejected()
    {
        var ev = await NewEvent(SiteA, SiteB);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
            { ParticipantIds = [_pathwaysStar], SiteId = OtherSite }));

        // A participating site, and no site at all, are both fine.
        Assert.NotNull(await _service.AddParticipantsAsync(AdminUser, ev.Id, new AddEventParticipantsDto
        { ParticipantIds = [_pathwaysStar], SiteId = SiteB }));
    }

    [Fact]
    public async Task The_date_is_normalized_to_midnight()
    {
        var ev = await _service.CreateAsync(AdminUser, new CreateEventSessionDto
        { Title = "Evening Show", Date = "2026-05-01T19:30:00", SiteIds = [] });

        var stored = _uow.EventSessionsRepo.Items.Single(e => e.Id == ev.Id);
        Assert.Equal(new DateTime(2026, 5, 1), stored.Date);
    }
}
