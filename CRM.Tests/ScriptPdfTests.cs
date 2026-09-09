using System.Text;
using CRM.Application.Exceptions;
using CRM.Application.Interfaces;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Tests;

/// <summary>
/// The rules for attaching a PDF to a script, driven through the real <see cref="ScriptService"/>
/// over an in-memory unit of work and an in-memory <see cref="IFileStorage"/>. What matters
/// here is the contract between the database pointer and the blob: which one is written
/// first, what gets cleaned up, and what is refused before storage is touched at all.
/// </summary>
public class ScriptPdfTests
{
    private static readonly Guid ScriptId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeFileStorage _files = new();
    private readonly ScriptService _service;

    public ScriptPdfTests()
    {
        _service = new ScriptService(_uow, _files, NullLogger<ScriptService>.Instance);
        _uow.Scripts.AddAsync(new Script { Id = ScriptId, Title = "The Magic Garden" }).GetAwaiter().GetResult();
    }

    private static MemoryStream Pdf(string body = "hello") =>
        new(Encoding.ASCII.GetBytes("%PDF-1.7\n" + body + "\n%%EOF"));

    // ── Attach ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Attach_stores_the_blob_under_the_script_and_records_the_pointer()
    {
        using var content = Pdf();

        var dto = await _service.AttachPdfAsync(ScriptId, content, "Magic Garden.pdf");

        Assert.NotNull(dto);
        Assert.True(dto.HasPdf);
        Assert.Equal("Magic Garden.pdf", dto.PdfFileName);
        Assert.Equal(content.Length, dto.PdfSizeBytes);
        Assert.NotNull(dto.PdfUploadedAt);

        var blobName = Assert.Single(_files.Blobs.Keys);
        Assert.StartsWith($"scripts/{ScriptId:D}/", blobName);
        Assert.EndsWith(".pdf", blobName);
        Assert.Equal("application/pdf", _files.ContentTypes[blobName]);
        Assert.Equal(content.ToArray(), _files.Blobs[blobName]);

        var script = await _uow.Scripts.GetByIdAsync(ScriptId);
        Assert.Equal(blobName, script!.PdfBlobName);
    }

    [Fact]
    public async Task Attach_uploads_the_whole_file_even_though_the_header_check_read_from_it()
    {
        using var content = Pdf(new string('x', 10_000));

        await _service.AttachPdfAsync(ScriptId, content, "big.pdf");

        Assert.Equal(content.Length, _files.Blobs.Values.Single().Length);
    }

    [Fact]
    public async Task Replacing_a_pdf_deletes_the_old_blob_only_after_the_new_pointer_is_saved()
    {
        await _service.AttachPdfAsync(ScriptId, Pdf("first"), "v1.pdf");
        var firstBlob = _files.Blobs.Keys.Single();

        var dto = await _service.AttachPdfAsync(ScriptId, Pdf("second"), "v2.pdf");

        var secondBlob = Assert.Single(_files.Blobs.Keys);
        Assert.NotEqual(firstBlob, secondBlob);
        Assert.Contains(firstBlob, _files.Deleted);
        Assert.Equal("v2.pdf", dto!.PdfFileName);
        // Order of operations: the upload of v2 happened before v1 was deleted.
        Assert.True(_files.Log.IndexOf($"upload:{secondBlob}") < _files.Log.IndexOf($"delete:{firstBlob}"));
    }

    [Fact]
    public async Task Attach_returns_null_for_an_unknown_script_without_touching_storage()
    {
        var dto = await _service.AttachPdfAsync(Guid.NewGuid(), Pdf(), "x.pdf");

        Assert.Null(dto);
        Assert.Empty(_files.Log);
    }

    // ── Validation — all refused before storage is touched ────────────────────────

    [Theory]
    [InlineData("script.docx")]
    [InlineData("script")]
    [InlineData("script.pdf.exe")]
    public async Task Refuses_files_not_named_pdf(string fileName)
    {
        var ex = await Assert.ThrowsAsync<InvalidFileException>(
            () => _service.AttachPdfAsync(ScriptId, Pdf(), fileName));

        Assert.Contains("Only PDF files", ex.Message);
        Assert.Empty(_files.Log);
        Assert.Null((await _uow.Scripts.GetByIdAsync(ScriptId))!.PdfBlobName);
    }

