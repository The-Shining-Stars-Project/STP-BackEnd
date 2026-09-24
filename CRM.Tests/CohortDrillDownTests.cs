using CRM.Application.DTOs.Progress;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// The roll-up is derived from the month's weekly scores (average → threshold), with a
/// confirmed month-end level overriding the derived one. The drill-down must reconcile with
/// the count it hangs off: clicking a 4 and being shown 6 names reads as the number being
/// wrong, so both sides are built from the same per-(star, skill) map.
/// </summary>
public class CohortDrillDownTests
{
    private static readonly Guid SkillId = Guid.Parse("11111111-0000-0000-0000-0000000000f1");
    private static readonly Guid OtherSkillId = Guid.Parse("11111111-0000-0000-0000-0000000000f9");
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
        _uow.SubSkillsRepo.Items.AddRange([
            new SubSkill { Id = SkillId, Name = "Projection", IsActive = true },
            new SubSkill { Id = OtherSkillId, Name = "Eye contact", IsActive = true },
        ]);
        _uow.ScoreThresholdsRepo.Items.AddRange([
            new ScoreThreshold { Level = ProgressLevel.Novice, MinAverage = 0.0 },
            new ScoreThreshold { Level = ProgressLevel.Intermediate, MinAverage = 1.5 },
            new ScoreThreshold { Level = ProgressLevel.Expert, MinAverage = 2.5 },
        ]);
        _service = new CohortRollUpService(_uow, new FakeAllowAllAccess(_uow));
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

    /// <summary>One weekly score per week given, on <see cref="SkillId"/> unless told otherwise.</summary>
    private async Task Score(Guid starId, Guid? skillId = null, params DataScore[] weeks)
    {
        for (var i = 0; i < weeks.Length; i++)
            await _uow.WeeklyDataEntries.AddAsync(new WeeklyDataEntry
            {
                ParticipantId = starId, SubSkillId = skillId ?? SkillId, MonthKey = Month,
                WeekNumber = i + 1, WeekDate = new DateTime(2026, 8, 3 + 7 * i), Score = weeks[i],
            });
    }

    private void Confirm(Guid starId, ProgressLevel level) =>
        _uow.MonthlyProgressSnapshotsRepo.Items.Add(new MonthlyProgressSnapshot
        {
            ParticipantId = starId, SubSkillId = SkillId, MonthKey = Month,
            Level = level, SuggestedLevel = level, IsConfirmed = true, ScoredWeekCount = 2, SummedScore = 4,
        });

