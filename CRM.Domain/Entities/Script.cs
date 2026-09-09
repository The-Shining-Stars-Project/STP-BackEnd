using CRM.Domain.Common;
using CRM.Domain.Enums;

namespace CRM.Domain.Entities;

public class Script : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public ScriptType Type { get; set; } = ScriptType.Play;
    public ScriptStatus Status { get; set; } = ScriptStatus.Draft;
    public bool IsOriginal { get; set; }
    public bool IsAdapted { get; set; }
    public int? CastMin { get; set; }
    public int? CastMax { get; set; }
    public string? Duration { get; set; }
    public DateTime? LastUsed { get; set; }

    // ── PDF attachment ────────────────────────────────────────────────────────────
    // The file itself lives in Azure Blob Storage; the database holds only the pointer plus
    // what the Script Library shows without a round-trip to storage. All four are null when
    // no PDF is attached.
    //
    // PdfBlobName is the storage key ("scripts/{Id}/{guid}.pdf"). Every upload gets a fresh
    // name rather than overwriting in place, so a replaced file can never be served from a
    // stale cache and the previous blob is deleted only after the new pointer is saved.
    public string? PdfBlobName { get; set; }

    /// <summary>The name the file was uploaded under — shown in the UI and used on download.</summary>
    public string? PdfFileName { get; set; }
    public long? PdfSizeBytes { get; set; }
    public DateTime? PdfUploadedAt { get; set; }

    public ICollection<ScriptProgram> ScriptPrograms { get; set; } = new List<ScriptProgram>();
}
