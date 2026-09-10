using CRM.Application.Exceptions;
using CRM.Application.Services;
using CRM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CRM.Tests;

/// <summary>The paperwork behind a staff onboarding checklist item.</summary>
public class StaffDocumentTests
{
    private static readonly Guid StaffId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeFileStorage _files = new();
    private readonly StaffService _service;

    public StaffDocumentTests()
    {
        _service = new StaffService(_uow, new FakeOrgClock(), _files, NullLogger<StaffService>.Instance);
        _uow.Staff.AddAsync(new StaffMember { Id = StaffId, FullName = "New Hire", Initials = "NH" }).GetAwaiter().GetResult();
        _uow.OnboardingItems.AddAsync(new OnboardingItem
        {
            Id = ItemId, StaffMemberId = StaffId, Section = "Documents", Label = "I-9 / ID verification",
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Attach_describes_the_file_on_the_checklist_item()
    {
        var detail = await _service.AttachOnboardingFileAsync(StaffId, ItemId, SampleFiles.Jpeg(), "i9-photo.jpeg");

        var item = Assert.Single(detail!.OnboardingItems);
        Assert.True(item.HasFile);
        Assert.Equal("i9-photo.jpeg", item.FileName);
        Assert.Equal("image/jpeg", item.ContentType);
        var blob = Assert.Single(_files.Blobs.Keys);
        Assert.StartsWith($"staff/{StaffId:D}/onboarding/{ItemId:D}/", blob);
        Assert.EndsWith(".jpg", blob);
    }

    [Fact]
    public async Task Item_from_a_different_staff_member_is_not_reachable()
    {
        var other = Guid.NewGuid();
        await _uow.Staff.AddAsync(new StaffMember { Id = other, FullName = "Other", Initials = "O" });

        Assert.Null(await _service.AttachOnboardingFileAsync(other, ItemId, SampleFiles.Jpeg(), "x.jpg"));
        Assert.Null(await _service.OpenOnboardingFileAsync(other, ItemId));
        Assert.Empty(_files.Blobs);
    }

    [Fact]
    public async Task Wrong_type_is_refused()
    {
        await Assert.ThrowsAsync<InvalidFileException>(() =>
            _service.AttachOnboardingFileAsync(StaffId, ItemId, SampleFiles.Jpeg(), "resume.docx"));
        Assert.Empty(_files.Blobs);
    }

    [Fact]
    public async Task Open_then_remove_round_trip()
    {
        await _service.AttachOnboardingFileAsync(StaffId, ItemId, SampleFiles.Jpeg(), "i9.jpg");

        var file = await _service.OpenOnboardingFileAsync(StaffId, ItemId);
        Assert.Equal("i9.jpg", file!.FileName);
        Assert.Equal("image/jpeg", file.ContentType);
        file.Content.Dispose();

        var detail = await _service.RemoveOnboardingFileAsync(StaffId, ItemId);
        Assert.False(Assert.Single(detail!.OnboardingItems).HasFile);
        Assert.Empty(_files.Blobs);
        Assert.Null(await _service.OpenOnboardingFileAsync(StaffId, ItemId));
    }

    [Fact]
    public async Task Replacing_deletes_the_previous_blob_after_the_new_pointer_is_saved()
    {
        await _service.AttachOnboardingFileAsync(StaffId, ItemId, SampleFiles.Pdf(), "v1.pdf");
        var first = Assert.Single(_files.Blobs.Keys);
        _files.Log.Clear();

        await _service.AttachOnboardingFileAsync(StaffId, ItemId, SampleFiles.Pdf(), "v2.pdf");

        Assert.StartsWith("upload:", _files.Log[0]);
        Assert.Equal($"delete:{first}", _files.Log[1]);
        Assert.Single(_files.Blobs);
    }
}
