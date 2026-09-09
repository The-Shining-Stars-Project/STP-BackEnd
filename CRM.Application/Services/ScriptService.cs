using CRM.Application.DTOs.Scripts;
using CRM.Application.Exceptions;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CRM.Application.Services;

public class ScriptService : IScriptService
{
    /// <summary>
    /// Upload ceiling. A full musical script with sheet music runs a few megabytes; 25 MB
    /// leaves generous room while keeping a single request well under Kestrel's default body
    /// limit. Enforced here (the rule) and again by the controller's request-size attributes
    /// (so an oversize body is refused before it is buffered).
    /// </summary>
    public const long MaxPdfBytes = 25L * 1024 * 1024;

    private const string PdfContentType = "application/pdf";
    private const int MaxFileNameLength = 255;

    // Every PDF begins with "%PDF-" (PDF 1.7 spec §7.5.2). Checking it is what stops a Word
    // document renamed to .pdf from being stored and then served as application/pdf.
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    private readonly IUnitOfWork _uow;
    private readonly IFileStorage _files;
    private readonly ILogger<ScriptService> _logger;

    public ScriptService(IUnitOfWork uow, IFileStorage files, ILogger<ScriptService> logger)
    {
        _uow = uow;
        _files = files;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScriptDto>> GetAllAsync()
    {
        var scripts = await _uow.Scripts.GetAllAsync();
        var namesByScript = await BuildProgramNameMapAsync();
        return scripts.Select(s => ToDto(s, namesByScript.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<ScriptDto?> GetByIdAsync(Guid id)
    {
        var script = await _uow.Scripts.GetByIdAsync(id);
        if (script is null) return null;
        var namesByScript = await BuildProgramNameMapAsync();
        return ToDto(script, namesByScript.GetValueOrDefault(id));
    }

    public async Task<ScriptDto> CreateAsync(CreateScriptDto dto)
    {
        var script = new Script
        {
            Title = dto.Title,
            Subtitle = dto.Subtitle,
            Type = dto.Type,
            Status = dto.Status,
            IsOriginal = dto.IsOriginal,
            IsAdapted = dto.IsAdapted,
            CastMin = dto.CastMin,
            CastMax = dto.CastMax,
            Duration = dto.Duration,
        };

        await _uow.Scripts.AddAsync(script);
        await _uow.SaveChangesAsync();

        await _uow.ReplaceScriptProgramsAsync(script.Id, dto.ProgramIds);
        await _uow.SaveChangesAsync();

        return await GetByIdAsync(script.Id) ?? ToDto(script, null);
    }

    public async Task<ScriptDto?> UpdateAsync(Guid id, UpdateScriptDto dto)
    {
        var script = await _uow.Scripts.GetByIdAsync(id);
        if (script is null) return null;

        // PUT is a full edit from the Script Library form: apply every provided field.
        if (dto.Title is not null) script.Title = dto.Title;
        script.Subtitle = dto.Subtitle;
        if (dto.Type.HasValue) script.Type = dto.Type.Value;
        if (dto.Status.HasValue) script.Status = dto.Status.Value;
        if (dto.IsOriginal.HasValue) script.IsOriginal = dto.IsOriginal.Value;
        if (dto.IsAdapted.HasValue) script.IsAdapted = dto.IsAdapted.Value;
        script.CastMin = dto.CastMin;
        script.CastMax = dto.CastMax;
        script.Duration = dto.Duration;

        await _uow.Scripts.UpdateAsync(script);

        if (dto.ProgramIds is not null)
            await _uow.ReplaceScriptProgramsAsync(id, dto.ProgramIds);

        await _uow.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    // ── PDF attachment ────────────────────────────────────────────────────────────

    public async Task<ScriptDto?> AttachPdfAsync(Guid id, Stream content, string fileName, CancellationToken ct = default)
    {
        var script = await _uow.Scripts.GetByIdAsync(id);
        if (script is null) return null;

        var safeName = ValidatePdf(content, fileName, out var length);

        // A fresh blob name per upload (never overwrite in place): the old file stays intact
        // until the new pointer is durable, and nothing can serve a half-written replacement.
        var blobName = $"scripts/{id:D}/{Guid.NewGuid():N}.pdf";
        await _files.UploadAsync(blobName, content, PdfContentType, ct);

        var previous = script.PdfBlobName;
        script.PdfBlobName = blobName;
        script.PdfFileName = safeName;
        script.PdfSizeBytes = length;
        script.PdfUploadedAt = DateTime.UtcNow;
        script.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _uow.Scripts.UpdateAsync(script);
            await _uow.SaveChangesAsync();
        }
        catch
        {
            // The pointer never landed, so the blob just written is an orphan. Remove it rather
            // than let storage drift away from the database; the original exception still
            // propagates.
            await TryDeleteAsync(blobName);
            throw;
        }

        // Only now, with the new pointer saved: a failure here leaves an orphan blob (harmless,
        // costs a fraction of a cent) rather than a script pointing at nothing.
        if (previous is not null && previous != blobName)
            await TryDeleteAsync(previous);

        return await GetByIdAsync(id);
    }

    public async Task<ScriptPdfFile?> OpenPdfAsync(Guid id, CancellationToken ct = default)
    {
        var script = await _uow.Scripts.GetByIdAsync(id);
        if (script?.PdfBlobName is null) return null;

        var stream = await _files.OpenReadAsync(script.PdfBlobName, ct);
        if (stream is null)
        {
            // The row says there is a file and storage says there is not — somebody deleted
            // the blob in the portal, or a restore put the database ahead of storage. Report it
            // as missing (404) rather than failing, and leave a trail for whoever investigates.
            _logger.LogWarning(
                "Script {ScriptId} points at blob {BlobName}, which no longer exists in storage.",
                id, script.PdfBlobName);
            return null;
        }

        return new ScriptPdfFile(stream, script.PdfFileName ?? "script.pdf", script.PdfSizeBytes);
    }

    public async Task<ScriptDto?> RemovePdfAsync(Guid id, CancellationToken ct = default)
    {
        var script = await _uow.Scripts.GetByIdAsync(id);
        if (script is null) return null;

        var blobName = script.PdfBlobName;
        if (blobName is null) return await GetByIdAsync(id);

        script.PdfBlobName = null;
        script.PdfFileName = null;
        script.PdfSizeBytes = null;
        script.PdfUploadedAt = null;
        script.UpdatedAt = DateTime.UtcNow;

        await _uow.Scripts.UpdateAsync(script);
        await _uow.SaveChangesAsync();

        // Pointer first, blob second — the failure mode of the other order is a script that
        // claims a PDF it can no longer serve.
        await TryDeleteAsync(blobName);

        return await GetByIdAsync(id);
    }

    /// <summary>
    /// Refuses anything that is not a PDF, by file name and by header, and returns the
    /// sanitised name to store. Rewinds <paramref name="content"/> to the start so the upload
    /// that follows sends the whole file.
    /// </summary>
    private static string ValidatePdf(Stream content, string fileName, out long length)
    {
        if (!content.CanSeek)
            throw new ArgumentException("PDF content must be a seekable stream.", nameof(content));

        length = content.Length;
        if (length <= 0)
            throw new InvalidFileException("The uploaded file is empty.");
        if (length > MaxPdfBytes)
            throw new InvalidFileException(
                $"That PDF is {length / (1024d * 1024):0.#} MB; the limit is {MaxPdfBytes / (1024 * 1024)} MB.");

        var name = SanitizeFileName(fileName);
        if (name.Length == 0)
            throw new InvalidFileException("The uploaded file has no name.");
        if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidFileException("Only PDF files can be attached to a script.");
        if (name.Length > MaxFileNameLength)
            name = name[..(MaxFileNameLength - 4)] + ".pdf";

        content.Position = 0;
        Span<byte> header = stackalloc byte[PdfMagic.Length];
        var read = content.ReadAtLeast(header, PdfMagic.Length, throwOnEndOfStream: false);
        content.Position = 0;
        if (read < PdfMagic.Length || !header.SequenceEqual(PdfMagic))
            throw new InvalidFileException(
                "That file is not a PDF — it is named .pdf but does not start with a PDF header.");

        return name;
    }

    /// <summary>
    /// Keeps only the final path segment (browsers send a bare name; older clients and some
    /// tools send a full path, with either separator) and drops control characters, which
    /// have no business in a file name and would corrupt a Content-Disposition header.
    /// </summary>
    private static string SanitizeFileName(string? fileName)
    {
        var raw = (fileName ?? string.Empty).Trim();
        var cut = raw.LastIndexOfAny(['/', '\\']);
        if (cut >= 0) raw = raw[(cut + 1)..];
        return new string(raw.Where(c => !char.IsControl(c)).ToArray()).Trim();
    }

    private async Task TryDeleteAsync(string blobName)
    {
        try
        {
            await _files.DeleteAsync(blobName);
        }
        catch (Exception ex)
        {
            // Best-effort by design: the database is already correct, and an undeleted blob is
            // an orphan to sweep, not a bug a user can see.
            _logger.LogWarning(ex, "Could not delete blob {BlobName}; it is now an orphan.", blobName);
        }
    }

    /// <summary>scriptId → the display names of the programs it's linked to.</summary>
    private async Task<Dictionary<Guid, List<string>>> BuildProgramNameMapAsync()
    {
        var links = await _uow.GetScriptProgramsAsync();
        if (links.Count == 0) return new();

        var programs = await _uow.Programs.GetAllAsync();
        var nameById = programs.ToDictionary(p => p.Id, p => p.Name);

        return links
            .GroupBy(l => l.ScriptId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => nameById.GetValueOrDefault(l.ProgramId))
                      .Where(n => n is not null)
                      .Select(n => n!)
                      .ToList());
    }

    private static ScriptDto ToDto(Script s, List<string>? programNames) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Subtitle = s.Subtitle,
        Type = s.Type,
        Status = s.Status,
        IsOriginal = s.IsOriginal,
        IsAdapted = s.IsAdapted,
        CastMin = s.CastMin,
        CastMax = s.CastMax,
        Duration = s.Duration,
        LastUsed = s.LastUsed?.ToString("yyyy-MM-dd"),
        ProgramNames = programNames ?? new(),
        HasPdf = s.PdfBlobName is not null,
        PdfFileName = s.PdfFileName,
        PdfSizeBytes = s.PdfSizeBytes,
        PdfUploadedAt = s.PdfUploadedAt,
    };
}
