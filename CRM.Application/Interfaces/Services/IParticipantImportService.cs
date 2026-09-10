using CRM.Application.DTOs.Participants;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// Bulk-creates stars from a spreadsheet. Always validates every row first and reports
/// per row; with <c>commit</c> it writes all rows in one save, or none if any row has a
/// problem — the same all-or-nothing rule the original data loader enforced, because a
/// half-loaded roster is worse than an unloaded one.
/// </summary>
public interface IParticipantImportService
{
    /// <summary>Column headers of the template the UI offers for download, in order.</summary>
    IReadOnlyList<string> TemplateHeaders { get; }

    Task<ParticipantImportReportDto> ImportAsync(Guid userId, Stream csv, string fileName, bool commit, CancellationToken ct = default);
}
