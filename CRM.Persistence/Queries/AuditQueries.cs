using CRM.Application.DTOs.Audit;
using CRM.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CRM.Persistence.Queries;

/// <summary>
/// EF implementation of the audit read side. Read-only by construction: AsNoTracking plus a
/// projection to a DTO means nothing here can produce a tracked AuditEvent that somebody
/// could modify and save. There is no update or delete method, and none should be added —
/// the table is append-only.
/// </summary>
public class AuditQueries : IAuditQueries
{
    private readonly AppDbContext _db;

    public AuditQueries(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<AuditEventDto> Rows, int Total)> SearchAsync(
        AuditQuery query, CancellationToken ct = default)
    {
        var q = _db.AuditEvents.AsNoTracking().AsQueryable();

        if (query.From is { } from) q = q.Where(e => e.OccurredAt >= from);
        if (query.To is { } to) q = q.Where(e => e.OccurredAt <= to);
        if (query.UserId is { } userId) q = q.Where(e => e.UserId == userId);
        if (query.Succeeded is { } succeeded) q = q.Where(e => e.Succeeded == succeeded);

        if (!string.IsNullOrWhiteSpace(query.UserEmail))
        {
            // Emails are stored normalized (trimmed, lower-cased) by AuditEventFactory, so
            // normalizing the filter the same way makes this an index-friendly equality
            // rather than a case-insensitive scan.
            var email = query.UserEmail.Trim().ToLowerInvariant();
            q = q.Where(e => e.UserEmail == email);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            // Prefix match so "participant" pulls participant.view / .update / .delete in one
            // filter, which is how the viewer's action dropdown is meant to be used.
            var action = query.Action.Trim();
            q = q.Where(e => e.Action.StartsWith(action));
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            q = q.Where(e => e.EntityType == entityType);
        }

        // Counted and paged in SQL. The participants endpoint loads its whole list and pages
        // in memory, which is fine for ~100 children and fatal here: this table only grows
        // and has no purge path.
        var total = await q.CountAsync(ct);

        var rows = await q
            .OrderByDescending(e => e.OccurredAt)
            // Tie-break on the primary key: several rows can share a timestamp, and without a
            // stable secondary sort the same row can appear on two pages or on neither.
            .ThenByDescending(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(e => new AuditEventDto
            {
                Id = e.Id,
                OccurredAt = e.OccurredAt,
                UserId = e.UserId,
                UserEmail = e.UserEmail,
                UserRole = e.UserRole,
                Action = e.Action,
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                Summary = e.Summary,
                IpAddress = e.IpAddress,
                UserAgent = e.UserAgent,
                Succeeded = e.Succeeded,
                Metadata = e.Metadata,
            })
            .ToListAsync(ct);

        return (rows, total);
    }
}
