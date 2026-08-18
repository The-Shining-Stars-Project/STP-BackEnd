using CRM.Application.DTOs.Progress;
using CRM.Application.Interfaces.Services;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;

namespace CRM.Tests;

/// <summary>
/// Saving a weekly score refreshes that skill's month-end snapshot in the same save —
/// Rachel's request #1. Before this, the Month-end dropdown stayed blank until somebody
/// found the "Recompute" button, and a confirmed level relied on ComputeMonthEndAsync's
/// never-overwrite rule; the inline path must honour the same rule.
/// </summary>
public class ProgressAutoComputeTests
{
    private static readonly Guid StarId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid SkillId = Guid.Parse("bbbbbbbb-0000-0000-0000-0000000000b1");
    private static readonly Guid UserId = Guid.Parse("cccccccc-0000-0000-0000-0000000000c1");
    private const string Month = "2026-08";

    private sealed class AllowAllAccess : IProgramAccessService
    {
        private readonly Participant _star;
        public AllowAllAccess(Participant star) => _star = star;
        public Task<ProgramAccess> ForUserAsync(Guid userId) =>
            Task.FromResult(new ProgramAccess(true, new HashSet<Guid>()));
        public Task<Participant?> RequireParticipantAsync(Guid userId, Guid participantId) =>
            Task.FromResult<Participant?>(participantId == _star.Id ? _star : null);
    }

    private readonly FakeUnitOfWork _uow = new();
    private readonly ProgressTrackingService _service;

    public ProgressAutoComputeTests()
    {
        var star = new Participant { Id = StarId, FullName = "Test Star", ProgramId = Guid.NewGuid() };
        _uow.ParticipantsRepo.Items.Add(star);
        _uow.SubSkillsRepo.Items.Add(new SubSkill { Id = SkillId, Name = "Projection", IsActive = true });
        _uow.ScoreThresholdsRepo.Items.AddRange(
        [
            new ScoreThreshold { Level = ProgressLevel.Novice, MinAverage = 0.0 },
            new ScoreThreshold { Level = ProgressLevel.Intermediate, MinAverage = 1.5 },
            new ScoreThreshold { Level = ProgressLevel.Expert, MinAverage = 2.5 },
        ]);
        _service = new ProgressTrackingService(_uow, new AllowAllAccess(star), new FakeOrgClock());
    }

    private Task Record(int week, DataScore score) =>
        _service.RecordWeeklyScoreAsync(UserId, new RecordWeeklyScoreDto
        {
            ParticipantId = StarId, SubSkillId = SkillId, MonthKey = Month,
            WeekNumber = week, Score = score,
        });

    private MonthlyProgressSnapshot Snap() =>
        _uow.MonthlyProgressSnapshotsRepo.Items.Single(s =>
            s.ParticipantId == StarId && s.SubSkillId == SkillId && s.MonthKey == Month);

    [Fact]
    public async Task First_score_creates_the_snapshot_with_a_level()
    {
        await Record(1, DataScore.Independent); // 3 points → Expert

        var snap = Snap();
        Assert.Equal(ProgressLevel.Expert, snap.SuggestedLevel);
        Assert.Equal(ProgressLevel.Expert, snap.Level);
        Assert.False(snap.IsConfirmed);
        Assert.Equal(1, snap.ScoredWeekCount);
    }

    [Fact]
    public async Task Later_scores_move_the_unconfirmed_level()
    {
        await Record(1, DataScore.Independent);   // avg 3.0 → Expert
        await Record(2, DataScore.FullPrompts);   // avg 2.0 → Intermediate

        Assert.Equal(ProgressLevel.Intermediate, Snap().SuggestedLevel);
        Assert.Equal(ProgressLevel.Intermediate, Snap().Level);
    }

    [Fact]
    public async Task Correcting_the_same_week_does_not_double_count_it()
    {
        await Record(1, DataScore.Refusal);       // avg 0
        await Record(1, DataScore.Independent);   // same week corrected → avg 3.0, one week

        var snap = Snap();
        Assert.Equal(1, snap.ScoredWeekCount);
        Assert.Equal(ProgressLevel.Expert, snap.SuggestedLevel);
    }

    [Fact]
    public async Task A_confirmed_level_is_never_overwritten_by_a_new_score()
    {
        await Record(1, DataScore.Independent);
        var snap = Snap();
        snap.Level = ProgressLevel.Novice;       // teacher signed off a different call
        snap.IsConfirmed = true;

        await Record(2, DataScore.Refusal);      // suggestion moves, sign-off must not

        Assert.Equal(ProgressLevel.Novice, Snap().Level);
        Assert.True(Snap().IsConfirmed);
        Assert.NotEqual(ProgressLevel.Novice, Snap().SuggestedLevel);
    }

    [Fact]
    public async Task NA_weeks_do_not_drag_the_average_down()
    {
        await Record(1, DataScore.Independent);
        await Record(2, DataScore.NotApplicable);

        var snap = Snap();
        Assert.Equal(1, snap.ScoredWeekCount);
        Assert.Equal(ProgressLevel.Expert, snap.SuggestedLevel);
    }
}
