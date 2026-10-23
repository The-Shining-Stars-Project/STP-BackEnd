using CRM.Application.DTOs.Games;
using CRM.Application.Services;
using CRM.Domain.Entities;
using CRM.Domain.Enums;
using Xunit;

namespace CRM.Tests;

/// <summary>
/// Oct 2026 (client): activities can be deleted and promoted ideas demoted; modifications can
/// be edited, deleted and attached to a library game. Delete never empties the library, because
/// the seeder would reload the defaults on the next restart.
/// </summary>
public class GamesLibraryDeleteTests
{
    private readonly FakeUnitOfWork _uow = new();
    private readonly GameService _games;
    private readonly GameBacklogService _backlog;
    private readonly Guid _areaId = Guid.NewGuid();

    public GamesLibraryDeleteTests()
    {
        _games = new GameService(_uow);
        _backlog = new GameBacklogService(_uow, _games);
        _uow.ObjectiveAreas.AddAsync(new ObjectiveArea { Id = _areaId, Name = "Multi-Area", Slug = "multi-area" }).GetAwaiter().GetResult();
    }

    private Game SeedGame(string name)
    {
        var game = new Game { Id = Guid.NewGuid(), Name = name, PrimaryObjectiveAreaId = _areaId, Tiers = GameTier.All };
        ((FakeRepository<Game>)_uow.Games).Items.Add(game);
        return game;
    }

    private List<Game> Games => ((FakeRepository<Game>)_uow.Games).Items;
    private List<GameSubGoal> SubGoals => ((FakeRepository<GameSubGoal>)_uow.GameSubGoals).Items;
    private List<AgeModification> Mods => ((FakeRepository<AgeModification>)_uow.AgeModifications).Items;
    private List<GameIdea> Ideas => ((FakeRepository<GameIdea>)_uow.GameIdeas).Items;

    // ── Delete activity ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_sub_goals_unlinks_modifications_and_returns_the_idea()
    {
        var keep = SeedGame("Zip Zap Zop");
        var gone = SeedGame("Mirror");
        SubGoals.Add(new GameSubGoal { GameId = gone.Id, SubSkillId = Guid.NewGuid() });
        SubGoals.Add(new GameSubGoal { GameId = keep.Id, SubSkillId = Guid.NewGuid() });
        Mods.Add(new AgeModification { GameName = "Mirror", Modification = "Slower", GameId = gone.Id });
        Ideas.Add(new GameIdea { Name = "Mirror", PromotedGameId = gone.Id });

        Assert.True(await _games.DeleteAsync(gone.Id));

        Assert.Single(Games);
        Assert.Equal(keep.Id, Assert.Single(SubGoals).GameId);
        Assert.Null(Assert.Single(Mods).GameId);            // kept, unlinked
        Assert.Null(Assert.Single(Ideas).PromotedGameId);   // back to To Develop
        Assert.False(await _games.DeleteAsync(gone.Id));    // 404 the second time
    }

    [Fact]
    public async Task The_last_game_cannot_be_deleted()
    {
        var only = SeedGame("Zip Zap Zop");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _games.DeleteAsync(only.Id));

        Assert.Contains("can't be emptied", ex.Message);
        Assert.Single(Games);
    }

    // ── Demote ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Demote_deletes_the_promoted_game_and_is_idempotent()
    {
        SeedGame("Zip Zap Zop");
        var promoted = SeedGame("Statues");
        var idea = new GameIdea { Id = Guid.NewGuid(), Name = "Statues", PromotedGameId = promoted.Id };
        Ideas.Add(idea);

        var dto = await _backlog.DemoteIdeaAsync(idea.Id);

        Assert.Null(dto!.PromotedGameId);
        Assert.DoesNotContain(Games, g => g.Id == promoted.Id);
        Assert.Null((await _backlog.DemoteIdeaAsync(idea.Id))!.PromotedGameId); // already demoted: no-op
        Assert.Null(await _backlog.DemoteIdeaAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Demote_still_clears_the_pointer_when_the_game_is_already_gone()
    {
        SeedGame("Zip Zap Zop");
        var idea = new GameIdea { Id = Guid.NewGuid(), Name = "Statues", PromotedGameId = Guid.NewGuid() };
        Ideas.Add(idea);

        Assert.Null((await _backlog.DemoteIdeaAsync(idea.Id))!.PromotedGameId);
    }

    // ── Modifications ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_modification_can_be_edited_and_attached_to_a_game()
    {
        var game = SeedGame("Mirror");
        var mod = new AgeModification { Id = Guid.NewGuid(), GameName = "Miror", GroupAgeLevel = "Novice", Modification = "Slower" };
        Mods.Add(mod);

        var dto = await _backlog.UpdateAgeModAsync(mod.Id, new UpdateAgeModificationDto
        {
            GameName = " Mirror ", GroupAgeLevel = "Intermediate", Modification = "Pairs face each other", GameId = game.Id,
        });

        Assert.Equal("Mirror", dto!.GameName);
        Assert.Equal("Intermediate", dto.GroupAgeLevel);
        Assert.Equal(game.Id, dto.GameId);

        var detail = await _games.GetByIdAsync(game.Id);
        Assert.Equal(mod.Id, Assert.Single(detail!.Modifications).Id);

        var unlinked = await _backlog.UpdateAgeModAsync(mod.Id, new UpdateAgeModificationDto { GameName = "Mirror", Modification = "Pairs", GameId = null });
        Assert.Null(unlinked!.GameId);
        Assert.Empty((await _games.GetByIdAsync(game.Id))!.Modifications);
    }

    [Fact]
    public async Task Editing_rejects_blank_fields_and_unknown_games()
    {
        var mod = new AgeModification { Id = Guid.NewGuid(), GameName = "Mirror", Modification = "Slower" };
        Mods.Add(mod);

        await Assert.ThrowsAsync<ArgumentException>(() => _backlog.UpdateAgeModAsync(mod.Id, new UpdateAgeModificationDto { GameName = "", Modification = "x" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _backlog.UpdateAgeModAsync(mod.Id, new UpdateAgeModificationDto { GameName = "Mirror", Modification = "x", GameId = Guid.NewGuid() }));
        Assert.Null(await _backlog.UpdateAgeModAsync(Guid.NewGuid(), new UpdateAgeModificationDto { GameName = "Mirror", Modification = "x" }));
        Assert.Equal("Slower", mod.Modification);
    }

    [Fact]
    public async Task A_modification_can_be_deleted()
    {
        var mod = new AgeModification { Id = Guid.NewGuid(), GameName = "Mirror", Modification = "Slower" };
        Mods.Add(mod);

        Assert.True(await _backlog.DeleteAgeModAsync(mod.Id));
        Assert.Empty(Mods);
        Assert.False(await _backlog.DeleteAgeModAsync(mod.Id));
    }
}
