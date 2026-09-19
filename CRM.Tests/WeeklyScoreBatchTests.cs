using CRM.Application.DTOs.Progress;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Sep 2026: the weekly grids save on a button instead of per cell. One request carries every
/// edit, the last edit to a cell wins, a null score clears the cell, and month-end levels are
/// re-derived from what is left.
/// </summary>
public class WeeklyScoreBatchTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid StarId = Guid.NewGuid();
    private static readonly Guid OtherStarId = Guid.NewGuid();
    private static readonly Guid SkillId = Guid.NewGuid();
    private static readonly Guid Skill2Id = Guid.NewGuid();
    private const string Month = "2026-09";

    private sealed class ScopedAccess : IProgramAccessService
    {
        private readonly Dictionary<Guid, Participant> _stars;
        public ScopedAccess(params Participant[] stars) => _stars = stars.ToDictionary(s => s.Id);
        public Task<ProgramAccess> ForUserAsync(Guid userId) => Task.FromResult(new ProgramAccess(true, new HashSet<Guid>()));
        public Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId) =>
            Task.FromResult(_stars.GetValueOrDefault(participantId));
    }

    private readonly FakeUnitOfWork _uow = new();
    private readonly ProgressTrackingService _service;

    public WeeklyScoreBatchTests()
    {
        var star = new Participant { Id = StarId, FullName = "A", ProgramId = Guid.NewGuid() };
        var other = new Participant { Id = OtherStarId, FullName = "B", ProgramId = star.ProgramId };
        _uow.ParticipantsRepo.Items.Add(star);
        _uow.ParticipantsRepo.Items.Add(other);
        _uow.SubSkillsRepo.Items.Add(new SubSkill { Id = SkillId, Name = "Projection", IsActive = true });
        _uow.SubSkillsRepo.Items.Add(new SubSkill { Id = Skill2Id, Name = "Focus", IsActive = true });
        _uow.ScoreThresholdsRepo.Items.AddRange(
        [
            new ScoreThreshold { Level = ProgressLevel.Novice, MinAverage = 0.0 },
            new ScoreThreshold { Level = ProgressLevel.Intermediate, MinAverage = 1.5 },
            new ScoreThreshold { Level = ProgressLevel.Expert, MinAverage = 2.5 },
        ]);
        _service = new ProgressTrackingService(_uow, new ScopedAccess(star, other), new FakeOrgClock());
    }

    private static WeeklyScoreChangeDto Change(Guid star, Guid skill, int week, DataScore? score) =>
        new() { ParticipantId = star, SubSkillId = skill, WeekNumber = week, Score = score };

    private IEnumerable<WeeklyDataEntry> Entries(Guid star, Guid skill) =>
        ((FakeRepository<WeeklyDataEntry>)_uow.WeeklyDataEntries).Items.Where(e => e.ParticipantId == star && e.SubSkillId == skill && e.MonthKey == Month);

    [Fact]
    public async Task Saves_many_cells_across_stars_and_skills_in_one_call()
    {
        var res = await _service.SaveWeeklyScoresAsync(UserId, new SaveWeeklyScoresDto
        {
            MonthKey = Month,
            Changes =
            {
                Change(StarId, SkillId, 1, DataScore.Independent),
                Change(StarId, SkillId, 2, DataScore.MinimalPrompts),
                Change(StarId, Skill2Id, 1, DataScore.Refusal),
                Change(OtherStarId, SkillId, 1, DataScore.FullPrompts),
            },
        });

        Assert.Equal(4, ((FakeRepository<WeeklyDataEntry>)_uow.WeeklyDataEntries).Items.Count);
        Assert.Equal(4, res.Entries.Count);
        Assert.Equal(3, res.Snapshots.Count);
        var snap = res.Snapshots.Single(s => s.ParticipantId == StarId && s.SubSkillId == SkillId);
        Assert.Equal(ProgressLevel.Expert, snap.SuggestedLevel); // avg 2.5
        Assert.Equal(2, snap.ScoredWeekCount);
    }

    [Fact]
    public async Task Last_edit_to_the_same_cell_wins()
    {
        await _service.SaveWeeklyScoresAsync(UserId, new SaveWeeklyScoresDto
        {
            MonthKey = Month,
            Changes = { Change(StarId, SkillId, 1, DataScore.Refusal), Change(StarId, SkillId, 1, DataScore.Independent) },
        });

        var entry = Assert.Single(Entries(StarId, SkillId));
        Assert.Equal(DataScore.Independent, entry.Score);
    }

    [Fact]
    public async Task Null_score_clears_the_cell_and_rederives_the_level()
    {
        await _service.SaveWeeklyScoresAsync(UserId, new SaveWeeklyScoresDto
        {
            MonthKey = Month,
            Changes = { Change(StarId, SkillId, 1, DataScore.Independent), Change(StarId, SkillId, 2, DataScore.Refusal) },
        });

        var res = await _service.SaveWeeklyScoresAsync(UserId, new SaveWeeklyScoresDto
        {
            MonthKey = Month,
            Changes = { Change(StarId, SkillId, 2, null) },
        });

        var entry = Assert.Single(Entries(StarId, SkillId));
        Assert.Equal(1, entry.WeekNumber);
        var snap = Assert.Single(res.Snapshots);
        Assert.Equal(ProgressLevel.Expert, snap.SuggestedLevel);
        Assert.Equal(1, snap.ScoredWeekCount);
        Assert.Single(res.Entries);
    }

    [Fact]
    public async Task Clearing_every_score_leaves_the_level_not_applicable_but_keeps_a_confirmed_level()
    {
        await _service.SaveWeeklyScoresAsync(UserId, new SaveWeeklyScoresDto
        {
            MonthKey = Month, Changes = { Change(StarId, SkillId, 1, DataScore.Independent) },
        });
        await _service.ConfirmMonthEndAsync(UserId, StarId, Month, new ConfirmMonthEndDto { SubSkillId = SkillId, Level = ProgressLevel.Intermediate });

        var res = await _service.SaveWeeklyScoresAsync(UserId, new SaveWeeklyScoresDto
        {
            MonthKey = Month, Changes = { Change(StarId, SkillId, 1, null) },
        });

        Assert.Empty(Entries(StarId, SkillId));
        var snap = Assert.Single(res.Snapshots);
        Assert.Equal(ProgressLevel.NotApplicable, snap.SuggestedLevel);
        Assert.Equal(ProgressLevel.Intermediate, snap.Level);
        Assert.True(snap.IsConfirmed);
    }

    [Fact]
    public async Task Unknown_stars_are_skipped_and_clearing_an_empty_cell_is_a_no_op()
    {
        var res = await _service.SaveWeeklyScoresAsync(UserId, new SaveWeeklyScoresDto
        {
            MonthKey = Month,
            Changes = { Change(Guid.NewGuid(), SkillId, 1, DataScore.Independent), Change(StarId, SkillId, 3, null) },
        });

        Assert.Empty(((FakeRepository<WeeklyDataEntry>)_uow.WeeklyDataEntries).Items);
        Assert.Empty(res.Entries);
    }
}
