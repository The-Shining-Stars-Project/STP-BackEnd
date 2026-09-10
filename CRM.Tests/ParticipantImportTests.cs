using System.Text;
using CRM.Application.Exceptions;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Xunit;

namespace CRM.Tests;

/// <summary>
/// The self-serve Stars import: accepts the export's own columns and the review sheet's,
/// reports every row by spreadsheet line, and never writes while any row has a problem.
/// </summary>
public class ParticipantImportTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid MjcId = Guid.NewGuid();
    private static readonly Guid PathwaysId = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly ParticipantImportService _service;

    public ParticipantImportTests()
    {
        _service = new ParticipantImportService(_uow, new FakeAllowAllAccess(_uow), new FakeAuditService(), new FakeOrgClock());
        _uow.Programs.AddAsync(new CrmProgram { Id = MjcId, Name = "MJC", Slug = "mjc" }).GetAwaiter().GetResult();
        _uow.Programs.AddAsync(new CrmProgram { Id = PathwaysId, Name = "Pathways", Slug = "pathways" }).GetAwaiter().GetResult();
    }

    private static Stream CsvOf(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Join("\r\n", lines)));

    private const string ExportHeader =
        "Name,DOB,Birth year,Program,Also enrolled in,Status,Alerts,Attendance %,Service coordinator,Started,Guardian,Guardian phone,Guardian email,Referral source,T-shirt size,POS expiry,IPP expiry,Allergies (* = anaphylactic)";

    // ── Dry run ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_columns_round_trip_and_a_dry_run_writes_nothing()
    {
        var csv = CsvOf(ExportHeader,
            "\"Ada Lovelace\",2012-05-01,2012,MJC,Pathways,Active,none,92,Sam SC,2024-09-03,Mary L,209-555-0100,mary@example.org,Regional center,YM,2027-06-30,2026-12-31,\"peanuts *\"");

        var report = await _service.ImportAsync(UserId, csv, "stars.csv", commit: false);

        Assert.Empty(report.FileProblems);
        Assert.Equal(1, report.RowCount);
        Assert.Equal(1, report.ReadyCount);
        Assert.Equal(0, report.ProblemCount);
        Assert.False(report.Committed);
        Assert.Empty(await _uow.Participants.GetAllAsync());
        var row = Assert.Single(report.Rows);
        Assert.Equal(2, row.Line);
        Assert.Equal("ready", row.Status);
    }

    [Fact]
    public async Task Review_sheet_columns_are_accepted_as_aliases()
    {
        var csv = CsvOf(
            "Review flags,Last name,First name,Anaphylactic?,Allergies,Program,Secondary program,Status,Start date,DOB,POS exp,IPP exp,Areas of concern,Guardian,Guardian phone,Guardian email,Service coordinator,Shirt,Notes",
            ",Lovelace,Ada,YES,peanuts,mjc,,Active,2024-09-03,2012-05-01,,,,Mary L,,,,YM,");

        var report = await _service.ImportAsync(UserId, csv, "review.csv", commit: true);

        Assert.True(report.Committed);
        var star = Assert.Single(await _uow.Participants.GetAllAsync());
        Assert.Equal("Ada Lovelace", star.FullName);
        Assert.True(star.AllergyAnaphylactic);
        Assert.Equal("peanuts", star.Allergies);
        Assert.Equal(MjcId, star.ProgramId);
    }

    // ── Rules ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Missing_required_columns_is_a_file_problem_not_a_row_problem()
    {
        var report = await _service.ImportAsync(UserId, CsvOf("Nickname,Team", "Ada,Blue"), "x.csv", commit: true);
        Assert.Equal(2, report.FileProblems.Count);
        Assert.Empty(report.Rows);
        Assert.False(report.Committed);
    }

    [Fact]
    public async Task Each_bad_row_reports_its_line_and_every_reason()
    {
        var csv = CsvOf("Name,Program,Started,DOB,Status",
            "Ada Lovelace,MJC,2024-09-03,2012-05-01,Active",
            "Grace Hopper,Drama Club,not a date,,Retired",
            ",MJC,2024-09-03,,");

        var report = await _service.ImportAsync(UserId, csv, "x.csv", commit: true);

        Assert.False(report.Committed);
        Assert.Equal(1, report.ReadyCount);
        Assert.Equal(2, report.ProblemCount);
        var grace = report.Rows.Single(r => r.Line == 3);
        Assert.Equal("error", grace.Status);
        Assert.Contains(grace.Messages, m => m.Contains("Unknown program 'Drama Club'"));
        Assert.Contains(grace.Messages, m => m.Contains("Start date"));
        Assert.Contains(grace.Messages, m => m.Contains("Status 'Retired'"));
        Assert.Contains("No name.", report.Rows.Single(r => r.Line == 4).Messages);
        Assert.Empty(await _uow.Participants.GetAllAsync());
    }

    [Fact]
    public async Task Missing_start_date_is_refused_rather_than_stamped_today()
    {
        var report = await _service.ImportAsync(UserId, CsvOf("Name,Program,Started", "Ada Lovelace,MJC,"), "x.csv", commit: true);
        var row = Assert.Single(report.Rows);
        Assert.Equal("error", row.Status);
        Assert.Contains(row.Messages, m => m.Contains("Start date is missing"));
    }

    [Fact]
    public async Task Existing_star_with_the_same_name_is_reported_not_duplicated()
    {
        await _uow.Participants.AddAsync(new Participant { FullName = "Ada Lovelace", Initials = "AL", ProgramId = MjcId });

        var report = await _service.ImportAsync(UserId, CsvOf("Name,Program,Started", "ada  lovelace,MJC,2024-09-03"), "x.csv", commit: true);

        Assert.False(report.Committed);
        Assert.Contains(Assert.Single(report.Rows).Messages, m => m.Contains("already exists"));
        Assert.Single(await _uow.Participants.GetAllAsync());
    }

    [Fact]
    public async Task Duplicate_rows_within_the_sheet_are_flagged_on_the_later_line()
    {
        var csv = CsvOf("Name,Program,Started", "Ada Lovelace,MJC,2024-09-03", "Ada Lovelace,Pathways,2024-09-03");
        var report = await _service.ImportAsync(UserId, csv, "x.csv", commit: true);
        Assert.Equal("ready", report.Rows[0].Status);
        Assert.Contains("Duplicate of line 2 in this sheet.", report.Rows[1].Messages);
        Assert.False(report.Committed);
    }

    [Fact]
    public async Task Status_labels_from_the_export_are_understood()
    {
        var csv = CsvOf("Name,Program,Started,Status",
            "A One,MJC,2024-09-03,Needs attention",
            "B Two,MJC,2024-09-03,Auth pending",
            "C Three,MJC,2024-09-03,",
            "D Four,MJC,2024-09-03,NotInterested");

        var report = await _service.ImportAsync(UserId, csv, "x.csv", commit: true);

        Assert.True(report.Committed);
        var stars = (await _uow.Participants.GetAllAsync()).ToDictionary(p => p.FullName, p => p.Status);
        Assert.Equal(ParticipantStatus.Attention, stars["A One"]);
        Assert.Equal(ParticipantStatus.AuthPending, stars["B Two"]);
        Assert.Equal(ParticipantStatus.Active, stars["C Three"]);
        Assert.Equal(ParticipantStatus.NotInterested, stars["D Four"]);
    }

    [Fact]
    public async Task Excel_style_dates_are_accepted()
    {
        var csv = CsvOf("Name,Program,Started,DOB,POS expiry", "Ada Lovelace,MJC,9/3/2024,05/01/2012,2027-06-30");
        var report = await _service.ImportAsync(UserId, csv, "x.csv", commit: true);
        Assert.True(report.Committed);
        var star = Assert.Single(await _uow.Participants.GetAllAsync());
        Assert.Equal(new DateTime(2024, 9, 3), star.StartDate);
        Assert.Equal(new DateTime(2012, 5, 1), star.DateOfBirth);
        Assert.Equal(2012, star.BirthYear);
    }

    [Fact]
    public async Task Sibling_initials_are_widened_instead_of_colliding()
    {
        var csv = CsvOf("Name,Program,Started", "Ada Lovelace,MJC,2024-09-03", "Alan Lovelace,MJC,2024-09-03");
        var report = await _service.ImportAsync(UserId, csv, "x.csv", commit: true);
        Assert.True(report.Committed);
        var initials = (await _uow.Participants.GetAllAsync()).Select(p => p.Initials).ToHashSet();
        Assert.Equal(2, initials.Count);
        Assert.Contains("AL", initials);
    }

    [Fact]
    public async Task Over_length_values_are_refused_not_truncated()
    {
        var longPhone = new string('9', 60);
        var report = await _service.ImportAsync(UserId,
            CsvOf("Name,Program,Started,Guardian phone", $"Ada Lovelace,MJC,2024-09-03,{longPhone}"), "x.csv", commit: true);
        Assert.Contains(Assert.Single(report.Rows).Messages, m => m.Contains("Guardian phone is 60 characters"));
        Assert.False(report.Committed);
    }

    // ── Commit ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Commit_creates_every_row_and_reports_ids()
    {
        var csv = CsvOf("Name,Program,Started", "Ada Lovelace,MJC,2024-09-03", "Grace Hopper,Pathways,2024-09-03");

        var report = await _service.ImportAsync(UserId, csv, "x.csv", commit: true);

        Assert.True(report.Committed);
        Assert.Equal(2, report.CreatedCount);
        Assert.All(report.Rows, r => { Assert.Equal("created", r.Status); Assert.NotNull(r.ParticipantId); });
        Assert.Equal(2, (await _uow.Participants.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Empty_file_is_rejected_as_invalid()
    {
        await Assert.ThrowsAsync<InvalidFileException>(() =>
            _service.ImportAsync(UserId, new MemoryStream(), "x.csv", commit: false));
    }
}
