using CRM.Application.DTOs.Progress;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

/// <summary>
/// Where the cohort lives this month. Every star with weekly scores on a skill gets a level
/// derived from those scores (average → threshold, exactly as the tracker's month-end
/// column does); a level a teacher has confirmed overrides the derived one. Confirmation is
/// therefore a correction step, not a prerequisite — the client's ask was "stars should
/// populate based on their score", and with confirmed-only counts the roll-up sat empty
/// all month while the data was already in.
/// </summary>
public class CohortRollUpService : ICohortRollUpService
{
    private readonly IUnitOfWork _uow;

    private readonly IProgramAccessService _access;

    public CohortRollUpService(IUnitOfWork uow, IProgramAccessService access)
    {
        _uow = uow;
        _access = access;
    }

    public async Task<CohortRollUpDto> GetRollUpAsync(Guid userId, string monthKey, Guid? programId)
    {
        var month = await LoadMonthAsync(userId, monthKey, programId);

        var subSkills = (await _uow.SubSkills.GetAllAsync()).Where(s => s.IsActive).ToList();
        var areas = (await _uow.ObjectiveAreas.GetAllAsync()).ToDictionary(a => a.Id);
        var bySkill = month.Levels
            .GroupBy(l => l.Key.SubSkillId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Value).ToList());

        var rows = subSkills
            .OrderBy(s => s.SectionNumber).ThenBy(s => s.SortOrder)
            .Select(skill =>
            {
                var list = bySkill.GetValueOrDefault(skill.Id) ?? new();
                var nov = list.Count(l => l == ProgressLevel.Novice);
                var inter = list.Count(l => l == ProgressLevel.Intermediate);
                var exp = list.Count(l => l == ProgressLevel.Expert);
                var na = list.Count(l => l == ProgressLevel.NotApplicable);
                var area = areas.GetValueOrDefault(skill.ObjectiveAreaId);

                return new CohortRollUpRowDto
                {
                    SubSkillId = skill.Id,
                    SubSkillName = skill.Name,
                    SectionNumber = skill.SectionNumber,
                    ObjectiveAreaName = area?.Name ?? "",
                    ObjectiveAreaColorHex = area?.ColorHex ?? "",
                    NoviceCount = nov,
                    IntermediateCount = inter,
                    ExpertCount = exp,
                    NotApplicableCount = na,
                    ScoredCount = nov + inter + exp,
                    MostCommonLevel = MostCommon(nov, inter, exp),
                };
            })
            .ToList();

        return new CohortRollUpDto
        {
            MonthKey = monthKey,
            ProgramId = programId,
            ProgramName = month.ProgramName,
            ParticipantCount = month.Levels
                .Where(l => IsRealLevel(l.Value))
                .Select(l => l.Key.ParticipantId)
                .Distinct()
                .Count(),
            ConfirmedCount = month.ConfirmedCount,
            Rows = rows,
        };
    }

    public async Task<IReadOnlyList<CohortStarDto>> GetStarsAtLevelAsync(
        Guid userId, string monthKey, Guid subSkillId, ProgressLevel level, Guid? programId)
    {
        // Built from the SAME per-(star, skill) map as the counts. If these two ever drift, a
        // user clicks a count of 7 and is shown 5 names, which reads as data loss rather
        // than as two different questions being asked.
        var month = await LoadMonthAsync(userId, monthKey, programId);

        var ids = month.Levels
            .Where(l => l.Key.SubSkillId == subSkillId && l.Value == level)
            .Select(l => l.Key.ParticipantId)
            .ToHashSet();
        if (ids.Count == 0) return [];

        var programNames = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);

        return month.Stars
            .Where(p => ids.Contains(p.Id))
            .OrderBy(p => p.FullName)
            .Select(p => new CohortStarDto
            {
                ParticipantId = p.Id,
                FullName = p.FullName,
                Initials = p.Initials,
                ProgramName = programNames.GetValueOrDefault(p.ProgramId, string.Empty),
            })
            .ToList();
    }

    // ── The shared derivation ─────────────────────────────────────────────────────

    private sealed record MonthLevels(
        string? ProgramName,
        IReadOnlyList<Participant> Stars,
        Dictionary<(Guid ParticipantId, Guid SubSkillId), ProgressLevel> Levels,
        int ConfirmedCount);

    /// <summary>
    /// One level per (star, skill) for the month: derived from the weekly scores, then
    /// overridden by any confirmed snapshot. Scope is the primary program only — a
    /// dual-enrolled star counts toward the program on their record, not both; the client
    /// has not asked for double counting, and the counts and the drill-down must agree.
    /// Soft-deleted stars fall out because the participant list never includes them.
    /// </summary>
    private async Task<MonthLevels> LoadMonthAsync(Guid userId, string monthKey, Guid? programId)
    {
        // Scoped to the caller's programs (#1) now that teachers can open this page: an
        // admin's "All programs" is every star, a teacher's is the stars in their programs.
        var access = await _access.ForUserAsync(userId);
        string? programName = null;
        IReadOnlyList<Participant> stars;
        if (programId is { } pid)
        {
            access.Require(pid);
            programName = (await _uow.Programs.GetByIdAsync(pid))?.Name;
            stars = await _uow.Participants.ListAsync(p => p.ProgramId == pid);
        }
        else
        {
            stars = (await _uow.Participants.GetAllAsync()).Where(p => access.CanAccess(p.ProgramId)).ToList();
        }

        var starIds = stars.Select(s => s.Id).ToHashSet();
        var levels = new Dictionary<(Guid, Guid), ProgressLevel>();
        if (starIds.Count == 0) return new MonthLevels(programName, stars, levels, 0);

        // Contains translates to IN, so a program-scoped month stays a SQL-side filter (#29).
        var entries = await _uow.WeeklyDataEntries.ListAsync(
            e => e.MonthKey == monthKey && starIds.Contains(e.ParticipantId));
        var thresholds = (await _uow.ScoreThresholds.GetAllAsync())
            .Select(t => (t.Level, t.MinAverage)).ToList();

        foreach (var g in entries.GroupBy(e => (e.ParticipantId, e.SubSkillId)))
        {
            // A skill scored only N/A this month derives to NotApplicable — a deliberate
            // "not targeted", which the N/A column reports and the scored count excludes.
            var r = ProgressLevelCalculator.Derive(g.Select(e => e.Score), thresholds);
            levels[g.Key] = r.Level;
        }

        var confirmed = await _uow.MonthlyProgressSnapshots.ListAsync(
            s => s.MonthKey == monthKey && s.IsConfirmed && starIds.Contains(s.ParticipantId));
        foreach (var snap in confirmed)
            levels[(snap.ParticipantId, snap.SubSkillId)] = snap.Level;

        return new MonthLevels(programName, stars, levels, confirmed.Count);
    }

    private static bool IsRealLevel(ProgressLevel l) =>
        l is ProgressLevel.Novice or ProgressLevel.Intermediate or ProgressLevel.Expert;

    // Mode of the three real levels; ties resolve to the higher level. "—" when nothing scored.
    private static string MostCommon(int nov, int inter, int exp)
    {
        if (nov + inter + exp == 0) return "—";
        if (exp >= inter && exp >= nov) return "Expert";
        if (inter >= nov) return "Intermediate";
        return "Novice";
    }
}
