using CRM.Application.DTOs.Progress;
using CRM.Application.Interfaces;
using CRM.Application.Interfaces.Services;
using CRM.Domain.Enums;

namespace CRM.Application.Services;

public class CohortRollUpService : ICohortRollUpService
{
    private readonly IUnitOfWork _uow;

    public CohortRollUpService(IUnitOfWork uow) => _uow = uow;

    public async Task<CohortRollUpDto> GetRollUpAsync(string monthKey, Guid? programId)
    {
        // Only confirmed snapshots count toward the roll-up. When scoped to a program,
        // resolve the participant ids first so the snapshot filter runs in SQL
        // (Contains translates to IN) instead of loading the whole month and filtering
        // in memory (#29).
        string? programName = null;
        IReadOnlyList<Domain.Entities.MonthlyProgressSnapshot> snaps;
        if (programId is { } pid)
        {
            var program = await _uow.Programs.GetByIdAsync(pid);
            programName = program?.Name;
            var inProgram = (await _uow.Participants.ListAsync(p => p.ProgramId == pid))
                .Select(p => p.Id)
                .ToHashSet();
            snaps = await _uow.MonthlyProgressSnapshots.ListAsync(
                s => s.MonthKey == monthKey && s.IsConfirmed && inProgram.Contains(s.ParticipantId));
        }
        else
        {
            snaps = await _uow.MonthlyProgressSnapshots.ListAsync(
                s => s.MonthKey == monthKey && s.IsConfirmed);
        }

        var subSkills = (await _uow.SubSkills.GetAllAsync()).Where(s => s.IsActive).ToList();
        var areas = (await _uow.ObjectiveAreas.GetAllAsync()).ToDictionary(a => a.Id);
        var bySkill = snaps.GroupBy(s => s.SubSkillId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = subSkills
            .OrderBy(s => s.SectionNumber).ThenBy(s => s.SortOrder)
            .Select(skill =>
            {
                var list = bySkill.GetValueOrDefault(skill.Id) ?? new();
                var nov = list.Count(s => s.Level == ProgressLevel.Novice);
                var inter = list.Count(s => s.Level == ProgressLevel.Intermediate);
                var exp = list.Count(s => s.Level == ProgressLevel.Expert);
                var na = list.Count(s => s.Level == ProgressLevel.NotApplicable);
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
            ProgramName = programName,
            ParticipantCount = snaps.Select(s => s.ParticipantId).Distinct().Count(),
            Rows = rows,
        };
    }

    // Mode of the three real levels; ties resolve to the higher level. "—" when nothing scored.
    public async Task<IReadOnlyList<CohortStarDto>> GetStarsAtLevelAsync(
        string monthKey, Guid subSkillId, ProgressLevel level, Guid? programId)
    {
        // IsConfirmed mirrors GetRollUpAsync exactly. If these two rules ever drift, a user
        // clicks a count of 7 and is shown 5 names, which reads as data loss rather than as
        // two different questions being asked.
        var snaps = await _uow.MonthlyProgressSnapshots.ListAsync(s =>
            s.MonthKey == monthKey && s.SubSkillId == subSkillId &&
            s.IsConfirmed && s.Level == level);

        if (snaps.Count == 0) return [];

        var ids = snaps.Select(s => s.ParticipantId).ToHashSet();
        var stars = await _uow.Participants.ListAsync(p => ids.Contains(p.Id));

        // Scope EXACTLY as GetRollUpAsync does — primary ProgramId only. It is tempting to
        // include SecondaryProgramId so a dual-enrolled Star appears under both programs, and
        // arguably it should, but the count this list hangs off does not: clicking a 4 and
        // being shown 6 names reads as a bug in the number, not as a more generous filter.
        // Whether dual-enrolled Stars should count toward both programs is a real question for
        // the client; when it is answered, change BOTH of these together.
        if (programId is { } pid)
            stars = stars.Where(p => p.ProgramId == pid).ToList();

        var programNames = (await _uow.Programs.GetAllAsync()).ToDictionary(p => p.Id, p => p.Name);

        return stars
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

    private static string MostCommon(int nov, int inter, int exp)
    {
        if (nov + inter + exp == 0) return "—";
        if (exp >= inter && exp >= nov) return "Expert";
        if (inter >= nov) return "Intermediate";
        return "Novice";
    }
}
