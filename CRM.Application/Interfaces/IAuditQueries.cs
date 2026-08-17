using CRM.Application.DTOs.Audit;

namespace CRM.Application.Interfaces;

/// <summary>
/// Read side of the audit log. Same split as <see cref="IStatsQueries"/> — interface in
/// Application, EF implementation in Persistence — because the audit table is the one table
/// that must never be loaded whole into memory: it only grows, and it has no purge path.
/// Paging happens in SQL.
/// </summary>
public interface IAuditQueries
{
    /// <summary>
    /// One page of audit rows, newest first, plus the total number of rows matching the
    /// filters before paging (for the X-Total-Count header).
    /// </summary>
    Task<(IReadOnlyList<AuditEventDto> Rows, int Total)> SearchAsync(AuditQuery query, CancellationToken ct = default);
}
