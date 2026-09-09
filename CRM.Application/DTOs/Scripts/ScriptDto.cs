using System.ComponentModel.DataAnnotations;
using CRM.Domain.Enums;

namespace CRM.Application.DTOs.Scripts;

public class ScriptDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public ScriptType Type { get; set; }
    public ScriptStatus Status { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsAdapted { get; set; }
    public int? CastMin { get; set; }
    public int? CastMax { get; set; }
    public string? Duration { get; set; }
    public string? LastUsed { get; set; }
    public List<string> ProgramNames { get; set; } = new();

    // The attached PDF, described but never embedded: the bytes come from
    // GET /api/scripts/{id}/pdf. The blob name stays server-side — it is a storage key, not
    // something a client needs or should build URLs from.
    public bool HasPdf { get; set; }
    public string? PdfFileName { get; set; }
    public long? PdfSizeBytes { get; set; }
    public DateTime? PdfUploadedAt { get; set; }
}

/// <summary>
/// An attached PDF opened for download. The caller owns <see cref="Content"/> and must
/// dispose it — the API's File() result does so once the response has been written.
/// </summary>
public sealed record ScriptPdfFile(Stream Content, string FileName, long? Length);

public class CreateScriptDto
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Title { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Subtitle { get; set; }

    public ScriptType Type { get; set; } = ScriptType.Play;
    public ScriptStatus Status { get; set; } = ScriptStatus.Draft;
    public bool IsOriginal { get; set; }
    public bool IsAdapted { get; set; }

    [Range(0, 1000)]
    public int? CastMin { get; set; }

    [Range(0, 1000)]
    public int? CastMax { get; set; }

    [StringLength(50)]
    public string? Duration { get; set; }

    public List<Guid> ProgramIds { get; set; } = new();
}

public class UpdateScriptDto
{
    [StringLength(200, MinimumLength = 1)]
    public string? Title { get; set; }

    [StringLength(300)]
    public string? Subtitle { get; set; }

    public ScriptType? Type { get; set; }
    public ScriptStatus? Status { get; set; }
    public bool? IsOriginal { get; set; }
    public bool? IsAdapted { get; set; }

    [Range(0, 1000)]
    public int? CastMin { get; set; }

    [Range(0, 1000)]
    public int? CastMax { get; set; }

    [StringLength(50)]
    public string? Duration { get; set; }

    public List<Guid>? ProgramIds { get; set; }
}
