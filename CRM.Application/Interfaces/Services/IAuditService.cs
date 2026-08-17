using CRM.Application.DTOs.Audit;

namespace CRM.Application.Interfaces.Services;

/// <summary>
/// Writes append-only audit records. Two contracts the implementation must keep, because
/// every caller relies on them:
///
/// 1. It never throws. A failed audit write must not turn a working request into a 500 —
///    the implementation swallows and logs. The accepted consequence is that this is
///    fail-open: if the database is unavailable the event is lost rather than the request
///    being rejected.
/// 2. It never participates in the caller's transaction. Audit rows are written on their own
///    DbContext and connection, so a business save that rolls back cannot discard the audit
///    row describing the attempt.
/// </summary>
public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct = default);
}