    [Fact]
    public async Task Refuses_a_file_named_pdf_whose_bytes_are_not_a_pdf()
    {
        // A Word document renamed to .pdf: the browser reports application/pdf, the name ends
        // in .pdf, and only the header gives it away.
        using var notPdf = new MemoryStream(Encoding.ASCII.GetBytes("PK\u0003\u0004 this is a zip"));

        var ex = await Assert.ThrowsAsync<InvalidFileException>(
            () => _service.AttachPdfAsync(ScriptId, notPdf, "renamed.pdf"));

        Assert.Contains("not a PDF", ex.Message);
        Assert.Empty(_files.Log);
    }

    [Fact]
    public async Task Refuses_an_empty_file()
    {
        using var empty = new MemoryStream();

        var ex = await Assert.ThrowsAsync<InvalidFileException>(
            () => _service.AttachPdfAsync(ScriptId, empty, "empty.pdf"));

        Assert.Contains("empty", ex.Message);
        Assert.Empty(_files.Log);
    }

    [Fact]
    public async Task Refuses_a_file_over_the_size_limit()
    {
        // Seekable, reports Length, but never allocates the 25 MB — the check must not need to.
        using var huge = new ZeroStream(ScriptService.MaxPdfBytes + 1);

        var ex = await Assert.ThrowsAsync<InvalidFileException>(
            () => _service.AttachPdfAsync(ScriptId, huge, "huge.pdf"));

        Assert.Contains("limit is 25 MB", ex.Message);
        Assert.Empty(_files.Log);
    }

    [Fact]
    public async Task Accepts_a_file_exactly_at_the_size_limit()
    {
        using var atLimit = new ZeroStream(ScriptService.MaxPdfBytes, pdfHeader: true);

        var dto = await _service.AttachPdfAsync(ScriptId, atLimit, "limit.pdf");

        Assert.Equal(ScriptService.MaxPdfBytes, dto!.PdfSizeBytes);
    }

    [Theory]
    [InlineData("C:\\Users\\rachel\\Desktop\\Cinderella.PDF", "Cinderella.PDF")]
    [InlineData("/home/rachel/Cinderella.pdf", "Cinderella.pdf")]
    [InlineData("  Under the Sea.pdf  ", "Under the Sea.pdf")]
    [InlineData("bad\r\nname.pdf", "badname.pdf")]
    public async Task Stores_only_the_file_name_with_paths_and_control_characters_removed(string given, string stored)
    {
        var dto = await _service.AttachPdfAsync(ScriptId, Pdf(), given);

        Assert.Equal(stored, dto!.PdfFileName);
    }

    [Fact]
    public async Task Truncates_an_absurdly_long_name_but_keeps_the_extension()
    {
        var given = new string('a', 400) + ".pdf";

        var dto = await _service.AttachPdfAsync(ScriptId, Pdf(), given);

        Assert.Equal(255, dto!.PdfFileName!.Length);
        Assert.EndsWith(".pdf", dto.PdfFileName);
    }

    [Fact]
    public async Task Unconfigured_storage_refuses_the_upload_and_leaves_the_script_untouched()
    {
        var service = new ScriptService(_uow, new UnconfiguredFileStorage(), NullLogger<ScriptService>.Instance);

        await Assert.ThrowsAsync<StorageNotConfiguredException>(
            () => service.AttachPdfAsync(ScriptId, Pdf(), "x.pdf"));

        Assert.Null((await _uow.Scripts.GetByIdAsync(ScriptId))!.PdfBlobName);
    }

    // ── Open ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_returns_the_stored_bytes_and_the_uploaded_name()
    {
        using var content = Pdf("act one");
        await _service.AttachPdfAsync(ScriptId, content, "Act One.pdf");

        var pdf = await _service.OpenPdfAsync(ScriptId);

        Assert.NotNull(pdf);
        Assert.Equal("Act One.pdf", pdf.FileName);
        Assert.Equal(content.Length, pdf.Length);
        using var read = new MemoryStream();
        await pdf.Content.CopyToAsync(read);
        Assert.Equal(content.ToArray(), read.ToArray());
    }

