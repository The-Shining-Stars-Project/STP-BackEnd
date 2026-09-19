using CRM.Application.Interfaces;
using CRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CRM.Persistence.Queries;

/// <summary>
/// EF implementation of the aggregate queries (#11). Every method translates to a single
/// grouped SQL statement — nothing here materializes an unbounded table.
/// </summary>
public class StatsQueries : IStatsQueries
{
    private readonly AppDbContext _db;

    public StatsQueries(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ParticipantAttendanceAgg>> GetParticipantAttendanceAsync(CancellationToken ct = default)
    {
        var rows = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.Status != AttendanceStatus.Unmarked)
            .GroupBy(r => new { r.ParticipantId, r.Participant.ProgramId })
            .Select(g => new
            {
                g.Key.ParticipantId,
                g.Key.ProgramId,
                Present = g.Count(r => r.Status == AttendanceStatus.Present),
                Absent = g.Count(r => r.Status == AttendanceStatus.Absent),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new ParticipantAttendanceAgg(r.ParticipantId, r.ProgramId, r.Present, r.Absent))
            .ToList();
    }

    public async Task<AttendanceStatusTotals> GetAttendanceStatusTotalsAsync(CancellationToken ct = default)
    {
        var counts = await _db.AttendanceRecords.AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var sessionCount = await _db.Sessions.AsNoTracking().CountAsync(ct);

        int Of(AttendanceStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        return new AttendanceStatusTotals(
            Of(AttendanceStatus.Present),
            Of(AttendanceStatus.Absent),
            Of(AttendanceStatus.Unmarked),
            sessionCount,
            Of(AttendanceStatus.Rescheduled),
            Of(AttendanceStatus.NotScheduled));
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetSessionCountByProgramAsync(CancellationToken ct = default)
    {
        var rows = await _db.Sessions.AsNoTracking()
            .GroupBy(s => s.ProgramId)
            .Select(g => new { ProgramId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.ProgramId, r => r.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, NextSessionStub>> GetNextSessionByProgramAsync(
        DateTime fromUtc, CancellationToken ct = default)
    {
        var rows = await _db.Sessions.AsNoTracking()
            .Where(s => s.Date >= fromUtc)
            .GroupBy(s => s.ProgramId)
            .Select(g => g.OrderBy(s => s.Date)
                          .Select(s => new { s.ProgramId, s.Date, s.Room })
                          .First())
            .ToListAsync(ct);

        return rows.ToDictionary(
            r => r.ProgramId,
            r => new NextSessionStub(r.ProgramId, r.Date, r.Room));
    }
}
