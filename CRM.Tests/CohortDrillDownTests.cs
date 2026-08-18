using CRM.Application.DTOs.Progress;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// The drill-down must reconcile with the count it hangs off. Clicking a 4 and being shown
/// 6 names reads as the number being wrong, so both sides scope identically.
/// </summary>
public class CohortDrillDownTests
{
    private static readonly Guid SkillId = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid ProgA = Guid.Parse("22222222-0000-0000-0000-0000000000f2");
    private static readonly Guid ProgB = Guid.Parse("33333333-0000-0000-0000-0000000000f3");
    private const string Month = "2026-08";

    private readonly FakeUnitOfWork _uow = new();
    private readonly CohortRollUpService _service;

    public CohortDrillDownTests()
    {
        _uow.ProgramsRepo.Items.AddRange([
            new CrmProgram { Id = ProgA, Name = "MJC", Slug = "mjc" },
            new CrmProgram { Id = ProgB, Name = "Pathways", Slug = "pathways" },
        ]);
        _uow.SubSkillsRepo.Items.Add(new SubSkill { Id = SkillId, Name = "Projection", IsActive = true });
        _service = new CohortRollUpService(_uow);
    }

    private Guid AddStar(string name, Guid programId, Guid? secondary = null)
    {
        var id = Guid.NewGuid();
        _uow.ParticipantsRepo.Items.Add(new Participant
        {
            Id = id, FullName = name, Initials = name[..2].ToUpperInvariant(),
            ProgramId = programId, SecondaryProgramId = secondary,
        });
        return id;
    }

    private void Confirm(Guid starId, ProgressLevel level) =>
        _uow.MonthlyProgressSnapshotsRepo.Items.Add(new MonthlyProgressSnapshot
        {
            ParticipantId = starId, SubSkillId = SkillId, MonthKey = Month,
            Level = level, SuggestedLevel = level, IsConfirmed = true, ScoredWeekCount = 2, SummedScore = 4,
        });

    [Fact]
    public async Task Unconfirmed_levels_are_excluded_like_the_counts()
    {
        var star = AddStar("Alpha One", ProgA);
        _uow.MonthlyProgressSnapshotsRepo.Items.Add(new MonthlyProgressSnapshot
        {
            ParticipantId = star, SubSkillId = SkillId, MonthKey = Month,
            Level = ProgressLevel.Expert, SuggestedLevel = ProgressLevel.Expert, IsConfirmed = false,
        });

        var stars = await _service.GetStarsAtLevelAsync(Month, SkillId, ProgressLevel.Expert, null);
        Assert.Empty(stars);
    }

    [Fact]
    public async Task The_list_length_matches_the_count_for_the_same_scope()
    {
        Confirm(AddStar("Alpha One", ProgA), ProgressLevel.Expert);
        Confirm(AddStar("Beta Two", ProgA), ProgressLevel.Expert);
        Confirm(AddStar("Gamma Three", ProgB), ProgressLevel.Expert);
        // Dual-enrolled: primary B, secondary A. Must NOT leak into A's drill-down, because
        // A's count does not include them either.
        Confirm(AddStar("Delta Four", ProgB, secondary: ProgA), ProgressLevel.Expert);

        var rollUp = await _service.GetRollUpAsync(Month, ProgA);
        var row = rollUp.Rows.Single(r => r.SubSkillId == SkillId);
        var stars = await _service.GetStarsAtLevelAsync(Month, SkillId, ProgressLevel.Expert, ProgA);

        Assert.Equal(row.ExpertCount, stars.Count);
        Assert.Equal(2, stars.Count);
    }

    [Fact]
    public async Task Unscoped_returns_every_program()
    {
        Confirm(AddStar("Alpha One", ProgA), ProgressLevel.Novice);
        Confirm(AddStar("Gamma Three", ProgB), ProgressLevel.Novice);

        var stars = await _service.GetStarsAtLevelAsync(Month, SkillId, ProgressLevel.Novice, null);
        Assert.Equal(2, stars.Count);
        Assert.Equal(["Alpha One", "Gamma Three"], stars.Select(s => s.FullName).ToArray());
    }
}
