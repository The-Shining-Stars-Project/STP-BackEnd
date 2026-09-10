using CRM.Application.DTOs.Participants;
using CRM.Application.Exceptions;
using CRM.Application.Services;
using CRM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CRM.Tests;

/// <summary>
/// A star's paperwork: records that can exist before their scan, files that are validated by
/// header and replaced safely (new pointer before old blob), and deletes that never leave the
/// UI offering a file storage no longer has.
/// </summary>
public class ParticipantDocumentTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid StarId = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeFileStorage _files = new();
    private readonly ParticipantDocumentService _service;

    public ParticipantDocumentTests()
    {
        _service = new ParticipantDocumentService(
            _uow, _files, new FakeAllowAllAccess(_uow), NullLogger<ParticipantDocumentService>.Instance);
        _uow.Participants.AddAsync(new Participant { Id = StarId, FullName = "Test Star", Initials = "TS" })
            .GetAwaiter().GetResult();
    }

    private async Task<DocumentRecordDto> CreateAsync(string type = "Intake packet") =>
        (await _service.CreateAsync(UserId, StarId, new CreateDocumentRecordDto { DocumentType = type }))!;

    // ── Records ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_then_list_returns_the_record_without_a_file()
    {
        var created = await CreateAsync("POS authorization");

        var list = await _service.ListAsync(UserId, StarId);

        var only = Assert.Single(list!);
        Assert.Equal(created.Id, only.Id);
        Assert.Equal("POS authorization", only.DocumentType);
        Assert.False(only.HasFile);
        Assert.Null(only.FileName);
    }

    [Fact]
    public async Task Unknown_participant_is_null_not_an_error()
    {
        Assert.Null(await _service.ListAsync(UserId, Guid.NewGuid()));
        Assert.Null(await _service.CreateAsync(UserId, Guid.NewGuid(), new CreateDocumentRecordDto { DocumentType = "x" }));
    }

    [Fact]
    public async Task Update_clears_expiry_only_when_asked()
    {
        var doc = await CreateAsync();
        var dated = await _service.UpdateAsync(UserId, StarId, doc.Id,
            new UpdateDocumentRecordDto { ExpiryDate = new DateTime(2027, 1, 31) });
        Assert.Equal("2027-01-31", dated!.ExpiryDate);

        var untouched = await _service.UpdateAsync(UserId, StarId, doc.Id, new UpdateDocumentRecordDto { IsComplete = true });
        Assert.Equal("2027-01-31", untouched!.ExpiryDate);
        Assert.True(untouched.IsComplete);

        var cleared = await _service.UpdateAsync(UserId, StarId, doc.Id, new UpdateDocumentRecordDto { ClearExpiry = true });
        Assert.Null(cleared!.ExpiryDate);
    }

    [Fact]
    public async Task Document_belonging_to_another_star_is_not_reachable_through_this_one()
    {
        var otherStar = Guid.NewGuid();
        await _uow.Participants.AddAsync(new Participant { Id = otherStar, FullName = "Other", Initials = "O" });
        var theirs = (await _service.CreateAsync(UserId, otherStar, new CreateDocumentRecordDto { DocumentType = "IPP" }))!;

        Assert.Null(await _service.UpdateAsync(UserId, StarId, theirs.Id, new UpdateDocumentRecordDto { IsComplete = true }));
        Assert.Null(await _service.AttachFileAsync(UserId, StarId, theirs.Id, SampleFiles.Pdf(), "ipp.pdf"));
        Assert.False(await _service.DeleteAsync(UserId, StarId, theirs.Id));
    }

    // ── Files ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Attach_stores_the_file_and_describes_it()
    {
        var doc = await CreateAsync();

        var result = await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(100), "Intake Packet.pdf");

        Assert.True(result!.HasFile);
        Assert.Equal("Intake Packet.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(100, result.SizeBytes);
        var blob = Assert.Single(_files.Blobs.Keys);
        Assert.StartsWith($"participants/{StarId:D}/documents/{doc.Id:D}/", blob);
        Assert.EndsWith(".pdf", blob);
    }

    [Fact]
    public async Task Photos_are_accepted_and_typed_correctly()
    {
        var doc = await CreateAsync();
        var result = await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Png(), "scan.PNG");
        Assert.Equal("image/png", result!.ContentType);
        Assert.EndsWith(".png", Assert.Single(_files.Blobs.Keys));
    }

    [Theory]
    [InlineData("notes.docx")]
    [InlineData("noext")]
    public async Task Disallowed_types_are_refused_before_storage(string name)
    {
        var doc = await CreateAsync();
        await Assert.ThrowsAsync<InvalidFileException>(() =>
            _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(), name));
        Assert.Empty(_files.Blobs);
    }

    [Fact]
    public async Task A_renamed_non_pdf_is_refused_by_its_header()
    {
        var doc = await CreateAsync();
        var ex = await Assert.ThrowsAsync<InvalidFileException>(() =>
            _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Zip(), "sneaky.pdf"));
        Assert.Contains("not a PDF", ex.Message);
        Assert.Empty(_files.Blobs);
    }

    [Fact]
    public async Task Replace_saves_the_new_pointer_before_deleting_the_old_blob()
    {
        var doc = await CreateAsync();
        await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(), "v1.pdf");
        var first = Assert.Single(_files.Blobs.Keys);
        _files.Log.Clear();

        var result = await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(), "v2.pdf");

        Assert.Equal("v2.pdf", result!.FileName);
        Assert.Equal(2, _files.Log.Count);
        Assert.StartsWith("upload:", _files.Log[0]);
        Assert.Equal($"delete:{first}", _files.Log[1]);
        Assert.Single(_files.Blobs);
    }

    [Fact]
    public async Task Open_returns_null_when_storage_lost_the_blob()
    {
        var doc = await CreateAsync();
        await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(), "gone.pdf");
        _files.Blobs.Clear();

        Assert.Null(await _service.OpenFileAsync(UserId, StarId, doc.Id));
    }

    [Fact]
    public async Task Open_streams_the_stored_bytes_with_name_and_type()
    {
        var doc = await CreateAsync();
        await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(48), "packet.pdf");

        var file = await _service.OpenFileAsync(UserId, StarId, doc.Id);

        Assert.Equal("packet.pdf", file!.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        using var ms = new MemoryStream();
        await file.Content.CopyToAsync(ms);
        Assert.Equal(48, ms.Length);
    }

    [Fact]
    public async Task Remove_file_keeps_the_record_and_deletes_the_blob()
    {
        var doc = await CreateAsync();
        await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(), "a.pdf");

        var result = await _service.RemoveFileAsync(UserId, StarId, doc.Id);

        Assert.False(result!.HasFile);
        Assert.Empty(_files.Blobs);
        Assert.Single((await _service.ListAsync(UserId, StarId))!);
    }

    [Fact]
    public async Task Delete_record_removes_row_then_blob()
    {
        var doc = await CreateAsync();
        await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(), "a.pdf");

        Assert.True(await _service.DeleteAsync(UserId, StarId, doc.Id));

        Assert.Empty((await _service.ListAsync(UserId, StarId))!);
        Assert.Empty(_files.Blobs);
    }

    [Fact]
    public async Task Storage_delete_failure_never_breaks_the_database_change()
    {
        var doc = await CreateAsync();
        await _service.AttachFileAsync(UserId, StarId, doc.Id, SampleFiles.Pdf(), "a.pdf");
        _files.FailDeletes = true;

        var result = await _service.RemoveFileAsync(UserId, StarId, doc.Id);

        Assert.False(result!.HasFile);
    }
}