    [Fact]
    public async Task Open_returns_null_when_the_script_has_no_pdf()
    {
        Assert.Null(await _service.OpenPdfAsync(ScriptId));
        Assert.Null(await _service.OpenPdfAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Open_returns_null_when_the_blob_has_vanished_behind_the_pointer()
    {
        await _service.AttachPdfAsync(ScriptId, Pdf(), "x.pdf");
        _files.Blobs.Clear(); // deleted in the portal

        Assert.Null(await _service.OpenPdfAsync(ScriptId));
    }

    // ── Remove ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_clears_the_pointer_and_deletes_the_blob()
    {
        await _service.AttachPdfAsync(ScriptId, Pdf(), "x.pdf");
        var blobName = _files.Blobs.Keys.Single();

        var dto = await _service.RemovePdfAsync(ScriptId);

        Assert.False(dto!.HasPdf);
        Assert.Null(dto.PdfFileName);
        Assert.Null(dto.PdfSizeBytes);
        Assert.Null(dto.PdfUploadedAt);
        Assert.Empty(_files.Blobs);
        Assert.Contains(blobName, _files.Deleted);
    }

    [Fact]
    public async Task Remove_is_idempotent_and_does_not_touch_storage_when_nothing_is_attached()
    {
        var dto = await _service.RemovePdfAsync(ScriptId);

        Assert.NotNull(dto);
        Assert.False(dto.HasPdf);
        Assert.Empty(_files.Log);
    }

    [Fact]
    public async Task Remove_still_clears_the_pointer_when_the_blob_delete_fails()
    {
        await _service.AttachPdfAsync(ScriptId, Pdf(), "x.pdf");
        _files.FailDeletes = true;

        var dto = await _service.RemovePdfAsync(ScriptId);

        // The database is the source of truth; an undeleted blob is an orphan, not an error.
        Assert.False(dto!.HasPdf);
        Assert.Null((await _uow.Scripts.GetByIdAsync(ScriptId))!.PdfBlobName);
    }

    // ── Read model ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_and_detail_report_the_attachment_without_the_blob_name()
    {
        await _service.AttachPdfAsync(ScriptId, Pdf(), "x.pdf");

        var listed = Assert.Single(await _service.GetAllAsync());
        Assert.True(listed.HasPdf);
        Assert.Equal("x.pdf", listed.PdfFileName);

        var detail = await _service.GetByIdAsync(ScriptId);
        Assert.True(detail!.HasPdf);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────────

    /// <summary>Dictionary-backed <see cref="IFileStorage"/> that records every call in order.</summary>
    private sealed class FakeFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Blobs { get; } = new();
        public Dictionary<string, string> ContentTypes { get; } = new();
        public List<string> Deleted { get; } = new();
        public List<string> Log { get; } = new();
        public bool FailDeletes { get; set; }

        public bool IsConfigured => true;

        public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken ct = default)
        {
            Log.Add($"upload:{blobName}");
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Blobs[blobName] = buffer.ToArray();
            ContentTypes[blobName] = contentType;
        }

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken ct = default)
        {
            Log.Add($"open:{blobName}");
            return Task.FromResult<Stream?>(
                Blobs.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes) : null);
        }

        public Task DeleteAsync(string blobName, CancellationToken ct = default)
        {
            Log.Add($"delete:{blobName}");
            if (FailDeletes) throw new IOException("storage is down");
            Blobs.Remove(blobName);
            Deleted.Add(blobName);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A seekable stream of a given length that reads as zeros (optionally after a PDF header)
    /// without ever allocating that many bytes — for the size-limit tests.
    /// </summary>
    private sealed class ZeroStream : Stream
    {
        private static readonly byte[] Header = "%PDF-1.7\n"u8.ToArray();
        private readonly long _length;
        private readonly bool _pdfHeader;
        private long _position;

        public ZeroStream(long length, bool pdfHeader = false)
        {
            _length = length;
            _pdfHeader = pdfHeader;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => _position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _length - _position;
            if (remaining <= 0) return 0;
            var n = (int)Math.Min(count, remaining);
            for (var i = 0; i < n; i++)
            {
                var abs = _position + i;
                buffer[offset + i] = _pdfHeader && abs < Header.Length ? Header[abs] : (byte)0;
            }
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _length + offset,
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
