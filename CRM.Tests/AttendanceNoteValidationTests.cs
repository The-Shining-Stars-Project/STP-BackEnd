using CRM.Application.DTOs.Attendance;
using CRM.Application.Services;
using CRM.Domain.Entities;

namespace CRM.Tests;

/// <summary>
/// Attendance notes are checked before they reach SQL (Oct 2026). An over-long note used to
/// fail the 1000-character column as a 409 that the page swallowed, so it "didn't save".
/// </summary>
public class AttendanceNoteValidationTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly AttendanceService _service;
    private readonly AttendanceRecord _record;

    public AttendanceNoteValidationTests()
    {
        _service = new AttendanceService(_uow, new FakeAllowAllAccess(_uow), new FakeOrgClock());

        var program = new CrmProgram { Name = "MJC", Slug = "mjc" };
        var session = new Session { ProgramId = program.Id, Date = new DateTime(2026, 10, 14) };
        var star = new Participant { FullName = "Sky Drullard", Initials = "SD", ProgramId = program.Id };
        _record = new AttendanceRecord { SessionId = session.Id, ParticipantId = star.Id };

        _uow.ProgramsRepo.Items.Add(program);
        _uow.Sessions.AddAsync(session).GetAwaiter().GetResult();
        _uow.ParticipantsRepo.Items.Add(star);
        _uow.AttendanceRepo.Items.Add(_record);
    }

    private Task<AttendanceNoteDto?> Add(string content, string noteType = "observation") =>
        _service.AddNoteAsync(UserId, _record.Id, new CreateAttendanceNoteDto { Content = content, NoteType = noteType });

    private async Task<int> SavedNotes() => (await _uow.AttendanceNotes.GetAllAsync()).Count;

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData(null)]
    public async Task A_blank_note_is_refused(string? content)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Add(content!));
        Assert.Equal(0, await SavedNotes());
    }

    [Fact]
    public async Task A_note_over_1000_characters_is_refused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Add(new string('a', 1001)));

        Assert.Contains("1000", ex.Message);
        Assert.Equal(0, await SavedNotes());
    }

    [Fact]
    public async Task A_note_of_exactly_1000_characters_is_saved_trimmed()
    {
        // Surrounding whitespace is trimmed first, so it doesn't count toward the limit.
        var note = await Add("  " + new string('a', 1000) + "\n");

        Assert.NotNull(note);
        Assert.Equal(1000, note!.Content.Length);
        Assert.Equal(1, await SavedNotes());
    }

    [Fact]
    public async Task An_unknown_note_type_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Add("Great focus today", noteType: "praise"));
        Assert.Equal(0, await SavedNotes());
    }

    [Theory]
    [InlineData("observation", "observation")]
    [InlineData("concern", "concern")]
    [InlineData("Concern", "concern")]
    [InlineData("", "observation")]
    public async Task The_note_types_the_app_uses_are_accepted(string sent, string stored)
    {
        var note = await Add("Arrived late — follow up with family.", noteType: sent);

        Assert.Equal(stored, note!.NoteType);
        Assert.Equal(stored, (await _uow.AttendanceNotes.GetAllAsync()).Single().NoteType);
    }
}
