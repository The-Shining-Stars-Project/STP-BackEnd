using CRM.Application.DTOs.Audit;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CRM.Persistence.Audit;

/// <summary>
/// Writes audit rows on their own DbContext, outside the caller's transaction.
///
/// Why this lives in CRM.Persistence rather than CRM.Infrastructure: the implementation
/// needs AppDbContext, and CRM.Infrastructure references neither EF Core nor CRM.Persistence
/// (it holds the JWT/PBKDF2 code and depends only on CRM.Application). This is the same
/// arrangement the codebase already uses for IStatsQueries — contract in Application, EF
/// implementation next door in Persistence.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IAuditContextAccessor _context;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IServiceScopeFactory scopes,
        IAuditContextAccessor context,
        ILogger<AuditService> logger)
    {
        _scopes = scopes;
        _context = context;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
    {
        try
        {
            // Build owns every string decision, the client-asserted forwarded address
            // included — see AuditEventFactory.BuildMetadata. Nothing about the row's contents
            // is decided here, so nothing about it is untested.
            var row = AuditEventFactory.Build(entry, _context.Current);

            // A separate DI scope means a separate AppDbContext, a separate connection, and
            // therefore a separate transaction. That is the whole point: a business
            // SaveChangesAsync that rolls back must not be able to take the record of the
            // attempt with it. Resolving through the scope factory reuses the single
            // AddDbContext registration verbatim — including the Azure SQL retry strategy and
            // 30s command timeout — instead of adding a second, singleton-lifetime
            // registration of DbContextOptions via AddDbContextFactory.
            await using var scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditEvents.Add(row);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Fail-open, deliberately. Rejecting the business write whenever the audit table
            // is unavailable would take the whole CRM down with the audit log, which is the
            // wrong trade for a ten-person nonprofit. It is a real trade, though, not a free
            // lunch: an attacker who can degrade this path gets unlogged actions, and this
            // log line is the only backstop. Never rethrow — callers are told this cannot
            // throw and do not guard it.
            _logger.LogError(ex, "Audit write failed for {Action}; event lost.", entry?.Action);
        }
    }
}
