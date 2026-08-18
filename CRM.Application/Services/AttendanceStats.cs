using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

/// <summary>
/// CLASS attendance only. Production and event attendance lives in EventAttendanceRecord and
/// is tracked separately at the client's request (Aug 2026) — a Star missing an optional
/// performance must not move the percentage that measures their class participation. The two
/// are different CLR types precisely so they cannot be summed here by accident; if you find
/// yourself wanting to widen these signatures to accept both, that is the design saying no.
///
/// Computes real attendance percentages from <see cref="AttendanceRecord"/>s (#8).
/// The denormalized <c>Participant.AttendancePct</c> column was only ever written by the
/// dev seeder and is never recomputed, so it must not be used for display — it reads 0 for
/// every API-created participant and a stale constant for seeded ones.
/// </summary>
public static class AttendanceStats
{
    /// <summary>
    /// Attendance % for one participant = Present / (Present + Absent) across marked records.
    /// Unmarked records are ignored. Returns 0 when there are no marked records.
    /// </summary>
    public static int PercentFor(IEnumerable<AttendanceRecord> records)
    {
        var present = 0;
        var marked = 0;
        foreach (var r in records)
        {
            if (r.Status == AttendanceStatus.Present) { present++; marked++; }
            else if (r.Status == AttendanceStatus.Absent) { marked++; }
        }
        return marked == 0 ? 0 : (int)Math.Round(100.0 * present / marked);
    }

    /// <summary>Builds participantId → attendance % from a flat set of records.</summary>
    public static Dictionary<Guid, int> PercentByParticipant(IEnumerable<AttendanceRecord> records) =>
        records.GroupBy(r => r.ParticipantId)
               .ToDictionary(g => g.Key, g => PercentFor(g));

    /// <summary>Percentage from pre-aggregated counts (#11) — same rounding as PercentFor.</summary>
    public static int Percent(int present, int marked) =>
        marked == 0 ? 0 : (int)Math.Round(100.0 * present / marked);

    /// <summary>Builds participantId → attendance % from SQL-side aggregates (#11).</summary>
    public static Dictionary<Guid, int> PercentByParticipant(IEnumerable<Interfaces.ParticipantAttendanceAgg> aggs) =>
        aggs.ToDictionary(
            a => a.ParticipantId,
            a => Percent(a.PresentCount, a.PresentCount + a.AbsentCount));
}
