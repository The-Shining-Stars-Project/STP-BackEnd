using CRM.Application.DTOs.Participants;
using CRM.Application.DTOs.Volunteers;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Client fixes from Sep 2026: stars and volunteers are soft-deleted (the row stays, the
/// query filter hides it — a hard delete failed on attendance history), the start date is
/// editable, emergency contacts ride along as a capped list, and a star's documents are
/// admin-only even on the profile read.
/// </summary>
public class StarLifecycleTests
{
    private static readonly Guid ProgramA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser = Guid.Parse("55555555-0000-0000-0000-000000000003");
    private static readonly Guid TeacherUser = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid StaffA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Star = Guid.Parse("77777777-0000-0000-0000-000000000001");

    private readonly FakeUnitOfWork _uow = new();
    private readonly ParticipantService _participants;
    private readonly VolunteerService _volunteers;

    public StarLifecycleTests()
    {
        _uow.ProgramsRepo.Items.Add(new CrmProgram { Id = ProgramA, Name = "Program A", Slug = "program-a", ColorHex = "#111111" });
        _uow.StaffRepo.Items.Add(new StaffMember { Id = StaffA, FullName = "Teacher A", Initials = "TA", Role = StaffRole.Teacher });
        _uow.StaffProgramAssignments.Add(new StaffProgramAssignment { StaffMemberId = StaffA, ProgramId = ProgramA });
        _uow.UsersRepo.Items.AddRange([
            new User { Id = AdminUser, Email = "admin@x.org", FullName = "Admin", Role = UserRole.Admin },
            new User { Id = TeacherUser, Email = "a@x.org", FullName = "Teacher A", Role = UserRole.Staff, StaffMemberId = StaffA },
        ]);
        _uow.ParticipantsRepo.Items.Add(new Participant
        {
            Id = Star, FullName = "Child In A", Initials = "CA", ProgramId = ProgramA,
            StartDate = new DateTime(2026, 1, 5),
        });
        _uow.DocumentRecordsRepo.Items.Add(new DocumentRecord { ParticipantId = Star, DocumentType = "IPP" });

        var access = new ProgramAccessService(_uow);
        _participants = new ParticipantService(_uow, new FakeStatsQueries(), access, new FakeOrgClock());
        _volunteers = new VolunteerService(_uow, new FakeOrgClock());
    }

    // ── Soft delete ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deleting_a_star_flags_the_row_instead_of_removing_it()
    {
        Assert.True(await _participants.DeleteAsync(AdminUser, Star));

        var row = _uow.ParticipantsRepo.Items.Single(p => p.Id == Star);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);
    }

    [Fact]
    public async Task Deleting_a_missing_star_reports_not_found()
    {
        Assert.False(await _participants.DeleteAsync(AdminUser, Guid.NewGuid()));
    }

    [Fact]
    public async Task Deleting_a_volunteer_flags_the_row_instead_of_removing_it()
    {
        var created = await _volunteers.CreateAsync(new CreateVolunteerDto { FullName = "Sam Torres", ProgramId = ProgramA });

        Assert.True(await _volunteers.DeleteAsync(created.Id));

        var row = (await _uow.Volunteers.GetAllAsync()).Single(v => v.Id == created.Id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);
    }

    // ── Start date ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Start_date_is_editable()
    {
        var updated = await _participants.UpdateAsync(AdminUser, Star,
            new UpdateParticipantDto { StartDate = new DateTime(2025, 9, 15) });

        Assert.Equal("2025-09-15", updated!.StartDate);
    }

    [Fact]
    public async Task Omitting_start_date_leaves_it_alone()
    {
        var updated = await _participants.UpdateAsync(AdminUser, Star, new UpdateParticipantDto { FullName = "Renamed" });

        Assert.Equal("2026-01-05", updated!.StartDate);
    }

    // ── Emergency contacts ──────────────────────────────────────────────────────

    [Fact]
    public async Task Emergency_contacts_round_trip_in_order_and_drop_blanks()
    {
        var updated = await _participants.UpdateAsync(AdminUser, Star, new UpdateParticipantDto
        {
            EmergencyContacts = ["Maria Rivera – (209) 555-0100", "  ", "Uncle Joe 209-555-0199 "],
        });

        Assert.Equal(["Maria Rivera – (209) 555-0100", "Uncle Joe 209-555-0199"], updated!.EmergencyContacts);

        var listed = (await _participants.GetAllAsync(AdminUser)).Single();
        Assert.Equal(2, listed.EmergencyContacts.Count);
    }

    [Fact]
    public async Task Emergency_contacts_null_means_unchanged_and_empty_means_cleared()
    {
        await _participants.UpdateAsync(AdminUser, Star, new UpdateParticipantDto { EmergencyContacts = ["One"] });

        var untouched = await _participants.UpdateAsync(AdminUser, Star, new UpdateParticipantDto { FullName = "Still Here" });
        Assert.Equal(["One"], untouched!.EmergencyContacts);

        var cleared = await _participants.UpdateAsync(AdminUser, Star, new UpdateParticipantDto { EmergencyContacts = [] });
        Assert.Empty(cleared!.EmergencyContacts);
    }

    [Fact]
    public async Task More_than_five_emergency_contacts_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _participants.UpdateAsync(AdminUser, Star,
            new UpdateParticipantDto { EmergencyContacts = ["1", "2", "3", "4", "5", "6"] }));
    }

    [Fact]
    public async Task Emergency_contacts_are_accepted_on_create()
    {
        var created = await _participants.CreateAsync(AdminUser, new CreateParticipantDto
        {
            FullName = "New Star", Initials = "NS", ProgramId = ProgramA,
            EmergencyContacts = ["Dad – 209-555-0111"],
        });

        Assert.Equal(["Dad – 209-555-0111"], created.EmergencyContacts);
    }

    // ── Teachers: notes only ────────────────────────────────────────────────────

    [Fact]
    public async Task A_teacher_can_change_the_intake_notes_of_a_star_in_their_program()
    {
        var updated = await _participants.UpdateIntakeNotesAsync(TeacherUser, Star, "  Loves the drum circle.  ");

        Assert.Equal("Loves the drum circle.", updated!.IntakeNotes);
        Assert.Equal("Child In A", updated.FullName); // nothing else touched
    }

    [Fact]
    public async Task Blank_notes_clear_the_field()
    {
        await _participants.UpdateIntakeNotesAsync(AdminUser, Star, "something");
        var updated = await _participants.UpdateIntakeNotesAsync(AdminUser, Star, "   ");
        Assert.Null(updated!.IntakeNotes);
    }

    // ── SDP ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sdp_fields_round_trip_and_the_date_clears_with_the_flag()
    {
        var yes = await _participants.UpdateAsync(AdminUser, Star, new UpdateParticipantDto
        {
            IsSdpClient = true, SdpFmsName = "Aveanna", SdpIndependentFacilitator = "Pat Lee",
            SdpStartDate = new DateTime(2026, 10, 1),
        });
        Assert.True(yes!.IsSdpClient);
        Assert.Equal("Aveanna", yes.SdpFmsName);
        Assert.Equal("Pat Lee", yes.SdpIndependentFacilitator);
        Assert.Equal("2026-10-01", yes.SdpStartDate);

        var cleared = await _participants.UpdateAsync(AdminUser, Star, new UpdateParticipantDto { IsSdpClient = false, ClearSdpStartDate = true });
        Assert.False(cleared!.IsSdpClient);
        Assert.Null(cleared.SdpStartDate);
        Assert.Equal("Aveanna", cleared.SdpFmsName); // null string = unchanged, as everywhere else
    }

    // ── Documents are admin-only ────────────────────────────────────────────────

    [Fact]
    public async Task Admins_see_a_stars_documents_on_the_profile()
    {
        var detail = await _participants.GetByIdAsync(AdminUser, Star);
        Assert.Single(detail!.Documents);
    }

    [Fact]
    public async Task Teachers_get_the_profile_without_its_documents()
    {
        var detail = await _participants.GetByIdAsync(TeacherUser, Star);

        Assert.NotNull(detail);
        Assert.Empty(detail!.Documents);
    }
}