    [Fact]
    public async Task Weekly_scores_alone_populate_the_roll_up()
    {
        // Independent every week → average 3 → Expert, with nothing confirmed.
        var star = AddStar("Alpha One", ProgA);
        await Score(star, null, DataScore.Independent, DataScore.Independent);

        var rollUp = await _service.GetRollUpAsync(Guid.NewGuid(), Month, null);
        var row = rollUp.Rows.Single(r => r.SubSkillId == SkillId);

        Assert.Equal(1, row.ExpertCount);
        Assert.Equal(1, row.ScoredCount);
        Assert.Equal(1, rollUp.ParticipantCount);
        Assert.Equal(0, rollUp.ConfirmedCount);

        var stars = await _service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, SkillId, ProgressLevel.Expert, null);
        Assert.Equal(["Alpha One"], stars.Select(s => s.FullName).ToArray());
    }

    [Fact]
    public async Task A_confirmed_level_overrides_the_derived_one()
    {
        var star = AddStar("Alpha One", ProgA);
        await Score(star, null, DataScore.Independent, DataScore.Independent); // derives Expert
        Confirm(star, ProgressLevel.Intermediate);                             // teacher says otherwise

        var rollUp = await _service.GetRollUpAsync(Guid.NewGuid(), Month, null);
        var row = rollUp.Rows.Single(r => r.SubSkillId == SkillId);

        Assert.Equal(0, row.ExpertCount);
        Assert.Equal(1, row.IntermediateCount);
        Assert.Equal(1, rollUp.ConfirmedCount);
        Assert.Empty(await _service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, SkillId, ProgressLevel.Expert, null));
        Assert.Single(await _service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, SkillId, ProgressLevel.Intermediate, null));
    }

    [Fact]
    public async Task An_unconfirmed_snapshot_is_not_a_source_of_truth()
    {
        // A stale auto-computed snapshot with no weekly entries behind it must not count —
        // the scores are the record; snapshots only matter once confirmed.
        var star = AddStar("Alpha One", ProgA);
        _uow.MonthlyProgressSnapshotsRepo.Items.Add(new MonthlyProgressSnapshot
        {
            ParticipantId = star, SubSkillId = SkillId, MonthKey = Month,
            Level = ProgressLevel.Expert, SuggestedLevel = ProgressLevel.Expert, IsConfirmed = false,
        });

        var rollUp = await _service.GetRollUpAsync(Guid.NewGuid(), Month, null);
        Assert.Equal(0, rollUp.Rows.Single(r => r.SubSkillId == SkillId).ScoredCount);
        Assert.Empty(await _service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, SkillId, ProgressLevel.Expert, null));
    }

    [Fact]
    public async Task All_not_applicable_weeks_land_in_the_na_column_not_the_scored_count()
    {
        var star = AddStar("Alpha One", ProgA);
        await Score(star, null, DataScore.NotApplicable, DataScore.NotApplicable);

        var rollUp = await _service.GetRollUpAsync(Guid.NewGuid(), Month, null);
        var row = rollUp.Rows.Single(r => r.SubSkillId == SkillId);

        Assert.Equal(1, row.NotApplicableCount);
        Assert.Equal(0, row.ScoredCount);
        Assert.Equal(0, rollUp.ParticipantCount);
    }

    [Fact]
    public async Task The_list_length_matches_the_count_for_the_same_scope()
    {
        await Score(AddStar("Alpha One", ProgA), null, DataScore.Independent);
        await Score(AddStar("Beta Two", ProgA), null, DataScore.Independent);
        await Score(AddStar("Gamma Three", ProgB), null, DataScore.Independent);
        // Dual-enrolled: primary B, secondary A. Must NOT leak into A's drill-down, because
        // A's count does not include them either.
        await Score(AddStar("Delta Four", ProgB, secondary: ProgA), null, DataScore.Independent);

        var rollUp = await _service.GetRollUpAsync(Guid.NewGuid(), Month, ProgA);
        var row = rollUp.Rows.Single(r => r.SubSkillId == SkillId);
        var stars = await _service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, SkillId, ProgressLevel.Expert, ProgA);

        Assert.Equal(row.ExpertCount, stars.Count);
        Assert.Equal(2, stars.Count);
        Assert.Equal("MJC", rollUp.ProgramName);
    }

    [Fact]
    public async Task Unscoped_returns_every_program_and_counts_each_star_once()
    {
        var alpha = AddStar("Alpha One", ProgA);
        await Score(alpha, null, DataScore.Refusal);
        await Score(alpha, OtherSkillId, DataScore.Refusal); // a second skill is not a second star
        await Score(AddStar("Gamma Three", ProgB), null, DataScore.FullPrompts);

        var rollUp = await _service.GetRollUpAsync(Guid.NewGuid(), Month, null);
        Assert.Equal(2, rollUp.ParticipantCount);

        var stars = await _service.GetStarsAtLevelAsync(Guid.NewGuid(), Month, SkillId, ProgressLevel.Novice, null);
        Assert.Equal(["Alpha One", "Gamma Three"], stars.Select(s => s.FullName).ToArray());
    }

    [Fact]
    public async Task Another_months_scores_do_not_bleed_in()
    {
        var star = AddStar("Alpha One", ProgA);
        await _uow.WeeklyDataEntries.AddAsync(new WeeklyDataEntry
        {
            ParticipantId = star, SubSkillId = SkillId, MonthKey = "2026-07",
            WeekNumber = 1, WeekDate = new DateTime(2026, 7, 6), Score = DataScore.Independent,
        });

        var rollUp = await _service.GetRollUpAsync(Guid.NewGuid(), Month, null);
        Assert.Equal(0, rollUp.Rows.Single(r => r.SubSkillId == SkillId).ScoredCount);
    }
}
