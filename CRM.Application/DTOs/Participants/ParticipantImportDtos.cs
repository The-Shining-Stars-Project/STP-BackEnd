namespace CRM.Application.DTOs.Participants;

/// <summary>
/// The outcome of validating (and optionally committing) a Stars spreadsheet. The same shape
/// comes back from a dry run and a commit, so the UI shows one report either way; a commit
/// simply flips <see cref="Committed"/> and stamps the created rows.
/// </summary>
public class ParticipantImportReportDto
{
    public string FileName { get; set; } = string.Empty;
    /// <summary>First 16 hex chars of the file's SHA-256 — lets a second run of the same sheet be spotted in the audit log.</summary>
    public string SourceHash { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int ReadyCount { get; set; }
    public int ProblemCount { get; set; }
    /// <summary>Problems with the file as a whole (missing columns, empty file) rather than a row.</summary>
    public List<string> FileProblems { get; set; } = new();
    public bool Committed { get; set; }
    public int CreatedCount { get; set; }
    public List<ParticipantImportRowDto> Rows { get; set; } = new();
}

public class ParticipantImportRowDto
{
    /// <summary>1-based line in the spreadsheet, matching what the reviewer sees in Excel.</summary>
    public int Line { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Program { get; set; } = string.Empty;
    /// <summary>"ready", "error" or (after a commit) "created".</summary>
    public string Status { get; set; } = "ready";
    public List<string> Messages { get; set; } = new();
    /// <summary>Set after a commit.</summary>
    public Guid? ParticipantId { get; set; }
}
